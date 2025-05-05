// Copyright (C) 2015-2025 The Neo Project.
//
// Fairy.Wallet.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.Json;
using Neo.Network.P2P;
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

            public FairyAccount(Wallet wallet, UInt160 scriptHash, KeyPair key)
                : base(scriptHash, wallet.ProtocolSettings)
            {
                this.wallet = wallet;
                this.key = key;
            }

            public override bool HasKey => true;
            public override KeyPair GetKey() => key;
        }

        public class FairyWallet : Wallet
        {
            public readonly List<FairyAccount> accounts = new();
            public FairyAccount account { get => accounts[0]; }
            public override string Name => "Fairy";
            public override Version Version => Version.Parse("68");

            public FairyWallet(ProtocolSettings settings, string nep2 = "6PYKrXGB2bhiux49bKYJPMMpaVic6SRrJcCLC8tdrz3YPLgktpe3H3PN35", string password = "1", int N = 16384, int r = 8, int p = 8) : base("./fairy/path", settings) => CreateAccountFromNep2(nep2, password, N, r, p);
            public FairyWallet(string wif, ProtocolSettings settings) : base("./fairy/path", settings) => CreateAccountFromWif(wif);
            public override bool ChangePassword(string oldPassword, string newPassword) => throw new NotImplementedException($"Password is unnecessary for {nameof(FairyWallet)}");
            public override bool Contains(UInt160 scriptHash) => GetAccount(scriptHash) != null;
            public WalletAccount CreateAccountFromNep2(string nep2, string password, int N = 16384, int r = 8, int p = 8) => CreateAccount(GetPrivateKeyFromNEP2(nep2, password, ProtocolSettings.AddressVersion, N, r, p));
            public WalletAccount CreateAccountFromWif(string wif) => CreateAccount(GetPrivateKeyFromWIF(wif));
            public override WalletAccount CreateAccount(byte[] privateKey)
            {
                KeyPair key = new(privateKey);
                Contract contract = new()
                {
                    Script = Contract.CreateSignatureRedeemScript(key.PublicKey),
                    ParameterList = new[] { ContractParameterType.Signature },
                };
                return CreateAccount(contract, key);
            }
            public override WalletAccount CreateAccount(Contract contract, KeyPair? key = null)
            {
                foreach (WalletAccount acc in accounts)
                    if (acc.ScriptHash == contract.ScriptHash)
                        return acc;
                if (key == null)
                    throw new NotImplementedException("privateKey==null not supported for now");
                FairyAccount account = new FairyAccount(this, contract.ScriptHash, key)
                {
                    Contract = contract
                };
                accounts.Add(account);
                return account;
            }
            public override WalletAccount CreateAccount(UInt160 scriptHash) => throw new NotImplementedException("privateKey==null not supported for now");
            public override void Delete() => throw new NotImplementedException();
            public override bool DeleteAccount(UInt160 scriptHash)
            {
                foreach (FairyAccount account in accounts)
                    if (account.ScriptHash == scriptHash)
                    {
                        accounts.Remove(account);
                        return true;
                    }
                return false;
            }
            public override WalletAccount? GetAccount(UInt160 scriptHash)
            {
                foreach (WalletAccount account in accounts)
                    if (account.ScriptHash == scriptHash)
                        return account;
                return accounts[0];  // Return the default account! Otherwise it becomes difficult to simulate MakeTransaction for a single account
            }
            public override IEnumerable<WalletAccount> GetAccounts() => accounts;
            public override bool VerifyPassword(string password) => true;
            public override void Save() => throw new NotImplementedException();
        }

        protected Wallet defaultFairyWallet;
        protected Witness[] defaultWitness = { new Witness { InvocationScript = (new byte[2] { 0x0c, 0x40 }).Concat(new byte[64]).ToArray(), VerificationScript = (new byte[2] { 0x0c, 0x21 }).Concat(new byte[33]).Concat(new byte[5] { 0x41, 0x56, 0xe7, 0xb3, 0x27 }).ToArray() } };

        [FairyRpcMethod]
        protected virtual JToken OpenDefaultFairyWallet(JArray _params)
        {
            string path = _params[0]!.AsString();
            string password = _params[1]!.AsString();
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

        [FairyRpcMethod]
        protected virtual JToken ResetDefaultFairyWallet(JArray _params)
        {
            FairyWallet defaultWallet = new FairyWallet(system.Settings);
            defaultFairyWallet = defaultWallet;
            JObject json = new();
            json[defaultWallet.account.key.PublicKey.ToString()] = defaultWallet.account.ScriptHash.ToString();
            return true;
        }

        [FairyRpcMethod]
        protected virtual JToken SetSessionFairyWalletWithNep2(JArray _params)
        {
            string sessionString = _params[0]!.AsString();
            FairySession session = sessionStringToFairySession[sessionString];
            JObject json = new();
            string nep2 = _params[1]!.AsString();
            string password = _params[2]!.AsString();
            FairyWallet wallet = new FairyWallet(system.Settings, nep2: nep2, password: password);
            json[wallet.account.key.PublicKey.ToString()] = wallet.account.ScriptHash.ToString();
            for (int i = 3; i < _params.Count; i += 2)
            {
                nep2 = _params[i]!.AsString();
                password = _params[i + 1]!.AsString();
                wallet.CreateAccountFromNep2(nep2, password);
                json[wallet.accounts.Last().key.PublicKey.ToString()] = wallet.accounts.Last().ScriptHash.ToString();
            }
            session.engine.runtimeArgs.fairyWallet = wallet;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken SetSessionFairyWalletWithWif(JArray _params)
        {
            string sessionString = _params[0]!.AsString();
            FairySession session = sessionStringToFairySession[sessionString];
            string wif = _params[1]!.AsString();
            FairyWallet wallet = new FairyWallet(wif, system.Settings);
            JObject json = new();
            json[wallet.account.key.PublicKey.ToString()] = wallet.account.ScriptHash.ToString();
            for (int i = 2; i < _params.Count; ++i)
            {
                wif = _params[i]!.AsString();
                wallet.CreateAccountFromWif(wif);
                json[wallet.accounts.Last().key.PublicKey.ToString()] = wallet.accounts.Last().ScriptHash.ToString();
            }
            session.engine.runtimeArgs.fairyWallet = wallet;
            return json;
        }

        internal static UInt160 AddressToScriptHash(string address, byte version)
        {
            if (UInt160.TryParse(address, out var scriptHash))
                return scriptHash;
            return address.ToScriptHash(version);
        }

        [FairyRpcMethod]
        protected virtual JObject ForceVerifyWithECDsa(JArray _params)
        {
            byte[] message = Convert.FromBase64String(_params[0]!.AsString());
            byte[] pubkey = Convert.FromBase64String(_params[1]!.AsString());
            byte[] signature = Convert.FromBase64String(_params[2]!.AsString());
            NamedCurveHash namedCurveHash = _params.Count > 3 ? _params[3]!.AsEnum<NamedCurveHash>() : NamedCurveHash.secp256r1SHA256;
            JObject json = new();
            json["result"] = CryptoLib.VerifyWithECDsa(message, pubkey, signature, namedCurveHash);
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject ForceSignMessage(JArray _params)
        {
            string session = _params[0]!.AsString();
            FairySession fairySession = GetOrCreateFairySession(session);
            Wallet signatureWallet = fairySession.engine.runtimeArgs.fairyWallet == null ? defaultFairyWallet : fairySession.engine.runtimeArgs.fairyWallet;
            byte[] message = Convert.FromBase64String(_params[1]!.AsString());
            NamedCurveHash namedCurveHash = _params.Count > 2 ? _params[2]!.AsEnum<NamedCurveHash>() : NamedCurveHash.secp256r1SHA256;
            (Cryptography.ECC.ECCurve curve, HashAlgorithm HashAlgorithm) = namedCurveHash switch
            {
                NamedCurveHash.secp256k1SHA256 =>
                    (Cryptography.ECC.ECCurve.Secp256k1, HashAlgorithm.SHA256),
                NamedCurveHash.secp256r1SHA256 =>
                    (Cryptography.ECC.ECCurve.Secp256r1, HashAlgorithm.SHA256),
                NamedCurveHash.secp256k1Keccak256 =>
                    (Cryptography.ECC.ECCurve.Secp256k1, HashAlgorithm.Keccak256),
                NamedCurveHash.secp256r1Keccak256 =>
                    (Cryptography.ECC.ECCurve.Secp256r1, HashAlgorithm.Keccak256),
                _ => throw new NotImplementedException($"Invalid namedCurveHash {namedCurveHash}"),
            };
            JObject json = new();
            KeyPair keyPair = signatureWallet.GetAccounts().First().GetKey();
            json["signed"] = Convert.ToBase64String(
                Crypto.Sign(message, keyPair.PrivateKey, curve, HashAlgorithm)
            );
            return json;
        }

        [FairyRpcMethod]
        protected virtual JObject ForceSignTransaction(JArray _params)
        {
            string session = _params[0]!.AsString();

            FairySession fairySession = GetOrCreateFairySession(session);
            Wallet signatureWallet = fairySession.engine.runtimeArgs.fairyWallet == null ? defaultFairyWallet : fairySession.engine.runtimeArgs.fairyWallet;
            DataCache snapshotForSignature = fairySession.engine.SnapshotCache.CloneCache();

            byte[] script = Convert.FromBase64String(_params[1]!.AsString());
            Signer[]? signers = _params.Count >= 3 ? SignersFromJson((JArray)_params[2]!, system.Settings) : null;
            if (signers != null && (signers.Length > 1 || signers[0].Account != signatureWallet.GetAccounts().First().ScriptHash))
                throw new("Multiple signature not supported by FairyWallet for now");
            //JArray WIFprivateKeys = (JArray)_params[...];
            long systemFee = _params.Count >= 4 ? long.Parse(_params[3]!.AsString()) : 1000_0000;
            long? networkFee = _params.Count >= 5 ? long.Parse(_params[4]!.AsString()) : null;
            uint validUntilBlock = _params.Count >= 6 ? uint.Parse(_params[5]!.AsString()) : NativeContract.Ledger.CurrentIndex(snapshotForSignature) + 5760;
            uint nonce = _params.Count >= 7 ? uint.Parse(_params[6]!.AsString()) : (uint)new Random().Next();

            Transaction tx = new()
            {
                Version = 0,
                Nonce = nonce,
                Script = script,
                ValidUntilBlock = validUntilBlock,
                Signers = signers,
                Attributes = Array.Empty<TransactionAttribute>(),
                SystemFee = systemFee
            };
            tx.NetworkFee = networkFee ?? tx.CalculateNetworkFee(snapshotForSignature, system.Settings, (a) => signatureWallet.GetAccount(a)?.Contract?.Script);

            ContractParametersContext context = new(snapshotForSignature, tx, system.Settings.Network);
            signatureWallet.Sign(context);
            tx.Witnesses = context.GetWitnesses();

            JObject result = new();
            result["gasconsumed"] = systemFee;
            result["tx"] = Convert.ToBase64String(tx.ToArray());
            result["txHash"] = tx.Hash.ToString();
            result["txSignData"] = Convert.ToBase64String(tx.GetSignData(system.Settings.Network));
            result["networkfee"] = tx.NetworkFee;
            result["nonce"] = nonce;
            result["witness"] = tx.Witnesses.Select(w => w.ToJson()).ToArray();
            if (!context.Completed)
                result["pendingsignature"] = context.ToJson();
            return result;
        }
    }
}
