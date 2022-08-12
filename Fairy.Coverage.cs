using Neo;
using Neo.Json;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        [RpcMethod]
        protected virtual JObject GetContractOpCodeCoverage(JArray _params)
        {
            UInt160 scripthash = UInt160.Parse(_params[0].AsString());
            if (!contractScriptHashToInstructionPointerToCoverage.ContainsKey(scripthash))
                throw new ArgumentException($"Scripthash {scripthash} not registered for debugging. Call SetDebugInfo(scriptHash, nefDbgNfo, dumpNef) first");
            Dictionary<uint, bool> coverage = contractScriptHashToInstructionPointerToCoverage[scripthash];
            JObject json = new();
            foreach (KeyValuePair<uint, bool> pair in coverage)
                json[pair.Key.ToString()] = pair.Value;
            return json;
        }

        [RpcMethod]
        protected virtual JObject ClearContractOpCodeCoverage(JArray _params)
        {
            UInt160 scripthash = UInt160.Parse(_params[0].AsString());
            if (!contractScriptHashToInstructionPointerToCoverage.ContainsKey(scripthash))
                throw new ArgumentException($"Scripthash {scripthash} not registered for debugging. Call SetDebugInfo(scriptHash, nefDbgNfo, dumpNef) first");
            Dictionary<uint, bool> coverage = contractScriptHashToInstructionPointerToCoverage[scripthash];
            JObject json = new();
            foreach (KeyValuePair<uint, bool> pair in coverage)
            {
                coverage[pair.Key] = false;
                json[pair.Key.ToString()] = false;
            }
            return json;
        }
    }
}
