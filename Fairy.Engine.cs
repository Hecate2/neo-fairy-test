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
        public class FairyEngine : ApplicationEngine
        {
            protected FairyEngine(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock, ProtocolSettings settings, long gas, IDiagnostic diagnostic) : base(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic){}

            public static new FairyEngine Create(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock = null, ProtocolSettings settings = null, long gas = TestModeGas, IDiagnostic diagnostic = null)
            {
                return new FairyEngine(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic);
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

            public static new FairyEngine Run(ReadOnlyMemory<byte> script, DataCache snapshot, IVerifiable container = null, Block persistingBlock = null, ProtocolSettings settings = null, int offset = 0, long gas = TestModeGas, IDiagnostic diagnostic = null)
            {
                persistingBlock ??= CreateDummyBlockWithTimestamp(snapshot, settings ?? ProtocolSettings.Default, timestamp: 0);
                FairyEngine engine = Create(TriggerType.Application, container, snapshot, persistingBlock, settings, gas, diagnostic);
                engine.LoadScript(script, initialPosition: offset);
                engine.Execute();
                return engine;
            }
        }

        private static Block CreateDummyBlockWithTimestamp(DataCache snapshot, ProtocolSettings settings, ulong timestamp = 0)
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
                    Timestamp = timestamp == 0 ? currentBlock.Timestamp + settings.MillisecondsPerBlock : timestamp,
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
            public FairyEngine _engine;
            public FairyEngine? debugEngine { get { ResetExpiration(); return _debugEngine; } set { _debugEngine = value; ResetExpiration(); } }
            public FairyEngine? _debugEngine = null;
            public ulong timestamp { get { ResetExpiration(); return _timestamp; } set { _timestamp = value; ResetExpiration(); } }
            public ulong _timestamp = 0;
            // public BigInteger? designatedRandom = null;

            public FairySession(Fairy fairy)
            {
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

        internal Timer timer;

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

