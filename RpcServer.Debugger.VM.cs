// include this file in neo-modules/src/RpcServer/RpcServer.csproj
// and build your own RpcServer

using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Neo.Plugins
{
    public partial class RpcServer
    {
        readonly ConcurrentDictionary<string, ApplicationEngine> debugSessionToEngine = new();
        enum BreakReason
        {
            None=0,
            AssemblyBreakpoint=1<<0,
            SourceCodeBreakpoint=1<<1,
            Call=1<<2,
            Return=1<<3,
            SourceCode=1<<4
        }

        [RpcMethod]
        protected virtual JObject DebugFunctionWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            UInt160 script_hash = UInt160.Parse(_params[2].AsString());
            string operation = _params[3].AsString();
            ContractParameter[] args = _params.Count >= 5 ? ((JArray)_params[4]).Select(p => ContractParameter.FromJson(p)).ToArray() : System.Array.Empty<ContractParameter>();
            Signers signers = _params.Count >= 6 ? SignersFromJson((JArray)_params[5], system.Settings) : null;

            byte[] script;
            using (ScriptBuilder sb = new())
            {
                script = sb.EmitDynamicCall(script_hash, operation, args).ToArray();
            }
            Transaction tx = signers == null ? null : new Transaction
            {
                Signers = signers.GetSigners(),
                Attributes = System.Array.Empty<TransactionAttribute>(),
                Witnesses = signers.Witnesses,
            };
            ulong timestamp;
            if (!sessionToTimestamp.TryGetValue(session, out timestamp))  // we allow initializing a new session when executing
                sessionToTimestamp[session] = 0;
            ApplicationEngine oldEngine, newEngine;
            logs.Clear();
            ApplicationEngine.Log += CacheLog;
            BreakReason breakReason = BreakReason.None;
            if (timestamp == 0)
            {
                if (sessionToEngine.TryGetValue(session, out oldEngine))
                {
                    newEngine = DebugRun(script, oldEngine.Snapshot.CreateSnapshot(), out breakReason, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
                }
                else
                {
                    newEngine = DebugRun(script, system.StoreView, out breakReason, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
                }
            }
            else
            {
                oldEngine = debugSessionToEngine[session];
                newEngine = DebugRun(script, oldEngine.Snapshot.CreateSnapshot(), out breakReason, persistingBlock: CreateDummyBlockWithTimestamp(oldEngine.Snapshot, system.Settings, timestamp: timestamp), container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
            }
            ApplicationEngine.Log -= CacheLog;
            if (writeSnapshot && newEngine.State != VMState.FAULT)
                debugSessionToEngine[session] = newEngine;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JObject DebugContinue(JArray _params)
        {
            string session = _params[0].AsString();
            ApplicationEngine newEngine = debugSessionToEngine[session];
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            ApplicationEngine.Log += CacheLog;
            Execute(newEngine, out breakReason);
            ApplicationEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private JObject DumpDebugResultJson(JObject json, ApplicationEngine newEngine, BreakReason breakReason)
        {
            json["state"] = newEngine.State;
            json["breakreason"] = breakReason;
            json["scripthash"] = newEngine.CurrentScriptHash?.ToString();
            json["instructionpointer"] = newEngine.CurrentContext?.InstructionPointer;
            if ((breakReason & BreakReason.SourceCode) > 0 || (breakReason & BreakReason.SourceCodeBreakpoint) > 0)
            {
                SourceFilenameAndLineNum sourceCodeBreakpoint = contractScriptHashToInstructionPointerToSourceLineNum[newEngine.CurrentScriptHash][(uint)newEngine.CurrentContext.InstructionPointer];
                json["sourcefilename"] = sourceCodeBreakpoint.SourceFilename;
                json["sourcelinenum"] = sourceCodeBreakpoint.LineNum;
            }
            else
            {
                json["sourcefilename"] = null;
                json["sourcelinenum"] = null;
            }
            json["gasconsumed"] = newEngine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(newEngine.FaultException);
            if (json["exception"] != null)
            {
                string traceback = $"{json["exception"].GetString()}\r\nCallingScriptHash={newEngine.CallingScriptHash}\r\nCurrentScriptHash={newEngine.CurrentScriptHash}\r\nEntryScriptHash={newEngine.EntryScriptHash}\r\n";
                traceback += newEngine.FaultException.StackTrace;
                foreach (ExecutionContext context in newEngine.InvocationStack)
                {
                    traceback += $"\r\nInstructionPointer={context.InstructionPointer}, OpCode {context.CurrentInstruction.OpCode}, Script Length={context.Script.Length}";
                }
                if (!logs.IsEmpty)
                {
                    traceback += $"\r\n-------Logs-------({logs.Count})";
                }
                foreach (LogEventArgs log in logs)
                {
                    string contractName = NativeContract.ContractManagement.GetContract(newEngine.Snapshot, log.ScriptHash).Manifest.Name;
                    traceback += $"\r\n[{log.ScriptHash}] {contractName}: {log.Message}";
                }
                json["traceback"] = traceback;
            }
            try
            {
                json["stack"] = new JArray(newEngine.ResultStack.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
            }
            catch (InvalidOperationException)
            {
                json["stack"] = "error: invalid operation";
            }
            return json;
        }

        private JObject DumpDebugResultJson(ApplicationEngine newEngine, BreakReason breakReason)
        {
            return DumpDebugResultJson(new JObject(), newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JObject ListDebugSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in debugSessionToEngine.Keys)
            {
                session.Add(s);
            }
            return session;
        }

        [RpcMethod]
        protected virtual JObject DeleteDebugSnapshots(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                json[session] = debugSessionToEngine.Remove(session, out _);
            }
            return json;
        }

        private ApplicationEngine DebugRun(byte[] script, DataCache snapshot, out BreakReason breakReason, IVerifiable container = null, Block persistingBlock = null, ProtocolSettings settings = null, int offset = 0, long gas = ApplicationEngine.TestModeGas, Diagnostic diagnostic = null)
        {
            persistingBlock ??= CreateDummyBlockWithTimestamp(snapshot, settings ?? ProtocolSettings.Default, timestamp: 0);
            ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, container, snapshot, persistingBlock, settings, gas, diagnostic);
            engine.LoadScript(script, initialPosition: offset);
            return Execute(engine, out breakReason);
        }

        private ApplicationEngine ExecuteAndCheck(ApplicationEngine engine, out BreakReason actualBreakReason,
            BreakReason requiredBreakReason = BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint)
        {
            actualBreakReason = BreakReason.None;
            if (engine.State == VMState.HALT || engine.State == VMState.FAULT)
                return engine;
            OpCode currentOpCode = engine.CurrentContext.CurrentInstruction.OpCode;
            if ((requiredBreakReason & BreakReason.Call) > 0 &&
               (currentOpCode == OpCode.CALL || currentOpCode == OpCode.CALLA || currentOpCode == OpCode.CALLT || currentOpCode == OpCode.CALL_L
             || currentOpCode == OpCode.SYSCALL))
            {
                engine.ExecuteNext();
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.Call;
                return engine;
            }
            if ((requiredBreakReason & BreakReason.Return) > 0 && currentOpCode == OpCode.RET)
            {
                engine.ExecuteNext();
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.Return;
                return engine;
            }
            engine.ExecuteNext();
            if (engine.State == VMState.HALT || engine.State == VMState.FAULT)
                return engine;
            UInt160 currentScriptHash = engine.CurrentScriptHash;
            uint currentInstructionPointer = (uint)engine.CurrentContext.InstructionPointer;
            if ((requiredBreakReason & BreakReason.AssemblyBreakpoint) > 0)
            {
                if (contractScriptHashToAssemblyBreakpoints.ContainsKey(currentScriptHash)
                 && contractScriptHashToAssemblyBreakpoints[currentScriptHash]
                    .Contains(currentInstructionPointer))
                {
                    engine.State = VMState.BREAK;
                    actualBreakReason |= BreakReason.AssemblyBreakpoint;
                    return engine;
                }
            }
            if ((requiredBreakReason & BreakReason.SourceCodeBreakpoint) > 0)
            {
                if (contractScriptHashToSourceCodeBreakpoints.ContainsKey(currentScriptHash)
                 && contractScriptHashToInstructionPointerToSourceLineNum[currentScriptHash].ContainsKey(currentInstructionPointer)
                 && contractScriptHashToSourceCodeBreakpoints[currentScriptHash]
                    .Contains(contractScriptHashToInstructionPointerToSourceLineNum[currentScriptHash][currentInstructionPointer]))
                {
                    engine.State = VMState.BREAK;
                    actualBreakReason |= BreakReason.SourceCodeBreakpoint;
                    return engine;
                }
            }
            if ((requiredBreakReason & BreakReason.SourceCode) > 0)
            {
                if (contractScriptHashToSourceLineNums.ContainsKey(currentScriptHash)
                 && contractScriptHashToInstructionPointerToSourceLineNum[currentScriptHash].ContainsKey(currentInstructionPointer)
                 && contractScriptHashToSourceLineNums[currentScriptHash]
                    .Contains(contractScriptHashToInstructionPointerToSourceLineNum[currentScriptHash][currentInstructionPointer]))
                {
                    engine.State = VMState.BREAK;
                    actualBreakReason |= BreakReason.SourceCode;
                    return engine;
                }
            }
            return engine;
        }

        private ApplicationEngine Execute(ApplicationEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason);
            return engine;
        }
        private ApplicationEngine StepInto(ApplicationEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint & BreakReason.SourceCodeBreakpoint & BreakReason.Call);
            return engine;
        }

        private ApplicationEngine StepOut(ApplicationEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint & BreakReason.SourceCodeBreakpoint & BreakReason.Return);
            return engine;
        }

        private ApplicationEngine StepOver(ApplicationEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            UInt160 prevScriptHash = engine.CurrentScriptHash;
            int invocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint & BreakReason.SourceCodeBreakpoint & BreakReason.SourceCode);
                if (engine.State == VMState.BREAK)
                    if ((breakReason & BreakReason.AssemblyBreakpoint) > 0 || (breakReason & BreakReason.SourceCodeBreakpoint) > 0)
                        break;
                    if ((breakReason & BreakReason.SourceCode) > 0 && engine.InvocationStack.Count == invocationStackCount && engine.CurrentScriptHash == prevScriptHash)
                        break;
                else
                    engine.State = VMState.NONE;
            }
            return engine;
        }
    }
}
