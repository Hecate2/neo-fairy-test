// include this file in neo-modules/src/RpcServer/RpcServer.csproj
// and build your own RpcServer

using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets.NEP6;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.Plugins
{
    public partial class RpcServer
    {
        Dictionary<string, ApplicationEngine> sessionToEngine = new();
        Dictionary<string, ulong> sessionToTimestamp = new();

        [RpcMethod]
        protected virtual JObject InvokeFunctionWithSession(JArray _params)
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
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers);
        }

        [RpcMethod]
        protected virtual JObject InvokeScriptWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            byte[] script = Convert.FromBase64String(_params[2].AsString());
            Signers signers = _params.Count >= 4 ? SignersFromJson((JArray)_params[3], system.Settings) : null;
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers);
        }

        [RpcMethod]
        protected virtual JObject NewSnapshotsFromCurrentSystem(JArray _params)
        {
            JObject json = new();
            foreach(var param in _params)
            {
                string session = param.AsString();
                if (sessionToEngine.TryGetValue(session, out _))
                    json[session] = true;
                else
                    json[session] = false;
                sessionToEngine[session] = ApplicationEngine.Run(new byte[] {0x40}, system.StoreView, settings: system.Settings, gas: settings.MaxGasInvoke);
                sessionToTimestamp[session] = 0;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JObject DeleteSnapshots(JArray _params)
        {
            int count = _params.Count;
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s.AsString();
                json[str] = sessionToEngine.Remove(str) ? sessionToTimestamp.Remove(str) : false;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JObject ListSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionToEngine.Keys)
            {
                session.Add(s);
            }
            return session;
        }

        [RpcMethod]
        protected virtual JObject RenameSnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionToEngine[to] = sessionToEngine[from];
            sessionToEngine.Remove(from);
            sessionToTimestamp[to] = sessionToTimestamp[from];
            sessionToTimestamp.Remove(from);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JObject CopySnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionToEngine[to] = BuildSnapshotWithDummyScript(sessionToEngine[from]);
            sessionToTimestamp[to] = sessionToTimestamp[from];
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JObject SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0].AsString();
            ulong timestamp = ulong.Parse(_params[1].AsString());
            sessionToTimestamp[session] = timestamp;
            JObject json = new();
            json[session] = timestamp;
            return json;
        }

        [RpcMethod]
        protected virtual JObject GetSnapshotTimeStamp(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                json[session] = sessionToTimestamp.GetValueOrDefault(session, (ulong)0);
            }
            return json;
        }

        private static Block CreateDummyBlockWithTimestamp(DataCache snapshot, ProtocolSettings settings, ulong timestamp=0)
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
                        InvocationScript = Array.Empty<byte>(),
                        VerificationScript = Array.Empty<byte>()
                    },
                },
                Transactions = Array.Empty<Transaction>()
            };
        }


        private ApplicationEngine BuildSnapshotWithDummyScript(ApplicationEngine engine)
        {
            return ApplicationEngine.Run(new byte[] { 0x40 }, engine.Snapshot.CreateSnapshot(), settings: system.Settings, gas: settings.MaxGasInvoke);
        }

        private JObject GetInvokeResultWithSession(string session, bool writeSnapshot, byte[] script, Signers signers = null)
        {
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
            DataCache validSnapshotBase;
            if (timestamp == 0)
            {
                if (sessionToEngine.TryGetValue(session, out oldEngine))
                {
                    newEngine = ApplicationEngine.Run(script, oldEngine.Snapshot.CreateSnapshot(), container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
                    validSnapshotBase = oldEngine.Snapshot;
                }
                else
                {
                    newEngine = ApplicationEngine.Run(script, system.StoreView, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
                    validSnapshotBase = system.StoreView;
                }
            }
            else
            {
                oldEngine = sessionToEngine[session];
                validSnapshotBase = oldEngine.Snapshot;
                newEngine = ApplicationEngine.Create(TriggerType.Application, container: tx, oldEngine.Snapshot.CreateSnapshot(), CreateDummyBlockWithTimestamp(oldEngine.Snapshot, system.Settings, timestamp: timestamp), system.Settings, settings.MaxGasInvoke);
                newEngine.LoadScript(script);
                newEngine.Execute();
            }
            if (writeSnapshot && newEngine.State == VMState.HALT)
                sessionToEngine[session] = newEngine;
            JObject json = new();
            json["script"] = Convert.ToBase64String(script);
            json["state"] = newEngine.State;
            json["gasconsumed"] = newEngine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(newEngine.FaultException);
            try
            {
                json["stack"] = new JArray(newEngine.ResultStack.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
            }
            catch (InvalidOperationException)
            {
                json["stack"] = "error: invalid operation";
            }
            if (newEngine.State != VMState.FAULT)
            {
                ProcessInvokeWithWalletAndSnapshot(validSnapshotBase, json, signers);
            }
            return json;
        }

        private void ProcessInvokeWithWalletAndSnapshot(DataCache snapshot, JObject result, Signers signers = null)
        {
            if (wallet == null || signers == null) return;

            Signer[] witnessSigners = signers.GetSigners().ToArray();
            UInt160 sender = signers.Size > 0 ? signers.GetSigners()[0].Account : null;
            if (witnessSigners.Length <= 0) return;

            Transaction tx;
            try
            {
                tx = wallet.MakeTransaction(snapshot.CreateSnapshot(), Convert.FromBase64String(result["script"].AsString()), sender, witnessSigners, maxGas: settings.MaxGasInvoke);
            }
            catch (Exception e)
            {
                // result["exception"] = GetExceptionMessage(e);
                return;
            }
            ContractParametersContext context = new(snapshot.CreateSnapshot(), tx, settings.Network);
            wallet.Sign(context);
            if (context.Completed)
            {
                tx.Witnesses = context.GetWitnesses();
                byte[] txBytes = tx.ToArray();
                result["tx"] = Convert.ToBase64String(txBytes);
                long networkfee = (wallet ?? new DummyWallet(system.Settings)).CalculateNetworkFee(system.StoreView, txBytes.AsSerializable<Transaction>());
                result["networkfee"] = networkfee.ToString();
            }
            else
            {
                result["pendingsignature"] = context.ToJson();
            }
        }

    }
}
