using Neo.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using Neo.Wallets.SQLite;
using static System.IO.Path;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        private class DummyWallet : Wallet
        {
            public DummyWallet(ProtocolSettings settings) : base(null, settings) { }
            public override string Name => "";
            public override Version Version => new();

            public override bool ChangePassword(string oldPassword, string newPassword) => false;
            public override bool Contains(UInt160 scriptHash) => false;
            public override WalletAccount? CreateAccount(byte[] privateKey) => null;
            public override WalletAccount? CreateAccount(Contract contract, KeyPair? key = null) => null;
            public override WalletAccount? CreateAccount(UInt160 scriptHash) => null;
            public override void Delete() { }
            public override bool DeleteAccount(UInt160 scriptHash) => false;
            public override WalletAccount? GetAccount(UInt160 scriptHash) => null;
            public override IEnumerable<WalletAccount> GetAccounts() => Array.Empty<WalletAccount>();
            public override bool VerifyPassword(string password) => false;
            public override void Save() { }
        }

        protected Wallet? fairyWallet = null;

        [RpcMethod]
        protected virtual JToken OpenFairyWallet(JArray _params)
        {
            string path = _params[0].AsString();
            string password = _params[1].AsString();
            if (!File.Exists(path)) throw new FileNotFoundException();
            switch (GetExtension(path))
            {
                case ".db3":
                    {
                        fairyWallet = UserWallet.Open(path, password, system.Settings);
                        break;
                    }
                case ".json":
                    {
                        NEP6Wallet nep6wallet = new(path, password, system.Settings);
                        fairyWallet = nep6wallet;
                        break;
                    }
                default:
                    throw new NotSupportedException();
            }
            return true;
        }

        [RpcMethod]
        protected virtual JToken CloseFairyWallet(JArray _params)
        {
            fairyWallet = null;
            return true;
        }

        internal static UInt160 AddressToScriptHash(string address, byte version)
        {
            if (UInt160.TryParse(address, out var scriptHash))
            {
                return scriptHash;
            }

            return address.ToScriptHash(version);
        }
    }
}
