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
        [Flags]
        enum BreakReason
        {
            None = 0,
            AssemblyBreakpoint = 1 << 0,
            SourceCodeBreakpoint = 1 << 1,
            Call = 1 << 2,
            Return = 1 << 3,
            SourceCode = 1 << 4
        }

        [RpcMethod]
        protected virtual JToken DebugFunctionWithSession(JArray _params)
        {
            string session = _params[0]!.AsString();
            bool writeSnapshot = _params[1]!.AsBoolean();
            UInt160 script_hash = UInt160.Parse(_params[2]!.AsString());
            string operation = _params[3]!.AsString();
            ContractParameter[] args = _params.Count >= 5 ? ((JArray)_params[4]!).Select(p => ContractParameter.FromJson((JObject)p!)).ToArray() : System.Array.Empty<ContractParameter>();
            Signer[]? signers = _params.Count >= 6 ? SignersFromJson((JArray)_params[5]!, system.Settings) : null;
            Witness[]? witnesses = _params.Count >= 6 ? WitnessesFromJson((JArray)_params[5]!) : null;

            byte[] script;
            using (ScriptBuilder sb = new())
            {
                script = sb.EmitDynamicCall(script_hash, operation, args).ToArray();
            }
            Transaction? tx = signers == null ? null : new Transaction
            {
                Signers = signers,
                Attributes = System.Array.Empty<TransactionAttribute>(),
                Witnesses = witnesses,
            };
            FairySession testSession = GetOrCreateFairySession(session);
            FairyEngine newEngine;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            BreakReason breakReason = BreakReason.None;
            newEngine = DebugRun(script, testSession.engine.Snapshot.CreateSnapshot(), out breakReason, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: testSession.engine);
            FairyEngine.Log -= CacheLog!;
            if (writeSnapshot)
                sessionStringToFairySession[session].debugEngine = newEngine;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JToken DebugScriptWithSession(JArray _params)
        {
            string session = _params[0]!.AsString();
            bool writeSnapshot = _params[1]!.AsBoolean();
            byte[] script = Convert.FromBase64String(_params[2]!.AsString());
            Signer[]? signers = _params.Count >= 4 ? SignersFromJson((JArray)_params[3]!, system.Settings) : null;
            Witness[]? witnesses = _params.Count >= 5 ? WitnessesFromJson((JArray)_params[4]!) : null;
            Transaction? tx = signers == null ? null : new Transaction
            {
                Signers = signers,
                Attributes = System.Array.Empty<TransactionAttribute>(),
                Witnesses = witnesses,
            };
            FairySession testSession = GetOrCreateFairySession(session);
            FairyEngine newEngine;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            BreakReason breakReason = BreakReason.None;
            newEngine = DebugRun(script, testSession.engine.Snapshot.CreateSnapshot(), out breakReason, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: testSession.engine);
            FairyEngine.Log -= CacheLog!;
            if (writeSnapshot)
                sessionStringToFairySession[session].debugEngine = newEngine;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JToken DebugContinue(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            Execute(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog!;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private void GetSourceCode(JObject json, UInt160? scripthash, uint? instructionPointer)
        {
            if (scripthash != null && instructionPointer != null
                && contractScriptHashToAllInstructionPointerToSourceLineNum.ContainsKey(scripthash)
                && contractScriptHashToAllInstructionPointerToSourceLineNum[scripthash].ContainsKey((uint)instructionPointer))
            {
                SourceFilenameAndLineNum sourceCodeBreakpoint = contractScriptHashToAllInstructionPointerToSourceLineNum[scripthash][(uint)instructionPointer];
                json["sourcefilename"] = sourceCodeBreakpoint.sourceFilename;
                json["sourcelinenum"] = sourceCodeBreakpoint.lineNum;
                json["sourcecontent"] = sourceCodeBreakpoint.sourceContent;
            }
            else
            {
                json["sourcefilename"] = null;
                json["sourcelinenum"] = null;
                json["sourcecontent"] = null;
            }
        }

        private JObject DumpDebugResultJson(JObject json, FairyEngine newEngine, BreakReason breakReason)
        {
            json["state"] = newEngine.State;
            json["breakreason"] = breakReason;
            json["scripthash"] = newEngine.CurrentScriptHash?.ToString();
            json["contractname"] = newEngine.CurrentScriptHash != null ? NativeContract.ContractManagement.GetContract(newEngine.Snapshot, newEngine.CurrentScriptHash)?.Manifest.Name : null;
            json["instructionpointer"] = newEngine.CurrentContext?.InstructionPointer;
            GetSourceCode(json, newEngine.CurrentScriptHash, (uint?)newEngine.CurrentContext?.InstructionPointer);
            json["gasconsumed"] = newEngine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(newEngine.FaultException);
            if (json["exception"] != null)
            {
                string traceback = $"CallingScriptHash={newEngine.CallingScriptHash}\r\nCurrentScriptHash={newEngine.CurrentScriptHash}\r\nEntryScriptHash={newEngine.EntryScriptHash}\r\n";
                traceback += newEngine.FaultException.StackTrace;
                foreach (Neo.VM.ExecutionContext context in newEngine.InvocationStack.Reverse())
                {
                    UInt160 contextScriptHash = context.GetScriptHash();
                    //try
                    {
                        if (contractScriptHashToAllInstructionPointerToSourceLineNum.ContainsKey(contextScriptHash) && contractScriptHashToAllInstructionPointerToSourceLineNum[contextScriptHash].ContainsKey((uint)context.InstructionPointer))
                        {
                            string sourceCodeTraceback = "";
                            SourceFilenameAndLineNum sourceCode = contractScriptHashToAllInstructionPointerToSourceLineNum[contextScriptHash][(uint)context.InstructionPointer];
                            sourceCodeTraceback += $"\r\nFile {sourceCode.sourceFilename}, line {sourceCode.lineNum}: {sourceCode.sourceContent}";
                            traceback += sourceCodeTraceback;
                        }
                    }
                    //catch (Exception _) {; }
                    traceback += $"\r\n\tScriptHash={contextScriptHash}, InstructionPointer={context.InstructionPointer}, OpCode {context.CurrentInstruction?.OpCode}, Script Length={context.Script.Length}";
                }
                traceback += $"\r\n{json["exception"]!.GetString()}";

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
            FairyEngine engine = FairyEngine.Create(TriggerType.Application, container, snapshot, this, persistingBlock, settings, gas, diagnostic, oldEngine: oldEngine);
            engine.LoadScript(script, initialPosition: offset);
            return Execute(engine, out breakReason);
        }

        private SourceFilenameAndLineNum GetCurrentSourceCode(FairyEngine engine)
        {
            UInt160 currentScriptHash = engine.CurrentScriptHash;
            uint instructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            SourceFilenameAndLineNum currentSource = defaultSource;
            if (contractScriptHashToAllSourceLineNums.ContainsKey(currentScriptHash)
                 && contractScriptHashToAllInstructionPointerToSourceLineNum[currentScriptHash].ContainsKey(instructionPointer)
                 && contractScriptHashToAllSourceLineNums[currentScriptHash]
                    .Contains(contractScriptHashToAllInstructionPointerToSourceLineNum[currentScriptHash][instructionPointer]))
                currentSource = contractScriptHashToAllInstructionPointerToSourceLineNum[currentScriptHash][instructionPointer];
            return currentSource;
        }

        private bool HitSourceCodeBreakpoint(FairyEngine engine)
        {
            UInt160 currentScriptHash = engine.CurrentScriptHash;
            uint currentInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            return contractScriptHashToSourceCodeBreakpoints.ContainsKey(currentScriptHash)
                && contractScriptHashToAllInstructionPointerToSourceLineNum[currentScriptHash].ContainsKey(currentInstructionPointer)
                && contractScriptHashToSourceCodeBreakpoints[currentScriptHash]
                   .Contains(contractScriptHashToAllInstructionPointerToSourceLineNum[currentScriptHash][currentInstructionPointer]);
        }

        /// <summary>
        /// Do not break for <see cref="BreakReason.SourceCodeBreakpoint"/> or <see cref="BreakReason.SourceCode"/>
        /// if we are at the same source code line as the starting position, or
        /// if invocation stack count is same as prev,
        /// and current source filename is different from prev,
        /// and instruction pointer is greater than prev
        /// </summary>
        /// <param name="engine"></param>
        /// <param name="breakReason"></param>
        /// <param name="startInstructionPointer"></param>
        /// <param name="startSourceCode"></param>
        /// <param name="startInvocationStackCount"></param>
        /// <returns></returns>
        private bool ShouldBreakAtDifferentSourceCode(FairyEngine engine, BreakReason breakReason,
            uint startInstructionPointer, SourceFilenameAndLineNum startSourceCode, int startInvocationStackCount)
        {
            if ((breakReason & BreakReason.SourceCodeBreakpoint) > 0
                || (breakReason & BreakReason.SourceCode) > 0)
            {
                uint currentInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
                if (currentInstructionPointer <= startInstructionPointer)
                    return true;
                if (engine.InvocationStack.Count != startInvocationStackCount)
                    return true;
                SourceFilenameAndLineNum currentSourceCode = GetCurrentSourceCode(engine);
                if (startSourceCode == defaultSource && currentSourceCode != defaultSource)
                    return true;
                // TODO: startSourceCode is code from framework, currentSourceCode is not, return true
                if (currentSourceCode.sourceFilename == startSourceCode.sourceFilename
                    && currentSourceCode.lineNum != startSourceCode.lineNum)
                    return true;
                // Do not break when invocation stack count is same as prev,
                // and current source filename is different from prev,
                // and instruction pointer is greater than prev
            }
            return false;
        }

        private void SetAssemblyCoverage(UInt160 scriptHash, uint instructionPointer)
        {
            if (contractScriptHashToInstructionPointerToCoverage.ContainsKey(scriptHash)
                && contractScriptHashToInstructionPointerToCoverage[scriptHash].ContainsKey(instructionPointer))
                contractScriptHashToInstructionPointerToCoverage[scriptHash][instructionPointer] = true;
        }

        private FairyEngine ExecuteAndCheck(FairyEngine engine, out BreakReason actualBreakReason,
            BreakReason requiredBreakReason = BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint)
        {
            actualBreakReason = BreakReason.None;
            if (engine.State == VMState.HALT || engine.State == VMState.FAULT)
                return engine;
            Instruction prevInstruction = engine.CurrentContext!.CurrentInstruction ?? Instruction.RET;
            OpCode prevOpCode = prevInstruction.OpCode;
            uint prevInstructionPointer = (uint)engine.CurrentContext.InstructionPointer;
            UInt160 prevScriptHash = engine.CurrentScriptHash;
            if ((requiredBreakReason & BreakReason.Call) > 0 &&
               (prevOpCode == OpCode.CALL || prevOpCode == OpCode.CALLA || prevOpCode == OpCode.CALLT || prevOpCode == OpCode.CALL_L)
               || (prevOpCode == OpCode.SYSCALL && prevInstruction.TokenU32 == ApplicationEngine.System_Contract_Call.Hash))
            {
                engine.ExecuteNext();
                if (engine.State != VMState.NONE)
                    return engine;
                SetAssemblyCoverage(prevScriptHash, prevInstructionPointer);
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.Call;
                return engine;
            }
            if ((requiredBreakReason & BreakReason.Return) > 0 && prevOpCode == OpCode.RET)
            {
                engine.ExecuteNext();
                if (engine.State != VMState.NONE)
                    return engine;
                SetAssemblyCoverage(prevScriptHash, prevInstructionPointer);
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.Return;
                return engine;
            }
            SourceFilenameAndLineNum prevSource = GetCurrentSourceCode(engine);

            engine.ExecuteNext();
            if (engine.State != VMState.NONE)
                return engine;

            // Set coverage for the previous instruction
            SetAssemblyCoverage(prevScriptHash, prevInstructionPointer);
            // Handle the current instruction
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
            if ((requiredBreakReason & BreakReason.SourceCodeBreakpoint) > 0
                && HitSourceCodeBreakpoint(engine))
            {
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.SourceCodeBreakpoint;
                return engine;
            }
            if ((requiredBreakReason & BreakReason.SourceCode) > 0
                && prevSource != GetCurrentSourceCode(engine))
            {
                engine.State = VMState.BREAK;
                actualBreakReason |= BreakReason.SourceCode;
                return engine;
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
            uint startInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            SourceFilenameAndLineNum startSourceCode = GetCurrentSourceCode(engine);
            int startInvocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason);
                if (engine.State == VMState.BREAK)
                {
                    if ((breakReason & BreakReason.AssemblyBreakpoint) > 0)
                        break;
                    if (ShouldBreakAtDifferentSourceCode(engine, breakReason, startInstructionPointer, startSourceCode, startInvocationStackCount))
                        break;
                    engine.State = VMState.NONE;
                }
            }
            return engine;
        }

        private FairyEngine StepInto(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            uint startInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            SourceFilenameAndLineNum startSourceCode = GetCurrentSourceCode(engine);
            int startInvocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.SourceCode | BreakReason.Call);
                if (engine.State == VMState.BREAK)
                {
                    if ((breakReason & BreakReason.AssemblyBreakpoint) > 0)
                        return engine;
                    if (ShouldBreakAtDifferentSourceCode(engine, breakReason, startInstructionPointer, startSourceCode, startInvocationStackCount))
                        return engine;
                    if ((breakReason & BreakReason.Call) > 0)
                        break;
                    engine.State = VMState.NONE;
                }
            }
            if (engine.State == VMState.FAULT || engine.State == VMState.HALT)
                return engine;
            // breaked by BreakReason.Call.
            engine.State = VMState.NONE;
            while ((engine.CurrentContext!.CurrentInstruction ?? Instruction.RET).OpCode != OpCode.INITSLOT
                && engine.State == VMState.NONE)
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.Call);
            // execute INITSLOT
            if ((engine.CurrentContext!.CurrentInstruction ?? Instruction.RET).OpCode == OpCode.INITSLOT
                && engine.State != VMState.FAULT)
                engine.ExecuteNext();
            if (engine.State != VMState.FAULT)
            {
                engine.State = VMState.BREAK;
                breakReason |= BreakReason.Call;
            }
            return engine;
        }

        [RpcMethod]
        protected virtual JToken DebugStepInto(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            StepInto(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog!;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private FairyEngine StepOut(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            uint startInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            SourceFilenameAndLineNum startSourceCode = GetCurrentSourceCode(engine);
            int startInvocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.Return);
                if (engine.State == VMState.BREAK)
                {
                    if ((breakReason & BreakReason.AssemblyBreakpoint) > 0)
                        break;
                    if ((breakReason & BreakReason.Return) > 0 && engine.InvocationStack.Count < startInvocationStackCount)
                        break;
                    if (ShouldBreakAtDifferentSourceCode(engine, breakReason, startInstructionPointer, startSourceCode, startInvocationStackCount))
                        break;
                    engine.State = VMState.NONE;
                }
            }
            return engine;
        }

        [RpcMethod]
        protected virtual JToken DebugStepOut(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            StepOut(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog!;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        private FairyEngine StepOverSourceCode(FairyEngine engine, out BreakReason breakReason)
        {
            breakReason = BreakReason.None;
            if (engine.State == VMState.BREAK)
                engine.State = VMState.NONE;
            uint startInstructionPointer = (uint)engine.CurrentContext!.InstructionPointer;
            SourceFilenameAndLineNum startSourceCode = GetCurrentSourceCode(engine);
            int startInvocationStackCount = engine.InvocationStack.Count;
            while (engine.State == VMState.NONE)
            {
                engine = ExecuteAndCheck(engine, out breakReason, requiredBreakReason: BreakReason.AssemblyBreakpoint | BreakReason.SourceCodeBreakpoint | BreakReason.SourceCode);
                if (engine.State == VMState.BREAK)
                {
                    if ((breakReason & BreakReason.AssemblyBreakpoint) > 0)
                        break;
                    if ((breakReason & BreakReason.SourceCodeBreakpoint) > 0
                        && ShouldBreakAtDifferentSourceCode(engine, breakReason, startInstructionPointer, startSourceCode, startInvocationStackCount))
                        break;
                    if ((breakReason & BreakReason.SourceCode) > 0 && engine.InvocationStack.Count <= startInvocationStackCount
                        && ShouldBreakAtDifferentSourceCode(engine, breakReason, startInstructionPointer, startSourceCode, startInvocationStackCount))
                        break;
                    engine.State = VMState.NONE;
                }
            }
            return engine;
        }

        [RpcMethod]
        protected virtual JToken DebugStepOverSourceCode(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            StepOverSourceCode(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog!;
            return DumpDebugResultJson(newEngine, breakReason);
        }

        [RpcMethod]
        protected virtual JToken DebugStepOverAssembly(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            BreakReason breakReason = BreakReason.None;
            logs.Clear();
            FairyEngine.Log += CacheLog!;
            ExecuteAndCheck(newEngine, out breakReason);
            FairyEngine.Log -= CacheLog!;
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
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).LocalVariables!.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetArguments(JArray _params)
        {
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).Arguments!.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetStaticFields(JArray _params)
        {
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).StaticFields!.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetEvaluationStack(JArray _params)
        {
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).EvaluationStack.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
        }

        [RpcMethod]
        protected virtual JToken GetInvocationStack(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.Select(p =>
            {
                JObject json = new();
                json["scripthash"] = p.GetScriptHash().ToString();
                json["instructionpointer"] = p.InstructionPointer;
                GetSourceCode(json, p.GetScriptHash(), (uint)p.InstructionPointer);
                return json;
            }));
        }

        [RpcMethod]
        protected virtual JToken GetInstructionPointer(JArray _params)
        {
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            return new JArray(newEngine.InvocationStack.ElementAt(invocationStackIndex).InstructionPointer);
        }

        [RpcMethod]
        protected virtual JToken GetVariableValueByName(JArray _params)
        {
            string session = _params[0]!.AsString();
            string variableName = _params[1]!.AsString();
            int invocationStackIndex = _params.Count > 2 ? int.Parse(_params[2]!.AsString()) : 0;
            return GetVariableNamesAndValues(new JArray(session, invocationStackIndex))[variableName] ?? throw new ArgumentException($"Variable `{variableName}` not found.");
        }

        [RpcMethod]
        protected virtual JToken GetVariableNamesAndValues(JArray _params)
        {
            string session = _params[0]!.AsString();
            int invocationStackIndex = _params.Count > 1 ? int.Parse(_params[1]!.AsString()) : 0;
            FairyEngine newEngine = sessionStringToFairySession[session].debugEngine!;
            Neo.VM.ExecutionContext invocationStackItem = newEngine.InvocationStack.ElementAt(invocationStackIndex);
            UInt160 invocationStackScriptHash = invocationStackItem.GetScriptHash();
            int instructionPointer = invocationStackItem.InstructionPointer;
            JToken? method = GetMethodByInstructionPointer(new JArray(invocationStackScriptHash.ToString(), instructionPointer));
            JObject returnedJson = new();
            JArray staticVariables = (JArray)contractScriptHashToNefDbgNfo[invocationStackScriptHash]["static-variables"]!;
            foreach (JString? param in staticVariables)
            {
                string[] nameTypeAndIndex = param!.AsString().Split(',');
                int index = int.Parse(nameTypeAndIndex[2]);
                returnedJson[nameTypeAndIndex[0]] = invocationStackItem.StaticFields![index].ToJson();
            }
            if (method != JObject.Null)
            {
                foreach (JToken? param in (JArray)method["params"]!)
                {
                    string[] nameTypeAndIndex = param!.AsString().Split(',');
                    int index = int.Parse(nameTypeAndIndex[2]);
                    returnedJson[nameTypeAndIndex[0]] = invocationStackItem.Arguments![index].ToJson();
                }
                foreach (JToken? param in (JArray)method["variables"]!)
                {
                    string[] nameTypeAndIndex = param!.AsString().Split(',');
                    int index = int.Parse(nameTypeAndIndex[2]);
                    returnedJson[nameTypeAndIndex[0]] = invocationStackItem.LocalVariables![index].ToJson();
                }
            }
            return returnedJson;
        }
    }
}
