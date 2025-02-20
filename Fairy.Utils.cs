using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Iterators;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System.Numerics;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        [FairyRpcMethod]
        protected virtual JObject VirtualDeploy(JArray _params)
        {
            if (defaultFairyWallet == null)
                throw new Exception("Please open a wallet before deploying a contract.");
            string session = _params[0]!.AsString();
            NefFile nef = Convert.FromBase64String(_params[1]!.AsString()).AsSerializable<NefFile>();
            ContractManifest manifest = ContractManifest.Parse(_params[2]!.AsString());
            ContractParameter? data = null;
            Signer[] signers;
            var param3 = _params[3]! as JObject;
            if (param3 != null)  // A contract parameter
            {
                data = ContractParameter.FromJson(param3);
                signers = SignersFromJson((JArray)_params[4]!, system.Settings);
            }
            else
                signers = SignersFromJson((JArray)_params[3]!, system.Settings);
            FairySession testSession = GetOrCreateFairySession(session);
            DataCache snapshot = testSession.engine.Snapshot;
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                if (data != null)
                    sb.EmitDynamicCall(NativeContract.ContractManagement.Hash, "deploy", nef.ToArray(), manifest.ToJson().ToString(), data);
                else
                    sb.EmitDynamicCall(NativeContract.ContractManagement.Hash, "deploy", nef.ToArray(), manifest.ToJson().ToString());
                script = sb.ToArray();
            }
            JObject json = new();
            try
            {
                Block dummyBlock = CreateDummyBlockWithTimestamp(testSession.engine.Snapshot, system.Settings, timestamp: sessionStringToFairySession[session].engine.runtimeArgs.timestamp);
                Transaction tx = defaultFairyWallet.MakeTransaction(sessionStringToFairySession[session].engine.Snapshot, script, sender: signers.Length > 0 ? signers[0].Account : defaultFairyWallet.GetAccounts().First().ScriptHash, persistingBlock: dummyBlock);
                json["networkfee"] = tx.NetworkFee.ToString();
                UInt160 hash = SmartContract.Helper.GetContractHash(tx.Sender, nef.CheckSum, manifest.Name);
                sessionStringToFairySession[session].engine = FairyEngine.Run(script, snapshot.CreateSnapshot(), this, persistingBlock: dummyBlock, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: sessionStringToFairySession[session].engine);
                json["gasconsumed"] = sessionStringToFairySession[session].engine.GasConsumed.ToString();
                json[session] = hash.ToString();
            }
            catch (InvalidOperationException ex)
            {
                if (ex.InnerException == null)
                {
                    throw;
                }
                if (ex.InnerException.Message.StartsWith("Contract Already Exists: "))
                {
                    json[session] = ex.InnerException.Message[^42..];
                }
                else
                {
                    throw ex.InnerException;
                }
            }
            return json;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="_params">
        /// multiple UInt256 or uint indexes
        /// 2 indexes, 1st index <= 2nd index: get all blocks between these indexes
        /// other cases: get all blocks defined by each param
        /// </param>
        /// <returns></returns>
        [FairyRpcMethod]
        protected virtual JToken GetManyBlocks(JArray _params)
        {
            using var snapshot = system.GetSnapshot();
            JArray result = new();
            if (_params.Count == 2 && _params[0] is JNumber && _params[1] is JNumber)
                for (uint i = uint.Parse(_params[0]!.AsString()); i <= uint.Parse(_params[1]!.AsString()); ++i)
                    result.Add(NativeContract.Ledger.GetBlock(snapshot, i).ToJson(system.Settings));
            if (result.Count == 0)
                foreach (JToken? key in _params)
                    if (key is JNumber)
                        result.Add(NativeContract.Ledger.GetBlock(snapshot, uint.Parse(key.AsString())).ToJson(system.Settings));
                    else
                        result.Add(NativeContract.Ledger.GetBlock(snapshot, UInt256.Parse(key!.AsString())).ToJson(system.Settings));
            return result;
        }

        [FairyRpcMethod]
        protected virtual JToken GetContract(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 hash = UInt160.Parse(_params[1]!.AsString());
            ContractState contractState = NativeContract.ContractManagement.GetContract(
                session == null ? system.StoreView : sessionStringToFairySession[session].engine.Snapshot,
                hash);
            JObject result = contractState.ToJson();
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                contractState.Nef.Serialize(writer);
                byte[] nef = ms.ToArray();
                // base64 encoded bytes that can be directly saved as .nef file
                result["nefFile"] = Convert.ToBase64String(nef)!;
            }
            return result;
        }

        [FairyRpcMethod]
        protected virtual JToken ListContracts(JArray _params)
        {
            string? session = _params[0]?.AsString();
            bool verbose = _params.Count >= 2 ? _params[1]!.AsBoolean() : false;
            IEnumerable<ContractState> contractStates = NativeContract.ContractManagement.ListContracts(
                session == null ? system.StoreView : sessionStringToFairySession[session].engine.Snapshot);
            JArray json = new();
            foreach (ContractState c in contractStates)
                json.Add(verbose ? c.ToJson() : new JObject { ["id"] = c.Id, ["hash"] = c.Hash.ToString() });
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject PutStorageWithSession(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] key = Convert.FromBase64String(keyBase64);
            string valueBase64 = _params[3]!.AsString();
            byte[] value = Convert.FromBase64String(valueBase64);
            bool debug = _params.Count > 4 ? _params[4]!.AsBoolean() : false;

            FairyEngine? oldEngine;
            FairySession testSession = GetOrCreateFairySession(session);
            if (debug)
            {
                oldEngine = testSession.debugEngine;
                if (oldEngine == null)
                    throw new ArgumentException($"Null debugEngine. Did you start debugging from fairy session {session}?");
            }
            else
                oldEngine = testSession.engine;
            ContractState contractState = NativeContract.ContractManagement.GetContract(oldEngine.Snapshot, contract);
            StorageKey storageKey = new StorageKey { Id = contractState.Id, Key = key };
            oldEngine.Snapshot.Delete(storageKey);
            if (value.Length > 0)
                oldEngine.Snapshot.Add(new StorageKey { Id = contractState.Id, Key = key }, new StorageItem(value));
            JObject json = new();
            json[keyBase64] = valueBase64;
            return new JObject();
        }

        [FairyRpcMethod]
        protected virtual JObject GetStorageWithSession(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] key = Convert.FromBase64String(keyBase64);
            bool debug = _params.Count > 3 ? _params[3]!.AsBoolean() : false;

            ContractState contractState;
            JObject json = new();
            StorageItem item;

            if (session == null)
            {   // use current actual blockchain state, instead of a fairy session
                DataCache storeView = system.StoreView;
                contractState = NativeContract.ContractManagement.GetContract(storeView, contract);
                item = storeView.TryGet(new StorageKey { Id = contractState.Id, Key = key });
                json[keyBase64] = item == null ? null : Convert.ToBase64String(item.Value.ToArray());
                return json;
            }

            FairyEngine? oldEngine;
            FairySession testSession = GetOrCreateFairySession(session);
            if (debug)
            {
                oldEngine = testSession.debugEngine;
                if (oldEngine == null)
                    throw new ArgumentException($"Null debugEngine. Did you start debugging from fairy session {session}?");
            }
            else
                oldEngine = testSession.engine;
            contractState = NativeContract.ContractManagement.GetContract(oldEngine.Snapshot, contract);
            item = oldEngine.Snapshot.TryGet(new StorageKey { Id = contractState.Id, Key = key });
            json[keyBase64] = item == null ? null : Convert.ToBase64String(item.Value.ToArray());
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject FindStorageWithSession(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] prefix = Convert.FromBase64String(keyBase64);
            bool debug = _params.Count > 3 ? _params[3]!.AsBoolean() : false;

            DataCache snapshot;
            if (session == null)
                // use current actual blockchain state, instead of a fairy session
                snapshot = system.StoreView;
            else
            {
                FairySession testSession = GetOrCreateFairySession(session);
                FairyEngine? oldEngine;
                if (debug)
                {
                    oldEngine = testSession.debugEngine;
                    if (oldEngine == null)
                        throw new ArgumentException($"Null debugEngine. Did you start debugging from fairy session {session}?");
                }
                else
                    oldEngine = testSession.engine;
                snapshot = oldEngine.Snapshot;
            }
            ContractState contractState = NativeContract.ContractManagement.GetContract(snapshot, contract);
            JObject json = new();
            foreach (var (key, value) in snapshot.Find(StorageKey.CreateSearchPrefix(contractState.Id, prefix)))
                json[Convert.ToBase64String(key.Key.ToArray())] = Convert.ToBase64String(value.ToArray());
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken Deserialize(JArray _params)
        {
            JArray json = new();
            foreach (JToken? param in _params)
            {
                string dataBase64 = param!.AsString();
                json.Add(BinarySerializer.Deserialize(Convert.FromBase64String(dataBase64), ExecutionEngineLimits.Default).ToJson());
            }
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject GetTime(JArray _params)
        {
            JObject json = new();
            if (_params.Count >= 1)
                json["time"] = sessionStringToFairySession[_params[0]!.AsString()].engine.GetFairyTime();  // usually you can use GetSnapshotTimeStamp instead of this method
            else
                json["time"] = FairyEngine.Run(new byte[] { 0x40 }, system.StoreView, this, settings: system.Settings, gas: settings.MaxGasInvoke).GetTime();
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject SetNeoBalance(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 account = UInt160.Parse(_params[1]!.AsString());
            ulong balance = ulong.Parse(_params[2]!.AsString());
            return SetTokenBalance(session, neoScriptHash, account, balance, Native_Prefix_Account);
        }

        [FairyRpcMethod]
        protected virtual JObject SetGasBalance(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 account = UInt160.Parse(_params[1]!.AsString());
            ulong balance = ulong.Parse(_params[2]!.AsString());
            return SetTokenBalance(session, gasScriptHash, account, balance, Native_Prefix_Account);
        }

        [FairyRpcMethod]
        protected virtual JObject SetNep17Balance(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            UInt160 account = UInt160.Parse(_params[2]!.AsString());
            ulong balance = ulong.Parse(_params[3]!.AsString());
            byte prefix = byte.Parse(_params.Count >= 5 ? _params[4]!.AsString() : "1");
            return SetTokenBalance(session, contract, account, balance, prefix);
        }

        protected JObject SetTokenBalance(string session, UInt160 contract, UInt160 account, ulong balance, byte prefixAccount)
        {
            byte[] balanceBytes = BitConverter.GetBytes(balance);
            FairyEngine oldEngine = sessionStringToFairySession[session].engine;
            ContractState contractState = NativeContract.ContractManagement.GetContract(oldEngine.Snapshot, contract);
            JObject json = new();
            if (contract == gasScriptHash)
            {
                prefixAccount = Native_Prefix_Account;
                byte[] key = new byte[] { prefixAccount }.Concat(account.ToArray()).ToArray();
                StorageItem storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id = contractState.Id, Key = key }, () => new StorageItem(new AccountState()));
                AccountState state = storage.GetInteroperable<AccountState>();
                storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id = contractState.Id, Key = new byte[] { Native_Prefix_TotalSupply } }, () => new StorageItem(BigInteger.Zero));
                storage.Add(balance - state.Balance);
                state.Balance = balance;
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
            else if (contract == neoScriptHash)
            {
                prefixAccount = Native_Prefix_Account;
                byte[] key = new byte[] { prefixAccount }.Concat(account.ToArray()).ToArray();
                StorageItem storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id = contractState.Id, Key = key }, () => new StorageItem(new NeoToken.NeoAccountState()));
                NeoToken.NeoAccountState state = storage.GetInteroperable<NeoToken.NeoAccountState>();
                storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id = contractState.Id, Key = new byte[] { Native_Prefix_TotalSupply } }, () => new StorageItem(BigInteger.Zero));
                storage.Add(balance - state.Balance);
                state.Balance = balance;
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
            else
            {
                byte[] key = new byte[] { prefixAccount }.Concat(account.ToArray()).ToArray();
                oldEngine.Snapshot.GetAndChange(new StorageKey { Id = contractState.Id, Key = key }, () => new StorageItem(balanceBytes));
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
        }

        [FairyRpcMethod]
        protected virtual JToken GetManyUnclaimedGas(JArray _params)
        {
            string? session = _params[0]?.AsString();
            DataCache snapshot = session == null ? system.StoreView : sessionStringToFairySession[session].engine.Snapshot;
            uint nextBlockIndex = NativeContract.Ledger.CurrentIndex(snapshot) + 1;
            JObject json = new JObject();
            for (int i = 1; i < _params.Count; ++i)
            {
                UInt160 account = UInt160.Parse(_params[i]!.AsString());
                json[account.ToString()] = NativeContract.NEO.UnclaimedGas(snapshot, account, nextBlockIndex).ToString();
            }
            return json;
        }

        /// <summary>
        /// Wait until the transaction is included in blocks
        /// </summary>
        /// <param name="_params">UInt256String; bool(verbose); waitBlockCount</param>
        /// <returns></returns>
        /// <exception cref="RpcException"></exception>
        [FairyRpcMethod]
        protected virtual JToken AwaitConfirmedTransaction(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0]!.AsString());
            bool verbose = _params.Count >= 2 && _params[1]!.AsBoolean();
            uint waitBlockCount = _params.Count >= 3 ? uint.Parse(_params[2]!.AsString()) : 2;
            JToken? json = GetConfirmedTransaction(hash, verbose);
            if (json != null)
                return json;
            SemaphoreSlim signal = new SemaphoreSlim(0, 1);
            uint count = 0;
            CommittedHandler getConfirmedTransactionAfterCommitted = delegate (NeoSystem @system, Block @block) { json = GetConfirmedTransaction(hash, verbose); count += 1; signal.Release(); };
            Blockchain.Committed += getConfirmedTransactionAfterCommitted;
            while (count < waitBlockCount)
            {
                signal.Wait();
                if (json != null)
                {
                    Blockchain.Committed -= getConfirmedTransactionAfterCommitted;
                    return json;
                }
            }
            Blockchain.Committed -= getConfirmedTransactionAfterCommitted;
            throw new RpcException(RpcError.UnknownTransaction.WithData($"Transaction not found in {waitBlockCount} blocks"));
        }

        protected JToken? GetConfirmedTransaction(UInt256 hash, bool verbose)
        {
            // Do not consider anything in MemPool, because they have not been confirmed
            //if (system.MemPool.TryGetValue(hash, out Transaction tx) && !verbose)
            //    return Convert.ToBase64String(tx.ToArray());
            Transaction? tx = null;
            DataCache snapshot = system.StoreView;
            TransactionState state = NativeContract.Ledger.GetTransactionState(snapshot, hash);
            tx ??= state?.Transaction;
            if (tx is null)
                return null;
            else
            {
                if (!verbose) return Convert.ToBase64String(tx.ToArray())!;
                JObject json = TransactionToJson(tx, system.Settings);
                if (state is not null)
                {
                    TrimmedBlock block = NativeContract.Ledger.GetTrimmedBlock(snapshot, NativeContract.Ledger.GetBlockHash(snapshot, state.BlockIndex));
                    json["blockhash"] = block.Hash.ToString();
                    json["confirmations"] = NativeContract.Ledger.CurrentIndex(snapshot) - block.Index + 1;
                    json["blocktime"] = block.Header.Timestamp;
                }
                return json;
            }
        }

        protected static JObject BlockToJson(Block block, ProtocolSettings settings)
        {
            JObject json = block.ToJson(settings);
            json["tx"] = block.Transactions.Select(p => TransactionToJson(p, settings)).ToArray();
            return json;
        }

        protected static JObject TransactionToJson(Transaction tx, ProtocolSettings settings)
        {
            JObject json = tx.ToJson(settings);
            json["sysfee"] = tx.SystemFee.ToString();
            json["netfee"] = tx.NetworkFee.ToString();
            return json;
        }

        protected static JObject ToJson(StackItem item, int max)
        {
            JObject json = item.ToJson();
            if (item is InteropInterface interopInterface && interopInterface.GetInterface<object>() is IIterator iterator)
            {
                JArray array = new();
                while (max > 0 && iterator.Next())
                {
                    array.Add(iterator.Value(null).ToJson());
                    max--;
                }
                json["iterator"] = array;
                json["truncated"] = iterator.Next();
            }
            return json;
        }

        protected static Signer[] SignersFromJson(JArray _params, ProtocolSettings settings)
        {
            var ret = _params.Select(u => new Signer
            {
                Account = AddressToScriptHash(u!["account"]!.AsString(), settings.AddressVersion),
                Scopes = (WitnessScope)Enum.Parse(typeof(WitnessScope), u["scopes"]?.AsString()!),
                AllowedContracts = ((JArray)u["allowedcontracts"]!)?.Select(p => UInt160.Parse(p!.AsString())).ToArray(),
                AllowedGroups = ((JArray)u["allowedgroups"]!)?.Select(p => ECPoint.Parse(p!.AsString(), ECCurve.Secp256r1)).ToArray(),
                Rules = ((JArray)u["rules"]!)?.Select(r => WitnessRule.FromJson((JObject)r!)).ToArray(),
            }).ToArray();

            // Validate format

            _ = IO.Helper.ToByteArray(ret).AsSerializableArray<Signer>();

            return ret;
        }

        protected static Witness[] WitnessesFromJson(JArray _params)
        {
            return _params.Select(u => new
            {
                Invocation = u!["invocation"]?.AsString(),
                Verification = u["verification"]?.AsString()
            }).Where(x => x.Invocation != null || x.Verification != null).Select(x => new Witness()
            {
                InvocationScript = Convert.FromBase64String(x.Invocation ?? string.Empty),
                VerificationScript = Convert.FromBase64String(x.Verification ?? string.Empty)
            }).ToArray();
        }

        protected static string? GetExceptionMessage(Exception exception)
        {
            var baseException = exception?.GetBaseException();
            string returned;
            if (baseException != null)
            {
                returned = baseException.StackTrace + "\n" + baseException.Message;
                if (returned.Contains("Cannot Call Method "))
                    returned += "\n!!!Check whether you have written [ContractPermission(\"*\", \"*\")] in your contract!!!";
                return returned;
            }
            else
                return null;
        }
    }
}
