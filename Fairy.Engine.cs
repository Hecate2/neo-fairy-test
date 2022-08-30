using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System.Numerics;
using System.Reflection;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public class FairyEngine : ApplicationEngine
        {
            protected FairyEngine(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock, ProtocolSettings settings, long gas, IDiagnostic diagnostic, FairyEngine? oldEngine = null) : base(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic)
            {
                if (oldEngine != null)
                {
                    this.services = oldEngine.services;
                    this.serviceArgs = oldEngine.serviceArgs;
                }
                else
                {
                    this.services = ApplicationEngine.Services.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    this.serviceArgs = new ServiceArgs();
                }
            }

            public static FairyEngine Create(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock = null, ProtocolSettings settings = null, long gas = TestModeGas, IDiagnostic diagnostic = null, FairyEngine? oldEngine = null)
            {
                return new FairyEngine(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic, oldEngine: oldEngine);
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

            public static FairyEngine Run(ReadOnlyMemory<byte> script, DataCache snapshot, IVerifiable container = null, Block persistingBlock = null, ProtocolSettings settings = null, int offset = 0, long gas = TestModeGas, IDiagnostic diagnostic = null, FairyEngine? oldEngine = null)
            {
                persistingBlock ??= CreateDummyBlockWithTimestamp(snapshot, settings ?? ProtocolSettings.Default, timestamp: null);
                FairyEngine engine = Create(TriggerType.Application, container, snapshot, persistingBlock, settings, gas, diagnostic, oldEngine: oldEngine);
                engine.LoadScript(script, initialPosition: offset);
                engine.Execute();
                return engine;
            }

            public Dictionary<uint, InteropDescriptor> services;

            public class ServiceArgs
            {
                public ulong? timestamp = null;
                public BigInteger? designatedRandom = null;
            }
            public ServiceArgs serviceArgs;

            protected override void OnSysCall(uint method)
            {
                OnSysCall(this.services[method]);
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
                this.services ??= new Dictionary<uint, InteropDescriptor>();
                this.services[hash] = descriptor;
                return descriptor;
            }

            public new BigInteger GetRandom() => base.GetRandom();
            public BigInteger GetFairyRandom() => serviceArgs.designatedRandom != null ? (BigInteger)serviceArgs.designatedRandom : base.GetRandom();
            public new ulong GetTime() => base.GetTime();
            public ulong GetFairyTime() => serviceArgs.timestamp != null ? (ulong)serviceArgs.timestamp : GetTime();
        }

        [RpcMethod]
        protected virtual JToken SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0].AsString();
            ulong? timestamp;
            if (_params[1] == null)
            {
                timestamp = null;
                sessionStringToFairySession[session].timestamp = null;
            }
            else
            {
                timestamp = ulong.Parse(_params[1].AsString());
                sessionStringToFairySession[session].timestamp = timestamp;
            }
            JObject json = new();
            json[session] = timestamp;
            return json;
        }

        [RpcMethod]
        protected virtual JToken GetSnapshotTimeStamp(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                json[session] = sessionStringToFairySession[session].timestamp;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JToken SetSnapshotRandom(JArray _params)
        {
            string session = _params[0].AsString();
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

        [RpcMethod]
        protected virtual JToken GetSnapshotRandom(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                if (sessionStringToFairySession.ContainsKey(session))
                    json[session] = sessionStringToFairySession[session].designatedRandom.ToString();
                else
                    json[session] = null;
            }
            return json;
        }

        private static Block CreateDummyBlockWithTimestamp(DataCache snapshot, ProtocolSettings settings, ulong? timestamp = null)
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
                    Index = currentBlock.Index + 1,
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
            public DateTime StartTime;
            public FairyEngine engine { get { ResetExpiration(); return _engine; } set { _engine = value; ResetExpiration(); } }
            private FairyEngine _engine;
            public FairyEngine? debugEngine { get { ResetExpiration(); return _debugEngine; } set { _debugEngine = value; ResetExpiration(); } }
            private FairyEngine? _debugEngine = null;

            public NeoSystem system;
            public ProtocolSettings settings;

            public ulong? timestamp
            {
                get => engine.serviceArgs.timestamp;
                set
                {
                    if (value == null)
                    {
                        engine.Register("System.Runtime.GetTime", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetTime)), ApplicationEngine.System_Runtime_GetTime.Hash, 1 << 3, CallFlags.None);
                        engine.serviceArgs.timestamp = null;
                    }
                    else
                    {
                        engine.serviceArgs.timestamp = value;
                        engine.Register("System.Runtime.GetTime", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetFairyTime)), ApplicationEngine.System_Runtime_GetTime.Hash, 1 << 3, CallFlags.None);
                    }
                }
            }

            public BigInteger? designatedRandom
            {
                get => engine.serviceArgs.designatedRandom;
                set
                {
                    if (value == null)
                    {
                        engine.Register("System.Runtime.GetRandom", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetRandom)), ApplicationEngine.System_Runtime_GetRandom.Hash, 0, CallFlags.None);
                        engine.serviceArgs.designatedRandom = null;
                    }
                    else
                    {
                        engine.serviceArgs.designatedRandom = value;
                        engine.Register("System.Runtime.GetRandom", typeof(FairyEngine).GetMethod(nameof(FairyEngine.GetFairyRandom)), ApplicationEngine.System_Runtime_GetRandom.Hash, 0, CallFlags.None);
                    }
                }
            }

            public void ResetServices()
            {
                engine.serviceArgs = new();
                engine.services = ApplicationEngine.Services.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                ResetExpiration();
            }

            public FairySession(Fairy fairy)
            {
                system = fairy.system;
                settings = fairy.system.Settings;
                _engine = Fairy.FairyEngine.Run(new byte[] { 0x40 }, fairy.system.StoreView, settings: fairy.system.Settings, gas: fairy.settings.MaxGasInvoke);
            }

            public void ResetExpiration()
            {
                StartTime = DateTime.UtcNow;
            }

            public new string ToString()
            {
                return $"hasDebugEngine: {debugEngine != null}\ttimestamp: {timestamp}";//\tdesignatedRandom: {designatedRandom}";
            }

            public void Dispose()
            {
                engine?.Dispose();
                debugEngine?.Dispose();
            }
        }

        internal Timer? timer;

        internal void InitializeTimer()
        {
            if (settings.SessionEnabled)
                timer = new(OnTimer, null, settings.SessionExpirationTime.Milliseconds, 60000);
        }

        internal void OnTimer(object state)
        {
            List<(string Id, FairySession Session)> toBeDestroyed = new();
            foreach (var (id, session) in sessionStringToFairySession)
                if (DateTime.UtcNow >= session.StartTime + settings.SessionExpirationTime)
                    toBeDestroyed.Add((id, session));
            //Console.WriteLine(toBeDestroyed.Count);
            foreach (var (id, _) in toBeDestroyed)
                sessionStringToFairySession.Remove(id, out _);
            foreach (var (_, session) in toBeDestroyed)
                session.Dispose();

            JArray debugInfoToBeDeleted = new();
            foreach (UInt160 k in this.contractScriptHashToSourceLineFilenames.Keys)
            {
                string? contractName = null;
                foreach (string s in this.sessionStringToFairySession.Keys)
                {
                    contractName = NativeContract.ContractManagement.GetContract(this.sessionStringToFairySession[s].engine.Snapshot, k)?.Manifest.Name;
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

