// Copyright (C) 2015-2025 The Neo Project.
//
// Fairy.Engine.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins.RpcServer;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using System.Numerics;
using System.Reflection;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public static FairySession NewFairySession(NeoSystem system, Fairy? fairy = null, DataCache? snapshot = null)
        {
            if (fairy == null)
                fairy = new(system, settings: RpcServerSettings.Default);
            return new FairySession(fairy, snapshot);
        }

        public static FairyEngine NewFairyEngine(NeoSystem system, Fairy? fairy = null, DataCache? snapshot = null)
        {
            if (fairy == null)
                fairy = new(system, settings: RpcServerSettings.Default);
            return Fairy.FairyEngine.Run(new byte[] { 0x40 }, snapshot == null ? fairy.system.StoreView : snapshot, fairy, settings: fairy.system.Settings, gas: fairy.settings.MaxGasInvoke);
        }

        public class FairyEngine : ApplicationEngine
        {
            readonly Fairy fairy;
            protected FairyEngine(TriggerType trigger, IVerifiable? container, DataCache snapshot, Block? persistingBlock, ProtocolSettings? settings, long gas, IDiagnostic? diagnostic, Fairy fairy, FairyEngine? oldEngine = null, bool copyRuntimeArgs = false)
                : base(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic, jumpTable: ComposeFairyJumpTable())
            {
                this.fairy = fairy;
                if (oldEngine != null)
                {
                    if (copyRuntimeArgs)
                    {
                        services = oldEngine.services.ToDictionary(kvpair => kvpair.Key, kvpair => kvpair.Value);  // clone
                        runtimeArgs = (RuntimeArgs)oldEngine.runtimeArgs.Clone();
                    }
                    else
                    {
                        services = oldEngine.services;
                        runtimeArgs = oldEngine.runtimeArgs;
                    }
                }
                else
                {
                    services = ApplicationEngine.Services.ToDictionary(kvpair => kvpair.Key, kvpair => kvpair.Value);
                    runtimeArgs = new RuntimeArgs(fairy.defaultFairyWallet);
                }
            }

            public static FairyEngine Create(TriggerType trigger, IVerifiable? container, DataCache snapshot, Fairy fairy, Block? persistingBlock = null, ProtocolSettings? settings = null, long gas = TestModeGas, IDiagnostic? diagnostic = null, FairyEngine? oldEngine = null, bool copyRuntimeArgs = false)
            {
                return new FairyEngine(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic, fairy, oldEngine: oldEngine, copyRuntimeArgs: copyRuntimeArgs);
            }

            public new VMState State
            {
                get
                {
                    return base.State;
                }
                set
                {
                    base.State = value;
                }
            }

            public new void ExecuteNext()
            {
                base.ExecuteNext();
            }

            public new VMState Execute()
            {
                if (State == VMState.BREAK)
                    State = VMState.NONE;
                while (State != VMState.HALT && State != VMState.FAULT)
                {
                    uint currentInstructionPointer = (uint)CurrentContext!.InstructionPointer;
                    UInt160 currentScripthash = CurrentScriptHash;
                    ExecuteNext();
                    if (currentScripthash != null && fairy.contractScriptHashToInstructionPointerToCoverage.ContainsKey(currentScripthash) && fairy.contractScriptHashToInstructionPointerToCoverage[currentScripthash].ContainsKey(currentInstructionPointer))
                        fairy.contractScriptHashToInstructionPointerToCoverage[currentScripthash][currentInstructionPointer] = true;
                }
                return State;
            }

            public static FairyEngine Run(ReadOnlyMemory<byte> script, DataCache snapshot, Fairy fairy, IVerifiable? container = null, Block? persistingBlock = null, ProtocolSettings? settings = null, int offset = 0, long gas = TestModeGas, IDiagnostic? diagnostic = null, FairyEngine? oldEngine = null, bool copyRuntimeArgs = false)
            {
                persistingBlock ??= CreateDummyBlockWithTimestamp(snapshot, settings ?? ProtocolSettings.Default, timestamp: null);
                FairyEngine engine = Create(TriggerType.Application, container, snapshot, fairy, persistingBlock, settings, gas, diagnostic, oldEngine: oldEngine, copyRuntimeArgs: copyRuntimeArgs);
                engine.LoadScript(script, initialPosition: offset);
                engine.Execute();
                return engine;
            }

            public Dictionary<uint, InteropDescriptor> services;

            public class RuntimeArgs : ICloneable
            {
                public RuntimeArgs(Wallet fairyWallet)
                {
                    this.fairyWallet = fairyWallet;
                }
                public Wallet fairyWallet;
                public ulong? timestamp = null;
                public BigInteger? designatedRandom = null;
                public bool checkWitnessReturnTrue = false;

                public object Clone() => MemberwiseClone();
                //public uint? blockIndex = null;
            }
            public RuntimeArgs runtimeArgs;

            protected static JumpTable ComposeFairyJumpTable()
            {
                var table = new JumpTable();

                table[OpCode.SYSCALL] = OnSysCall;
                table[OpCode.CALLT] = OnCallT;

                return table;
            }

            protected static new void OnSysCall(ExecutionEngine engine, Instruction instruction)
            {
                if (engine is FairyEngine fairyEngine)
                {
                    uint method = instruction.TokenU32;
                    fairyEngine.OnSysCall(fairyEngine.services[method]);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            public InteropDescriptor Register(string name, MethodInfo method, uint hash, long fixedPrice, CallFlags requiredCallFlags)
            {
                InteropDescriptor descriptor = new()
                {
                    Name = name,
                    Handler = method,
                    FixedPrice = fixedPrice,
                    RequiredCallFlags = requiredCallFlags
                };
                services ??= new Dictionary<uint, InteropDescriptor>();
                services[hash] = descriptor;
                return descriptor;
            }

            public new bool CheckWitness(byte[] hashOrPubkey) => base.CheckWitness(hashOrPubkey);
            public bool CheckFairyWitness(byte[] hashOrPubkey) => runtimeArgs.checkWitnessReturnTrue || CheckWitness(hashOrPubkey);
            public new BigInteger GetRandom() => base.GetRandom();
            public BigInteger GetFairyRandom() => runtimeArgs.designatedRandom != null ? (BigInteger)runtimeArgs.designatedRandom : base.GetRandom();
            public new ulong GetTime()
            {
                ulong result = base.GetTime();
                if (result != 0)
                    return result;
                uint currentIndex = NativeContract.Ledger.CurrentIndex(SnapshotCache);
                Block currentBlock = NativeContract.Ledger.GetBlock(SnapshotCache, currentIndex);
                return currentBlock.Timestamp + ProtocolSettings.MillisecondsPerBlock;
            }
            public ulong GetFairyTime() => runtimeArgs.timestamp != null ? (ulong)runtimeArgs.timestamp : GetTime();
            //public ulong GetFairyBlockIndex() => serviceArgs.blockIndex != null ? (uint)serviceArgs.blockIndex : NativeContract.Ledger.CurrentIndex(this.Snapshot);
        }

        [FairyRpcMethod]
        protected virtual JToken SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0]!.AsString();
            ulong? timestamp;
            if (_params[1] == null)
            {
                timestamp = null;
                sessionStringToFairySession[session].timestamp = null;
            }
            else
            {
                timestamp = ulong.Parse(_params[1]!.AsString());
                sessionStringToFairySession[session].timestamp = timestamp;
            }
            JObject json = new();
            json[session] = timestamp;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken GetSnapshotTimeStamp(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s!.AsString();
                json[session] = sessionStringToFairySession[session].timestamp;
            }
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken SetSnapshotRandom(JArray _params)
        {
            string session = _params[0]!.AsString();
            string? designatedRandomString = _params[1]?.AsString();
            FairySession fairySession = sessionStringToFairySession[session];
            if (designatedRandomString == null)
                fairySession.designatedRandom = null;
            else
                fairySession.designatedRandom = BigInteger.Parse(designatedRandomString);
            JObject json = new();
            json[session] = designatedRandomString;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken GetSnapshotRandom(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s!.AsString();
                if (sessionStringToFairySession.ContainsKey(session))
                    json[session] = sessionStringToFairySession[session].designatedRandom.ToString();
                else
                    json[session] = null;
            }
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken SetSnapshotCheckWitness(JArray _params)
        {
            string session = _params[0]!.AsString();
            bool setValue = _params[1]!.AsBoolean();
            FairySession fairySession = sessionStringToFairySession[session];
            fairySession.checkWitnessReturnTrue = setValue;
            JObject json = new();
            json[session] = setValue;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken GetSnapshotCheckWitness(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s!.AsString();
                if (sessionStringToFairySession.ContainsKey(session))
                    json[session] = sessionStringToFairySession[session].checkWitnessReturnTrue;
                else
                    json[session] = null;
            }
            return json;
        }

        private static Block CreateDummyBlockWithTimestamp(DataCache snapshot, ProtocolSettings settings, ulong? timestamp = null, uint? index = null)
        {
            UInt256 hash = NativeContract.Ledger.CurrentHash(snapshot);
            Block currentBlock = NativeContract.Ledger.GetBlock(snapshot, hash);
            return new Block
            {
                Header = new Header
                {
                    Version = 0,
                    PrevHash = hash,
                    MerkleRoot = new UInt256(),
                    Timestamp = timestamp == null ? currentBlock.Timestamp + settings.MillisecondsPerBlock : (ulong)timestamp,
                    Index = index == null ? currentBlock.Index + 1 : (uint)index,
                    NextConsensus = currentBlock.NextConsensus,
                    Witness = new Witness
                    {
                        InvocationScript = System.Array.Empty<byte>(),
                        VerificationScript = System.Array.Empty<byte>()
                    },
                },
                Transactions = System.Array.Empty<Transaction>()
            };
        }

        public class FairySession
        {
            readonly Fairy fairy;
            public DateTime StartTime;
            public FairyEngine engine { get { ResetExpiration(); return _engine; } set { _engine = value; ResetExpiration(); } }
            private FairyEngine _engine;
            public FairyEngine? debugEngine { get { ResetExpiration(); return _debugEngine; } set { _debugEngine = value; ResetExpiration(); } }
            private FairyEngine? _debugEngine = null;

            public NeoSystem system;
            public ProtocolSettings settings;

            public ulong? timestamp
            {
                get => engine.runtimeArgs.timestamp;
                set
                {
                    if (value == null)
                    {
                        engine.Register("System.Runtime.GetTime", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetTime))!, ApplicationEngine.System_Runtime_GetTime.Hash, 1 << 3, CallFlags.None);
                        engine.runtimeArgs.timestamp = null;
                    }
                    else
                    {
                        engine.runtimeArgs.timestamp = value;
                        engine.Register("System.Runtime.GetTime", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetFairyTime))!, ApplicationEngine.System_Runtime_GetTime.Hash, 1 << 3, CallFlags.None);
                    }
                }
            }

            public BigInteger? designatedRandom
            {
                get => engine.runtimeArgs.designatedRandom;
                set
                {
                    if (value == null)
                    {
                        engine.Register("System.Runtime.GetRandom", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetRandom))!, ApplicationEngine.System_Runtime_GetRandom.Hash, 0, CallFlags.None);
                        engine.runtimeArgs.designatedRandom = null;
                    }
                    else
                    {
                        engine.runtimeArgs.designatedRandom = value;
                        engine.Register("System.Runtime.GetRandom", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetFairyRandom))!, ApplicationEngine.System_Runtime_GetRandom.Hash, 0, CallFlags.None);
                    }
                }
            }

            public bool checkWitnessReturnTrue
            {
                get => engine.runtimeArgs.checkWitnessReturnTrue;
                set
                {
                    if (value == false)
                    {
                        engine.Register("System.Runtime.CheckWitness", typeof(FairyEngine).GetMethod(nameof(FairyEngine.CheckWitness))!, ApplicationEngine.System_Runtime_CheckWitness.Hash, 1 << 10, CallFlags.None);
                        engine.runtimeArgs.checkWitnessReturnTrue = value;
                    }
                    else
                    {
                        engine.runtimeArgs.checkWitnessReturnTrue = value;
                        engine.Register("System.Runtime.CheckWitness", typeof(FairyEngine).GetMethod(nameof(FairyEngine.CheckFairyWitness))!, ApplicationEngine.System_Runtime_CheckWitness.Hash, 1 << 10, CallFlags.None);
                    }
                }
            }

            public void ResetServices()
            {
                engine.runtimeArgs = new(fairy.defaultFairyWallet);
                engine.services = ApplicationEngine.Services.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                ResetExpiration();
            }

            public FairySession(Fairy fairy, DataCache? snapshot = null)
            {
                this.fairy = fairy;
                system = fairy.system;
                settings = fairy.system.Settings;
                _engine = Fairy.FairyEngine.Run(new byte[] { 0x40 }, snapshot is null ? fairy.system.StoreView : snapshot, fairy, settings: fairy.system.Settings, gas: fairy.settings.MaxGasInvoke);
            }

            public void ResetExpiration()
            {
                StartTime = DateTime.UtcNow;
            }

            public new string ToString()
            {
                return $"hasDebugEngine: {debugEngine != null}\ttimestamp: {timestamp}";//\tdesignatedRandom: {designatedRandom}";
            }
        }

        internal Timer? timer;

        internal void InitializeTimer()
        {
            if (settings.SessionEnabled)
                timer = new(OnTimer, null, settings.SessionExpirationTime.Milliseconds, 60000);
        }

        internal void OnTimer(object? state)
        {
            List<(string Id, FairySession Session)> toBeDestroyed = new();
            foreach (var (id, session) in sessionStringToFairySession)
                if (DateTime.UtcNow >= session.StartTime + settings.SessionExpirationTime)
                    toBeDestroyed.Add((id, session));
            //Console.WriteLine(toBeDestroyed.Count);
            foreach (var (id, _) in toBeDestroyed)
                sessionStringToFairySession.Remove(id, out _);

            JArray debugInfoToBeDeleted = new();
            foreach (UInt160 k in contractScriptHashToSourceLineFilenames.Keys)
            {
                string? contractName = null;
                foreach (string s in sessionStringToFairySession.Keys)
                {
                    contractName = NativeContract.ContractManagement.GetContract(sessionStringToFairySession[s].engine.SnapshotCache, k)?.Manifest.Name;
                    if (contractName != null)
                        break;
                }
                if (contractName == null)
                    debugInfoToBeDeleted.Add(k.ToString());
            }
            //Console.WriteLine(debugInfoToBeDeleted.Count);
            DeleteDebugInfo(debugInfoToBeDeleted);
        }

    }
}

