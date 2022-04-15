// include this file in neo-modules/src/RpcServer/RpcServer.csproj
// and build your own RpcServer

using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.Plugins
{
    public partial class RpcServer
    {
        Dictionary<string, ApplicationEngine> sessionToEngine = new Dictionary<string, ApplicationEngine>();

        [RpcMethod]
        protected virtual JObject InvokeFunctionWithSession(JArray _params)
        {
            string session = _params[0].ToString();
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
            string session = _params[0].ToString();
            bool writeSnapshot = _params[1].AsBoolean();
            byte[] script = Convert.FromBase64String(_params[2].AsString());
            Signers signers = _params.Count >= 4 ? SignersFromJson((JArray)_params[3], system.Settings) : null;
            return GetInvokeResultWithSession(session, writeSnapshot, script, signers);
        }

        [RpcMethod]
        protected virtual JObject DeleteSession(JArray _params)
        {
            int count = _params.Count;
            JObject json = new();
            for (int i = 0; i<count; i++)
            {
                json[i] = sessionToEngine.Remove(_params[i].AsString());
            }
            return json;
        }

        [RpcMethod]
        protected virtual JObject ListSession()
        {
            JArray session = new JArray();
            foreach (JString s in sessionToEngine.Keys.Select(s => (JString)s))
            {
                session.Add(s);
            }
            return session;
        }

        private JObject GetInvokeResultWithSession(string session, bool writeSnapshot, byte[] script, Signers signers = null)
        {
            Transaction tx = signers == null ? null : new Transaction
            {
                Signers = signers.GetSigners(),
                Attributes = System.Array.Empty<TransactionAttribute>(),
                Witnesses = signers.Witnesses,
            };
            ApplicationEngine engine;
            engine = sessionToEngine.TryGetValue(session, out engine)
                ? ApplicationEngine.Run(script, engine.Snapshot, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke)
                : ApplicationEngine.Run(script, system.StoreView, container: tx, settings: system.Settings, gas: settings.MaxGasInvoke);
            if (writeSnapshot)
                sessionToEngine[session] = engine;
            JObject json = new();
            json["script"] = Convert.ToBase64String(script);
            json["state"] = engine.State;
            json["gasconsumed"] = engine.GasConsumed.ToString();
            json["exception"] = GetExceptionMessage(engine.FaultException);
            try
            {
                json["stack"] = new JArray(engine.ResultStack.Select(p => ToJson(p, settings.MaxIteratorResultItems)));
            }
            catch (InvalidOperationException)
            {
                json["stack"] = "error: invalid operation";
            }
            if (engine.State != VMState.FAULT)
            {
                ProcessInvokeWithWallet(json, signers);
            }
            return json;
        }
    }
}
