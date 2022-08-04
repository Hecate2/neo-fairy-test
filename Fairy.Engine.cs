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
            protected FairyEngine(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock, ProtocolSettings settings, long gas, IDiagnostic diagnostic) : base(trigger, container, snapshot, persistingBlock, settings, gas, diagnostic)
            {
            }

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
    }
}