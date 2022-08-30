using Neo.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;

namespace Neo.Plugins
{
    public partial class Fairy
    {
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
        protected virtual JToken DebugFunctionWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            UInt160 script_hash = UInt160.Parse(_params[2].AsString());
            string operation = _params[3].AsString();
            ContractParameter[] args = _params.Count >= 5 ? ((JArray)_params[4]).Select(p => ContractParameter.FromJson((JObject)p)).ToArray() : System.Array.Empty<ContractParameter>();
            Signers? signers = _params.Count >= 6 ? SignersFromJson((JArray)_params[5], system.Settings) : null;

            byte[] script;
            using (ScriptBuilder sb = new())
            {
                script = sb.EmitDynamicCall(script_hash, operation, args).ToArray();
            }
            Transaction? tx = signers == null ? null : new Transaction
            {
                Signers = signers.GetSigners(),
                Attributes = System.Array.Empty<TransactionAttribute>(),
                Witnesses = signers.Witnesses,
            };
            FairySession testSession;
            if (!sessionStringToFairySession.TryGetValue(session, out testSession))
            {  // we allow initializing a new session when executing
                testSession = NewTestSession();
                sessionStringToFairySession[session] = testSession;
            }
            FairyEngine newEngine;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            BreakReason breakReason = BreakReason.None;
            newEngine = DebugRun(script, testSession.engine.Snapshot.CreateSnapshot(), out breakReason, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: testSession.engine);
            FairyEngine.Log -= CacheLog;
            if (writeSnapshot)
                sessionStringToFairySession[session].debugEngine = newEngine;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JToken DebugContinue(JArray _params)
        {
            string session = _params[0].AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            Execute(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private JObject DumpDebugResultJson(JObject json, FairyEngine newEngine, BreakReason breakReason)
        {
            json["state"] = newEngine.State;
            json["breakreason"] = breakReason;
            json["scripthash"] = newEngine.CurrentScriptHash?.ToString();
            json["contractname"] = newEngine.CurrentScriptHash != null ? NativeContract.ContractManagement.GetContract(newEngine.Snapshot, newEngine.CurrentScriptHash)?.Manifest.Name : null;
            json["instructionpointer"] = newEngine.CurrentContext?.InstructionPointer;
            try
            {
                SourceFilenameAndLineNum sourceCodeBreakpoint = contractScriptHashToAllInstructionPointerToSourceLineNum[newEngine.CurrentScriptHash][(uint)newEngine.CurrentContext.InstructionPointer];
                json["sourcefilename"] = sourceCodeBreakpoint.sourceFilename;
                json["sourcelinenum"] = sourceCodeBreakpoint.lineNum;
                json["sourcecontent"] = sourceCodeBreakpoint.sourceContent;
            }
            catch
            {
                json["sourcefilename"] = null;
                json["sourcelinenum"] = null;
                json["sourcecontent"] = null;
            }
            json["gasconsumed"] = newEngine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(newEngine.FaultException);
            if (json["exception"] != null)
            {
                string traceback = $"{json["exception"].GetString()}\r\nCallingScriptHash={newEngine.CallingScriptHash}\r\nCurrentScriptHash={newEngine.CurrentScriptHash}\r\nEntryScriptHash={newEngine.EntryScriptHash}\r\n";
                traceback += newEngine.FaultException.StackTrace;
                foreach (Neo.VM.ExecutionContext context in newEngine.InvocationStack)
                {
                    UInt160 contextScriptHash = context.GetScriptHash();
                    try
                    {
                        string sourceCodeTraceback = "";
                        SourceFilenameAndLineNum sourceCode = contractScriptHashToAllInstructionPointerToSourceLineNum[contextScriptHash][(uint)context.InstructionPointer];
                        sourceCodeTraceback += $"\r\nFile {sourceCode.sourceFilename}, line {sourceCode.lineNum}: {sourceCode.sourceContent}";
                        traceback += sourceCodeTraceback;
                    }catch(Exception _) {; }
                    traceback += $"\r\n\tScriptHash={contextScriptHash}, InstructionPointer={context.InstructionPointer}, OpCode {context.CurrentInstruction?.OpCode}, Script Length={context.Script.Length}";
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

        private JObject DumpDebugResultJson(FairyEngine newEngine, BreakReason breakReason)
        {
            return DumpDebugResultJson(new JObject(), newEngine, breakReason);
        }

        private FairyEngine DebugRun(byte[] script, DataCache snapshot, out BreakReason breakReason, IVerifiable? container = null, Block? persistingBlock = null, ProtocolSettings? settings = null, int offset = 0, long gas = FairyEngine.TestModeGas, IDiagnostic? diagnostic = null, FairyEngine oldEngine = null)
        {
            persistingBlock ??= CreateDummyBlockWithTimestamp(snapshot, settings ?? ProtocolSettings.Default, timestamp: 0);
            FairyEngine engine = FairyEngine.Create(TriggerType.Application, container, snapshot, persistingBlock, settings, gas, diagnostic, oldEngine: oldEngine);
            engine.LoadScript(script, initialPosition: offset);
            return Execute(engine, out breakReason);
        }

        private FairyEngine ExecuteAndCheck(FairyEngine engine, out BreakReason actualBreakReason,
            BreakReason requiredBreakReason = BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint)
        {
            actualBreakReason = BreakReason.None;
            if (engine.State == VMState.HALT || engine.State == VMState.FAULT)
                return engine;
            OpCode currentOpCode = engine.CurrentContext.CurrentInstruction.OpCode;
            if ((requiredBreakReason & BreakReason.Call) > 0 &&
               (currentOpCode == OpCode.CALL || currentOpCode == OpCode.CALLA || currentOpCode == OpCode.CALLT || currentOpCode == OpCode.CALL_L
             || (currentOpCode == OpCode.SYSCALL && engine.CurrentContext.CurrentInstruction.TokenU32 == ApplicationEngine.System_Contract_Call.Hash)))
            {
                engine.ExecuteNext();
                if (engine.CurrentContext.CurrentInstruction.OpCode == OpCode.INITSLOT)
                    engine.ExecuteNext();
                else
                    return engine;
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
            uint currentInstructionPointer = (uint)engine.CurrentContext.InstructionPointer;
            engine.ExecuteNext();
            if (contractScriptHashToInstructionPointerToCoverage.ContainsKey(engine.CurrentScriptHash) && contractScriptHashToInstructionPointerToCoverage[engine.CurrentScriptHash].ContainsKey(currentInstructionPointer))
                contractScriptHashToInstructionPointerToCoverage[engine.CurrentScriptHash][currentInstructionPointer] = true;
            if (engine.State == VMState.HALT || engine.State == VMState.FAULT)
                return engine;
            UInt160 currentScriptHash = engine.CurrentScriptHash;
            currentInstructionPointer = (uint)engine.CurrentContext.InstructionPointer;
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

        private FairyEngine Execute(FairyEngine engine, out BreakReason breakReason)
        {
            if (engine.State == VMState.HALT)
                throw new ArgumentException("Engine HALTed. You have probably finished running this debug session. Call DebugFunctionWithSession to start again.");
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason);
            return engine;
        }

        private FairyEngine StepInto(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.Call);
            return engine;
        }

        [RpcMethod]
        protected virtual JToken DebugStepInto(JArray _params)
        {
            string session = _params[0].AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            StepInto(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private FairyEngine StepOut(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            while (engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.Return);
            return engine;
        }

        [RpcMethod]
        protected virtual JToken DebugStepOut(JArray _params)
        {
            string session = _params[0].AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            StepOut(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private FairyEngine StepOverSourceCode(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            UInt160 prevScriptHash = engine.CurrentScriptHash;
            int invocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.SourceCode);
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

        [RpcMethod]
        protected virtual JToken DebugStepOverSourceCode(JArray _params)
        {
            string session = _params[0].AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            StepOverSourceCode(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JToken DebugStepOverAssembly(JArray _params)
        {
            string session = _params[0].AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog;
            ExecuteAndCheck(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog;
            return DumpDebugResultJson(newEngine, BreakReason.None);
        }

        [RpcMethod]
        protected virtual JToken DebugStepOver(JArray _params)
        {
            return DebugStepOverSourceCode(_params);
        }

        [RpcMethod]
        protected virtual JToken GetLocalVariables(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).LocalVariables.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetArguments(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).Arguments.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetStaticFields(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).StaticFields.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetEvaluationStack(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).EvaluationStack.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetInstructionPointer(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).InstructionPointer);
        }

        [RpcMethod]
        protected virtual JToken GetVariableValueByName(JArray _params)
        {
            string session = _params[0].AsString();
            string variableName = _params[1].AsString();
            int invocationStackIndex = _params.Count > 2 ? int.Parse(_params[2].AsString()) : 0;
            return GetVariableNamesAndValues(new JArray(session, invocationStackIndex))[variableName];
        }

        [RpcMethod]
        protected virtual JToken GetVariableNamesAndValues(JArray _params)
        {
            string session = _params[0].AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1].AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine;
            Neo.VM.ExecutionContext invocationStackItem = newEngine.InvocationStack.ElementAt(invocationStackIndex);
            UInt160 invocationStackScriptHash = invocationStackItem.GetScriptHash();
            int instructionPointer = invocationStackItem.InstructionPointer;
            JToken method = GetMethodByInstructionPointer(new JArray(invocationStackScriptHash.ToString(), instructionPointer));
            JObject returnedJson = new();
            JArray staticVariables = (JArray)contractScriptHashToNefDbgNfo[invocationStackScriptHash]["static-variables"];
            foreach (JObject param in staticVariables)
            {
                string[] nameTypeAndIndex = param.AsString().Split(',');
                int index = int.Parse(nameTypeAndIndex[2]);
                returnedJson[nameTypeAndIndex[0]] =  invocationStackItem.StaticFields[index].ToJson();
            }
            if (method != JObject.Null)
            {
                foreach (JToken param in (JArray)method["params"])
                {
                    string[] nameTypeAndIndex = param.AsString().Split(',');
                    int index = int.Parse(nameTypeAndIndex[2]);
                    returnedJson[nameTypeAndIndex[0]] = invocationStackItem.Arguments[index].ToJson();
                }
                foreach (JToken param in (JArray)method["variables"])
                {
                    string[] nameTypeAndIndex = param.AsString().Split(',');
                    int index = int.Parse(nameTypeAndIndex[2]);
                    returnedJson[nameTypeAndIndex[0]] = invocationStackItem.LocalVariables[index].ToJson();
                }
            }
            return returnedJson;
        }
    }
}
