using Neo.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System.Numerics;
using System.Collections.Concurrent;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        readonly ConcurrentQueue<LogEventArgs> logs = new();

        public UInt160 neoScriptHash = UInt160.Parse("0xef4073a0f2b305a38ec4050e4d3d28bc40ea63f5");
        public UInt160 gasScriptHash = UInt160.Parse("0xd2a4cff31913016155e38e474a2c06d08be276cf");
        const byte Native_Prefix_Account = 20;
        const byte Native_Prefix_TotalSupply = 11;

        [RpcMethod]
        protected virtual JObject InvokeFunctionWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            UInt160 script_hash = UInt160.Parse(_params[2].AsString());
            string operation = _params[3].AsString();
            ContractParameter[] args = _params.Count >= 5 ? ((JArray)_params[4]).Select(p => ContractParameter.FromJson((JObject)p)).ToArray() : System.Array.Empty<ContractParameter>();
            Signer[]? signers = _params.Count >= 6 ? SignersFromJson((JArray)_params[5], system.Settings) : null;
            Witness[]? witnesses = _params.Count >= 6 ? WitnessesFromJson((JArray)_params[5]) : null;

            byte[] script;
            using (ScriptBuilder sb = new())
            {
                script = sb.EmitDynamicCall(script_hash, operation, args).ToArray();
            }
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers, witnesses);
        }

        [RpcMethod]
        protected virtual JObject InvokeManyWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            Signer[]? signers = _params.Count >= 4 ? SignersFromJson((JArray)_params[3], system.Settings) : null;
            Witness[]? witnesses = _params.Count >= 5 ? WitnessesFromJson((JArray)_params[4]) : null;
            byte[] script = Array.Empty<byte>();
            using (ScriptBuilder sb = new())
            {
                foreach (JArray invokeParams in (JArray)_params[2])
                {
                    UInt160 script_hash = UInt160.Parse(invokeParams[0].AsString());
                    string operation = invokeParams[1].AsString();
                    ContractParameter[] args = invokeParams.Count >= 3 ? ((JArray)invokeParams[2]).Select(p => ContractParameter.FromJson((JObject)p)).ToArray() : System.Array.Empty<ContractParameter>();
                    sb.EmitDynamicCall(script_hash, operation, args);
                }
                script = sb.ToArray();
            }
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers, witnesses);
        }

        [RpcMethod]
        protected virtual JObject InvokeScriptWithSession(JArray _params)
        {
            string session = _params[0].AsString();
            bool writeSnapshot = _params[1].AsBoolean();
            byte[] script = Convert.FromBase64String(_params[2].AsString());
            Signer[]? signers = _params.Count >= 4 ? SignersFromJson((JArray)_params[3], system.Settings) : null;
            Witness[]? witnesses = _params.Count >= 4 ? WitnessesFromJson((JArray)_params[3]) : null;
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers, witnesses);
        }

        private void CacheLog(object sender, LogEventArgs logEventArgs)
        {
            logs.Enqueue(logEventArgs);
        }

        private JObject GetInvokeResultWithSession(string session, bool writeSnapshot, byte[] script, Signer[]? signers = null, Witness[]? witnesses = null)
        {
            FairySession testSession;
            if (!sessionStringToFairySession.TryGetValue(session, out testSession))
            {  // we allow initializing a new session when executing
                testSession = NewTestSession();
                sessionStringToFairySession[session] = testSession;
            }
            FairyEngine oldEngine = testSession.engine, newEngine;
            logs.Clear();
            Random random = new();
            Transaction? tx = signers == null ? null : new Transaction
            {
                Nonce = (uint)random.Next(),
                ValidUntilBlock = NativeContract.Ledger.CurrentIndex(testSession.engine.Snapshot) + system.Settings.MaxValidUntilBlockIncrement,
                Signers = signers,
                Attributes = Array.Empty<TransactionAttribute>(),
                Script = script,
                Witnesses = witnesses
            };
            FairyEngine.Log += CacheLog;
            newEngine = FairyEngine.Run(script, testSession.engine.Snapshot.CreateSnapshot(), container: tx, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: oldEngine, fairy: this);
            FairyEngine.Log -= CacheLog;
            if (writeSnapshot && newEngine.State == VMState.HALT)
                sessionStringToFairySession[session].engine = newEngine;

            JObject json = new();

            for (int i = newEngine.Notifications.Count - 1; i >= 0; i--)
            {
                if(newEngine.Notifications[i].EventName == "OracleRequest")
                {
                    int oracleContractId = NativeContract.Oracle.Id;
                    ulong requestId = (ulong)(new BigInteger(newEngine.Snapshot.TryGet(new StorageKey { Id=oracleContractId, Key=new byte[] { 9 } }).Value.ToArray()) - 1);
                    OracleRequest oracleRequest = newEngine.Snapshot.TryGet(new KeyBuilder(oracleContractId, 7).AddBigEndian(requestId)).GetInteroperable<OracleRequest>();
                    //if (!Uri.TryCreate(oracleRequest.Url, UriKind.Absolute, out var uri))
                    //    break;
                    //if (uri.Scheme != "https")
                    //{
                    //    ConsoleHelper.Info($"WARNING: uri scheme {uri.Scheme} not supported by fairy.");
                    //    break;
                    //}
                    JArray oracleRequests;
                    if (!json.ContainsProperty("oraclerequests"))
                    {
                        oracleRequests = new JArray();
                        json["oraclerequests"] = oracleRequests;
                    }
                    else
                    {
                        oracleRequests = (JArray)json["oraclerequests"];
                    }
                    oracleRequests.Add(oracleRequest.ToStackItem(new ReferenceCounter()).ToJson());
                }
            }

            json["script"] = Convert.ToBase64String(script);
            json["state"] = newEngine.State;
            json["gasconsumed"] = newEngine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(newEngine.FaultException);
            if(json["exception"] != null)
            {
                string traceback = $"{json["exception"].GetString()}\r\nCallingScriptHash={newEngine.CallingScriptHash}[{NativeContract.ContractManagement.GetContract(newEngine.Snapshot, newEngine.CallingScriptHash)?.Manifest.Name}]\r\nCurrentScriptHash={newEngine.CurrentScriptHash}[{NativeContract.ContractManagement.GetContract(newEngine.Snapshot, newEngine.CurrentScriptHash)?.Manifest.Name}]\r\nEntryScriptHash={newEngine.EntryScriptHash}\r\n";
                traceback += newEngine.FaultException.StackTrace;
                foreach (Neo.VM.ExecutionContext context in newEngine.InvocationStack)
                {
                    traceback += $"\r\nInstructionPointer={context.InstructionPointer}, OpCode {context.CurrentInstruction?.OpCode}, Script Length={context.Script.Length}";
                }
                if(!logs.IsEmpty)
                {
                    traceback += $"\r\n-------Logs-------({logs.Count})";
                }
                foreach (LogEventArgs log in logs)
                {
                    string contractName = NativeContract.ContractManagement.GetContract(newEngine.Snapshot, log.ScriptHash)?.Manifest.Name;
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
            if (newEngine.State != VMState.FAULT)
            {
                ProcessInvokeWithWalletAndSnapshot(oldEngine.Snapshot, json, signers, block: CreateDummyBlockWithTimestamp(testSession.engine.Snapshot, system.Settings, timestamp: testSession.timestamp));
            }
            return json;
        }

        private void ProcessInvokeWithWalletAndSnapshot(DataCache snapshot, JObject result, Signer[]? signers = null, Block block = null)
        {
            if (fairyWallet == null || signers == null) return;

            Signer[] witnessSigners = signers;
            if (witnessSigners.Length <= 0) return;
            UInt160? sender = signers.Length > 0 ? signers[0].Account : null;

            Transaction tx;
            try
            {
                tx = fairyWallet.MakeTransaction(snapshot.CreateSnapshot(), Convert.FromBase64String(result["script"].AsString()), sender, witnessSigners, maxGas: settings.MaxGasInvoke, persistingBlock: block);
            }
            catch //(Exception e)
            {
                // result["exception"] = GetExceptionMessage(e);
                return;
            }
            ContractParametersContext context = new(snapshot.CreateSnapshot(), tx, system.Settings.Network);
            fairyWallet.Sign(context);
            if (context.Completed)
            {
                tx.Witnesses = context.GetWitnesses();
                byte[] txBytes = tx.ToArray();
                result["tx"] = Convert.ToBase64String(txBytes);
                long networkfee = (fairyWallet ?? new DummyWallet(system.Settings)).CalculateNetworkFee(system.StoreView, txBytes.AsSerializable<Transaction>());
                result["networkfee"] = networkfee.ToString();
            }
            else
            {
                result["pendingsignature"] = context.ToJson();
            }
        }
    }
}
