// Copyright (C) 2015-2025 The Neo Project.
//
// Fairy.Oracle.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        [FairyRpcMethod]
        protected virtual JObject OracleFinish(JArray _params)
        {
            string session = _params[0]!.AsString();
            bool writeSnapshot = _params[1]!.AsBoolean();
            ulong oracleRequestId = ulong.Parse(_params[2]!.AsString());
            OracleResponseCode oracleResponseCode = (OracleResponseCode)byte.Parse(_params[3]!.AsString());
            byte[] result = Convert.FromBase64String(_params[4]!.AsString());
            bool debug = _params[5] == null ? false : _params[5]!.AsBoolean();

            FairySession testSession = GetOrCreateFairySession(session);
            FairyEngine oldEngine = testSession.engine;
            DataCache snapshot = oldEngine.SnapshotCache;
            OracleRequest request = NativeContract.Oracle.GetRequest(snapshot, oracleRequestId);
            uint height = NativeContract.Ledger.CurrentIndex(snapshot) + 1;
            ECPoint[] oracleNodes = NativeContract.RoleManagement.GetDesignatedByRole(snapshot, Role.Oracle, height);
            OracleResponse response = new OracleResponse() { Id = oracleRequestId, Code = oracleResponseCode, Result = result };
            Transaction tx = CreateResponseTx(snapshot, request, response, oracleNodes, system.Settings);

            JObject json;
            if (debug)
                json = DebugFairyTransaction(session, writeSnapshot, tx.Script, tx);
            else
                json = ExecuteFairyTransaction(session, writeSnapshot, tx.Script, tx);
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken? OracleJsonPath(JArray _params)
        {
            string j = _params[0]!.AsString();
            string jsonPath = _params[1]!.AsString();
            byte[]? result = OracleJsonPath(j, jsonPath);
            return result == null ? null : Convert.ToBase64String(result);
        }

        public static byte[]? OracleJsonPath(string input, string filterArgs)
        {
            if (string.IsNullOrEmpty(filterArgs))
                return Utility.StrictUTF8.GetBytes(input);

            JToken? beforeObject = JToken.Parse(input);
            JArray? afterObjects = beforeObject?.JsonPath(filterArgs);
            return afterObjects?.ToByteArray(false);
        }

        public static Transaction CreateResponseTx(DataCache snapshot, OracleRequest request, OracleResponse response, ECPoint[] oracleNodes, ProtocolSettings settings, bool useCurrentHeight = false)
        {
            var requestTx = NativeContract.Ledger.GetTransactionState(snapshot, request.OriginalTxid);
            var n = oracleNodes.Length;
            var m = n - (n - 1) / 3;
            var oracleSignContract = Contract.CreateMultiSigContract(m, oracleNodes);
            uint height = NativeContract.Ledger.CurrentIndex(snapshot);
            uint validUntilBlock;
            if (requestTx == null)
                validUntilBlock = height + settings.MaxValidUntilBlockIncrement;
            else
                validUntilBlock = requestTx.BlockIndex + settings.MaxValidUntilBlockIncrement;
            while (useCurrentHeight && validUntilBlock <= height)
                validUntilBlock += settings.MaxValidUntilBlockIncrement;
            var tx = new Transaction()
            {
                Version = 0,
                Nonce = unchecked((uint)response.Id),
                ValidUntilBlock = validUntilBlock,
                Signers = new[]
                {
                    new Signer
                    {
                        Account = NativeContract.Oracle.Hash,
                        Scopes = WitnessScope.None
                    },
                    new Signer
                    {
                        Account = oracleSignContract.ScriptHash,
                        Scopes = WitnessScope.None
                    }
                },
                Attributes = new[] { response },
                Script = OracleResponse.FixedScript,
                Witnesses = new Witness[2]
            };
            Dictionary<UInt160, Witness> witnessDict = new Dictionary<UInt160, Witness>
            {
                [oracleSignContract.ScriptHash] = new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = oracleSignContract.Script,
                },
                [NativeContract.Oracle.Hash] = new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = Array.Empty<byte>(),
                }
            };

            UInt160[] hashes = tx.GetScriptHashesForVerifying(snapshot);
            tx.Witnesses[0] = witnessDict[hashes[0]];
            tx.Witnesses[1] = witnessDict[hashes[1]];

            // Calculate network fee

            var oracleContract = NativeContract.ContractManagement.GetContract(snapshot, NativeContract.Oracle.Hash);
            var engine = ApplicationEngine.Create(TriggerType.Verification, tx, snapshot.CloneCache(), settings: settings);
            ContractMethodDescriptor md = oracleContract.Manifest.Abi.GetMethod("verify", -1);
            engine.LoadContract(oracleContract, md, CallFlags.None);
            //if (engine.Execute() != VMState.HALT) return null;
            engine.Execute();
            tx.NetworkFee += engine.FeeConsumed;

            var executionFactor = NativeContract.Policy.GetExecFeeFactor(snapshot);
            var networkFee = executionFactor * SmartContract.Helper.MultiSignatureContractCost(m, n);
            tx.NetworkFee += networkFee;

            // Base size for transaction: includes const_header + signers + script + hashes + witnesses, except attributes

            int sizeInv = 66 * m;
            int size = Transaction.HeaderSize + tx.Signers.GetVarSize() + tx.Script.GetVarSize()
                + hashes.Length.GetVarSize() + witnessDict[NativeContract.Oracle.Hash].Size
                + sizeInv.GetVarSize() + sizeInv + oracleSignContract.Script.GetVarSize();

            var feePerByte = NativeContract.Policy.GetFeePerByte(snapshot);
            if (response.Result.Length > OracleResponse.MaxResultSize)
            {
                response.Code = OracleResponseCode.ResponseTooLarge;
                response.Result = Array.Empty<byte>();
            }
            else if (tx.NetworkFee + (size + tx.Attributes.GetVarSize()) * feePerByte > request.GasForResponse)
            {
                response.Code = OracleResponseCode.InsufficientFunds;
                response.Result = Array.Empty<byte>();
            }
            size += tx.Attributes.GetVarSize();
            tx.NetworkFee += size * feePerByte;

            // Calcualte system fee

            tx.SystemFee = request.GasForResponse - tx.NetworkFee;

            return tx;
        }
    }
}
