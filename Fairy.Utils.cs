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
        [RpcMethod]
        protected virtual JObject VirtualDeploy(JArray _params)
        {
            if (defaultFairyWallet == null)
                throw new Exception("Please open a wallet before deploying a contract.");
            string session = _params[0]!.AsString();
            NefFile nef = Convert.FromBase64String(_params[1]!.AsString()).AsSerializable<NefFile>();
            ContractManifest manifest = ContractManifest.Parse(_params[2]!.AsString());
            Signer[] signers = SignersFromJson((JArray)_params[3]!, system.Settings);
            FairySession testSession = GetOrCreateFairySession(session);
            DataCache snapshot = testSession.engine.Snapshot;
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
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
                    throw ex;
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

        [RpcMethod]
        protected virtual JToken GetContract(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 hash = UInt160.Parse(_params[1]!.AsString());
            ContractState contractState = NativeContract.ContractManagement.GetContract(
                session == null ? system.StoreView : sessionStringToFairySession[session].engine.Snapshot,
                hash);
            return contractState.ToJson();
        }


        /// <summary>
        /// Wait until the transaction is included in blocks
        /// </summary>
        /// <param name="_params">UInt256String; bool(verbose); waitBlockCount</param>
        /// <returns></returns>
        /// <exception cref="RpcException"></exception>
        [RpcMethod]
        protected virtual JToken AwaitConfirmedTransaction(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0]!.AsString());
            bool verbose = _params.Count >= 2 && _params[1]!.AsBoolean();
            uint waitBlockCount = _params.Count >= 2 ? uint.Parse(_params[2]!.AsString()) : 2;
            JToken json = GetConfirmedTransaction(hash, verbose);
            if (json != null)
                return json;
            SemaphoreSlim signal = new SemaphoreSlim(0, 1);
            uint count = 0;
            CommittedHandler getConfirmedTransactionAfterCommitted = delegate(NeoSystem @system, Block @block){ json = GetConfirmedTransaction(hash, verbose); count += 1; signal.Release(); };
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
            throw new RpcException(-100, $"Transaction not found in {waitBlockCount} blocks");
        }

        [RpcMethod]
        protected virtual JObject PutStorageWithSession(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] key = Convert.FromBase64String(keyBase64);
            string valueBase64 = _params[3]!.AsString();
            byte[] value = Convert.FromBase64String(valueBase64);

            FairySession testSession = sessionStringToFairySession[session];
            ContractState contractState = NativeContract.ContractManagement.GetContract(testSession.engine.Snapshot, contract);
            StorageKey storageKey = new StorageKey { Id=contractState.Id, Key=key };
            testSession.engine.Snapshot.Delete(storageKey);
            if (value.Length > 0)
                testSession.engine.Snapshot.Add(new StorageKey { Id=contractState.Id, Key=key }, new StorageItem(value));
            testSession.engine.Snapshot.Commit();
            JObject json = new();
            json[keyBase64] = valueBase64;
            return new JObject();
        }

        [RpcMethod]
        protected virtual JObject GetStorageWithSession(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] key = Convert.FromBase64String(keyBase64);

            ContractState contractState;
            JObject json = new();
            StorageItem item;

            if (session == null)
            {   // use current actual blockchain state, instead of a fairy session
                DataCache storeView = system.StoreView;
                contractState = NativeContract.ContractManagement.GetContract(storeView, contract);
                item = storeView.TryGet(new StorageKey { Id=contractState.Id, Key=key });
                json[keyBase64] = item == null ? null : Convert.ToBase64String(item.Value.ToArray());
                return json;
            }

            FairyEngine oldEngine = sessionStringToFairySession[session].engine;
            contractState = NativeContract.ContractManagement.GetContract(oldEngine.Snapshot, contract);
            item = oldEngine.Snapshot.TryGet(new StorageKey { Id=contractState.Id, Key=key });
            json[keyBase64] = item == null ? null : Convert.ToBase64String(item.Value.ToArray());
            return json;
        }

        [RpcMethod]
        protected virtual JObject FindStorageWithSession(JArray _params)
        {
            string? session = _params[0]?.AsString();
            UInt160 contract = UInt160.Parse(_params[1]!.AsString());
            string keyBase64 = _params[2]!.AsString();
            byte[] prefix = Convert.FromBase64String(keyBase64);

            DataCache snapshot;
            if (session == null)
            {   // use current actual blockchain state, instead of a fairy session
                snapshot = system.StoreView;
            }
            else
            {
                FairyEngine oldEngine = sessionStringToFairySession[session].engine;
                snapshot = oldEngine.Snapshot;
            }
            ContractState contractState = NativeContract.ContractManagement.GetContract(snapshot, contract);
            JObject json = new();
            foreach (var (key, value) in snapshot.Find(StorageKey.CreateSearchPrefix(contractState.Id, prefix)))
                json[Convert.ToBase64String(key.Key.ToArray())] = Convert.ToBase64String(value.ToArray());
            return json;
        }

        [RpcMethod]
        protected virtual JObject GetTime(JArray _params)
        {
            JObject json = new();
            if (_params.Count >= 1)
                json["time"] = sessionStringToFairySession[_params[0]!.AsString()].engine.GetFairyTime();  // usually you can use GetSnapshotTimeStamp instead of this method
            else
                json["time"] = FairyEngine.Run(new byte[] { 0x40 }, system.StoreView, this, settings: system.Settings, gas: settings.MaxGasInvoke).GetTime();
            return json;
        }

        [RpcMethod]
        protected virtual JObject SetNeoBalance(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 account = UInt160.Parse(_params[1]!.AsString());
            ulong balance = ulong.Parse(_params[2]!.AsString());
            return SetTokenBalance(session, neoScriptHash, account, balance, Native_Prefix_Account);
        }

        [RpcMethod]
        protected virtual JObject SetGasBalance(JArray _params)
        {
            string session = _params[0]!.AsString();
            UInt160 account = UInt160.Parse(_params[1]!.AsString());
            ulong balance = ulong.Parse(_params[2]!.AsString());
            return SetTokenBalance(session, gasScriptHash, account, balance, Native_Prefix_Account);
        }

        [RpcMethod]
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
                StorageItem storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id=contractState.Id, Key=key }, () => new StorageItem(new AccountState()));
                AccountState state = storage.GetInteroperable<AccountState>();
                storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id=contractState.Id, Key=new byte[] { Native_Prefix_TotalSupply } }, () => new StorageItem(BigInteger.Zero));
                storage.Add(balance - state.Balance);
                state.Balance = balance;
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
            else if (contract == neoScriptHash)
            {
                prefixAccount = Native_Prefix_Account;
                byte[] key = new byte[] { prefixAccount }.Concat(account.ToArray()).ToArray();
                StorageItem storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id=contractState.Id, Key=key }, () => new StorageItem(new NeoToken.NeoAccountState()));
                NeoToken.NeoAccountState state = storage.GetInteroperable<NeoToken.NeoAccountState>();
                storage = oldEngine.Snapshot.GetAndChange(new StorageKey { Id=contractState.Id, Key=new byte[] { Native_Prefix_TotalSupply } }, () => new StorageItem(BigInteger.Zero));
                storage.Add(balance - state.Balance);
                state.Balance = balance;
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
            else
            {
                byte[] key = new byte[] { prefixAccount }.Concat(account.ToArray()).ToArray();
                oldEngine.Snapshot.GetAndChange(new StorageKey { Id=contractState.Id, Key=key }, () => new StorageItem(balanceBytes));
                json[Convert.ToBase64String(key)] = Convert.ToBase64String(balanceBytes);
                return json;
            }
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

        protected static JObject NativeContractToJson(NativeContract contract, ProtocolSettings settings)
        {
            return new JObject
            {
                ["id"] = contract.Id,
                ["hash"] = contract.Hash.ToString(),
                ["nef"] = contract.Nef.ToJson(),
                ["manifest"] = contract.Manifest.ToJson(),
                ["updatehistory"] = settings.NativeUpdateHistory[contract.Name].Select(p => (JToken)p).ToArray()
            };
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
                if (returned.Contains("Cannot Call Method Neo.SmartContract.Manifest.ContractMethodDescriptor"))
                    returned += "\n!!!Check whether you have written [ContractPermission(\"*\", \"*\")] in your contract!!!";
                return returned;
            }
            else
                return null;
        }
    }
}
