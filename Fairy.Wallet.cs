using Neo.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using static System.IO.Path;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public class FairyAccount : WalletAccount
        {
            public readonly Wallet wallet;
            public readonly KeyPair key;
            public readonly string nep2key;
            public readonly string password;

            public FairyAccount(Wallet wallet, UInt160 scriptHash, string nep2key, KeyPair key)
                : base(scriptHash, wallet.ProtocolSettings)
            {
                this.wallet = wallet;
                this.key = key;
                this.nep2key = nep2key;
            }

            public FairyAccount(Wallet wallet, UInt160 scriptHash, KeyPair key, string password)
                : this(wallet, scriptHash, key.Export(password, wallet.ProtocolSettings.AddressVersion, N:16384, r:8, p:8), key)
            {
                this.password = password;
            }
            public override bool HasKey => true;
            public override KeyPair GetKey() => key;
        }

        public class FairyWallet : Wallet
        {
            public readonly FairyAccount account;
            public readonly string password;
            public override string Name => "Fairy";
            public override Version Version => Version.Parse("68");

            public FairyWallet(ProtocolSettings settings, string nep2="6PYKrXGB2bhiux49bKYJPMMpaVic6SRrJcCLC8tdrz3YPLgktpe3H3PN35", string password="1", int N = 16384, int r = 8, int p = 8): base("./fairy/path", settings)
            {
                this.password = password;
                KeyPair key = new(GetPrivateKeyFromNEP2(nep2, password, ProtocolSettings.AddressVersion, N, r, p));
                Contract contract = new()
                {
                    Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                    ParameterList = new[] { ContractParameterType.Signature },
                };
                account = new FairyAccount(this, contract.ScriptHash, nep2, key);
                account.Contract = contract;
            }
            public FairyWallet(string WIF, string password, ProtocolSettings settings) : base("./fairy/path", settings)
            {
                this.password = password;
                KeyPair key = new(GetPrivateKeyFromWIF(WIF));
                Contract contract = new()
                {
                    Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                    ParameterList = new[] { ContractParameterType.Signature },
                };
                account = new FairyAccount(this, contract.ScriptHash, key, password);
                account.Contract = contract;
            }
            public override bool ChangePassword(string oldPassword, string newPassword) => throw new NotImplementedException();
            public override bool Contains(UInt160 scriptHash) => throw new NotImplementedException();
            public override WalletAccount CreateAccount(byte[] privateKey) => throw new NotImplementedException();
            public override WalletAccount CreateAccount(Contract contract, KeyPair key = null) => throw new NotImplementedException();
            public override WalletAccount CreateAccount(UInt160 scriptHash) => throw new NotImplementedException();
            public override void Delete() => throw new NotImplementedException();
            public override bool DeleteAccount(UInt160 scriptHash) => throw new NotImplementedException();
            public override WalletAccount GetAccount(UInt160 scriptHash) => account;  // Always return the account regardless of scriptHash
            public override IEnumerable<WalletAccount> GetAccounts() => new List<WalletAccount> { account };
            public override bool VerifyPassword(string password) => password == this.password;
            public override void Save() => throw new NotImplementedException();
        }

        protected Wallet defaultFairyWallet;
        protected Witness[] defaultWitness = { new Witness { InvocationScript=(new byte[2] { 0x0c, 0x40 }).Concat(new byte[64]).ToArray(), VerificationScript=(new byte[2] { 0x0c, 0x21 }).Concat(new byte[33]).Concat(new byte[5] { 0x41, 0x56, 0xe7, 0xb3, 0x27 }).ToArray() } };

        [RpcMethod]
        protected virtual JToken OpenDefaultFairyWallet(JArray _params)
        {
            string path = _params[0].AsString();
            string password = _params[1].AsString();
            if (!File.Exists(path)) throw new FileNotFoundException();
            switch (GetExtension(path))
            {
                case ".json":
                    {
                        NEP6Wallet nep6wallet = new(path, password, system.Settings);
                        defaultFairyWallet = nep6wallet;
                        break;
                    }
                default:
                    throw new NotSupportedException();
            }
            return true;
        }

        [RpcMethod]
        protected virtual JToken ResetDefaultFairyWallet(JArray _params)
        {
            FairyWallet defaultWallet = new FairyWallet(system.Settings);
            defaultFairyWallet = defaultWallet;
            JObject json = new();
            json[defaultWallet.account.key.PublicKey.ToString()] = defaultWallet.account.ScriptHash.ToString();
            return true;
        }

        [RpcMethod]
        protected virtual JToken SetSessionFairyWalletWithNep2(JArray _params)
        {
            string sessionString = _params[0].AsString();
            FairySession session = sessionStringToFairySession[sessionString];
            string nep2 = _params[1].AsString();
            string password = _params[2].AsString();
            FairyWallet wallet = new FairyWallet(system.Settings, nep2:nep2, password:password);
            session.engine.runtimeArgs.fairyWallet = wallet;
            JObject json = new();
            json[wallet.account.key.PublicKey.ToString()] = wallet.account.ScriptHash.ToString();
            return json;
        }

        [RpcMethod]
        protected virtual JToken SetSessionFairyWalletWithWif(JArray _params)
        {
            string sessionString = _params[0].AsString();
            FairySession session = sessionStringToFairySession[sessionString];
            string wif = _params.Count >= 2 ? _params[1].AsString() : "Fairy";
            string password = _params.Count >= 3 ? _params[2].AsString() : "1";
            FairyWallet wallet = new FairyWallet(wif, password: password, system.Settings);
            session.engine.runtimeArgs.fairyWallet = wallet;
            JObject json = new();
            json[wallet.account.key.PublicKey.ToString()] = wallet.account.ScriptHash.ToString();
            return json;
        }

        internal static UInt160 AddressToScriptHash(string address, byte version)
        {
            if (UInt160.TryParse(address, out var scriptHash))
                return scriptHash;
            return address.ToScriptHash(version);
        }

        [RpcMethod]
        protected virtual JObject ForceSignTransaction(JArray _params)
        {
            string session = _params[0].AsString();

            FairySession fairySession;
            if (!sessionStringToFairySession.TryGetValue(session, out fairySession))
            {  // we allow initializing a new session when executing
                fairySession = NewFairySession(system, this);
                sessionStringToFairySession[session] = fairySession;
            }
            Wallet signatureWallet = fairySession.engine.runtimeArgs.fairyWallet == null ? defaultFairyWallet : fairySession.engine.runtimeArgs.fairyWallet;
            DataCache snapshotForSignature = fairySession.engine.Snapshot.CreateSnapshot();

            byte[] script = Convert.FromBase64String(_params[1].AsString());
            Signer[]? signers = _params.Count >= 3 ? SignersFromJson((JArray)_params[2], system.Settings) : null;
            if (signers != null && (signers.Length > 1 || signers[0].Account != signatureWallet.GetAccounts().First().ScriptHash))
                throw new("Multiple signature not supported by FairyWallet for now");
            //JArray WIFprivateKeys = (JArray)_params[...];
            long systemFee = _params.Count >= 4 ? long.Parse(_params[3].AsString()) : 1000_0000;
            long? networkFee = _params.Count >= 5 ? long.Parse(_params[4].AsString()) : null;
            uint validUntilBlock = _params.Count >= 6 ? uint.Parse(_params[5].AsString()) : NativeContract.Ledger.CurrentIndex(snapshotForSignature) + 5760;
            uint nonce = _params.Count >= 7 ? uint.Parse(_params[6].AsString()) : (uint)new Random().Next();

            Transaction tx = new()
            {
                Version = 0,
                Nonce = nonce,
                Script = script,
                ValidUntilBlock = validUntilBlock,
                Signers = signers,
                Attributes = Array.Empty<TransactionAttribute>(),
            };
            tx.SystemFee = systemFee;
            tx.NetworkFee = networkFee ?? signatureWallet.CalculateNetworkFee(snapshotForSignature, tx);

            ContractParametersContext context = new(snapshotForSignature, tx, system.Settings.Network);
            signatureWallet.Sign(context);
            tx.Witnesses = context.GetWitnesses();

            JObject result = new();
            result["gasconsumed"] = systemFee;
            result["tx"] = Convert.ToBase64String(tx.ToArray());
            result["txHash"] = tx.Hash.ToString();
            result["networkfee"] = tx.NetworkFee;
            result["nonce"] = nonce;
            result["witness"] = tx.Witnesses.Select(w => w.ToJson()).ToArray();
            if (!context.Completed)
                result["pendingsignature"] = context.ToJson();
            return result;
        }
    }
}
