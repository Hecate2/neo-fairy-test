using Neo.Json;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="_params"></param>
        /// <returns>opcode -> coveredOrNot</returns>
        /// <exception cref="ArgumentException"></exception>
        [RpcMethod]
        protected virtual JObject GetContractOpCodeCoverage(JArray _params)
        {
            UInt160 scripthash = UInt160.Parse(_params[0]!.AsString());
            if (!contractScriptHashToInstructionPointerToCoverage.ContainsKey(scripthash))
                throw new ArgumentException($"Scripthash {scripthash} not registered for debugging. Call SetDebugInfo(scriptHash, nefDbgNfo, dumpNef) first");
            Dictionary<uint, bool> coverage = contractScriptHashToInstructionPointerToCoverage[scripthash];
            JObject json = new();
            foreach (KeyValuePair<uint, bool> pair in coverage)
                json[pair.Key.ToString()] = pair.Value;
            return json;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="_params"></param>
        /// <returns>SourceFilenameAndLineNum -> {opcode -> coveredOrNot}</returns>
        [RpcMethod]
        protected virtual JObject GetContractSourceCodeCoverage(JArray _params)
        {
            JObject opcodeCoverage = GetContractOpCodeCoverage(_params);
            UInt160 scripthash = UInt160.Parse(_params[0]!.AsString());
            JObject result = new();
            foreach ((uint opcode, SourceFilenameAndLineNum source) in contractScriptHashToAllInstructionPointerToSourceLineNum[scripthash])
            {
                string key = $"{source.sourceFilename}::line {source.lineNum}: {source.sourceContent}";
                if (result[key] == null)
                    result[key] = new JObject();
                result[key]![opcode.ToString()] = opcodeCoverage[opcode.ToString()];
            }
            return result;
        }

        [RpcMethod]
        protected virtual JObject ClearContractOpCodeCoverage(JArray _params)
        {
            UInt160 scripthash = UInt160.Parse(_params[0]!.AsString());
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
