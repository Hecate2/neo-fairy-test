using Neo.Json;
using Neo.VM;
using Neo.SmartContract.Native;
using System.IO.Compression;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public struct SourceFilenameAndLineNum { public string sourceFilename; public uint lineNum; public string sourceContent; }
        public readonly ConcurrentDictionary<UInt160, HashSet<SourceFilenameAndLineNum>> contractScriptHashToSourceLineNums = new();
        public readonly ConcurrentDictionary<UInt160, Dictionary<uint, SourceFilenameAndLineNum>> contractScriptHashToInstructionPointerToSourceLineNum = new();  // stores only assembly instructions which are the beginning of source code lines. Used for setting source code breakpoints; DO NOT FILL sourceContent!
        public readonly ConcurrentDictionary<UInt160, Dictionary<uint, SourceFilenameAndLineNum>> contractScriptHashToAllInstructionPointerToSourceLineNum = new();  // stores a mapping of all instructions to corresponding source code lines. Used only for finding the source code from assembly.
        public readonly ConcurrentDictionary<UInt160, HashSet<string>> contractScriptHashToSourceLineFilenames = new();
        public readonly ConcurrentDictionary<UInt160, Dictionary<uint, OpCode>> contractScriptHashToInstructionPointerToOpCode = new();
        public readonly ConcurrentDictionary<UInt160, Dictionary<uint, bool>> contractScriptHashToInstructionPointerToCoverage = new();
        public readonly ConcurrentDictionary<UInt160, JObject> contractScriptHashToNefDbgNfo = new();
        struct DumpNefPatterns
        {
            public DumpNefPatterns() { }
            public Regex opCodeRegex = new Regex(@"^(\d+)\s(.*?)\s?(#\s.*)?$");  // 8039 SYSCALL 62-7D-5B-52 # System.Contract.Call SysCall
            public Regex sourceCodeRegex = new Regex(@"^#\sCode\s(.*\.cs)\sline\s(\d+):\s""(.*)""$");  // # Code NFTLoan.cs line 523: "ExecutionEngine.Assert((bool)Contract.Call(token, "transfer", CallFlags.All, tenant, Runtime.ExecutingScriptHash, neededAmount, tokenId, TRANSACTION_DATA), "NFT payback failed");"
            public Regex methodStartRegex = new Regex(@"^# Method\sStart\s(.*)$");  // # Method Start NFTLoan.NFTLoan.FlashBorrowDivisible
            public Regex methodEndRegex = new Regex(@"^# Method\sEnd\s(.*)$");  // # Method End NFTLoan.NFTLoan.FlashBorrowDivisible
        }
        readonly DumpNefPatterns dumpNefPatterns = new();

        public static string Unzip(byte[] zippedBuffer)
        {
            using var zippedStream = new MemoryStream(zippedBuffer);
            using var archive = new ZipArchive(zippedStream);
            var entry = archive.Entries.FirstOrDefault();
            if (entry != null)
            {
                using var unzippedEntryStream = entry.Open();
                using var ms = new MemoryStream();
                unzippedEntryStream.CopyTo(ms);
                var unzippedArray = ms.ToArray();
                return Encoding.UTF8.GetString(unzippedArray);
            }
            throw new ArgumentException("No file found in zip archive");
        }

        [RpcMethod]
        protected virtual JToken SetDebugInfo(JArray _params)
        {
            string param0 = _params[0]!.AsString();
            UInt160 scriptHash = UInt160.Parse(param0);
            // nccs YourContractProject.csproj --debug
            // find .nefdbgnfo beside your .nef contract, and
            // give me the base64encode(content) of .nefdbgnfo file
            JObject nefDbgNfo = (JObject)JObject.Parse(Unzip(Convert.FromBase64String(_params[1]!.AsString())))!;
            contractScriptHashToNefDbgNfo[scriptHash] = nefDbgNfo;
            // https://github.com/devhawk/DumpNef
            // dumpnef contract.nef > contract.nef.txt
            // give me the content of that txt file!
            string dumpNef = _params[2]!.AsString();
            string[] lines = dumpNef.Replace("\r", "").Split("\n", StringSplitOptions.RemoveEmptyEntries);

            HashSet<SourceFilenameAndLineNum> sourceFilenameAndLineNums = new();
            contractScriptHashToSourceLineNums[scriptHash] = sourceFilenameAndLineNums;
            Dictionary<uint, SourceFilenameAndLineNum> InstructionPointerToSourceLineNum = new();
            contractScriptHashToInstructionPointerToSourceLineNum[scriptHash] = InstructionPointerToSourceLineNum;
            Dictionary<uint, SourceFilenameAndLineNum> AllInstructionPointerToSouceLineNum = new();
            contractScriptHashToAllInstructionPointerToSourceLineNum[scriptHash] = AllInstructionPointerToSouceLineNum;
            Dictionary<uint, OpCode> instructionPointerToOpCode = new();
            contractScriptHashToInstructionPointerToOpCode[scriptHash] = instructionPointerToOpCode;
            Dictionary<uint, bool> instructionPointerToCoverage = new();
            contractScriptHashToInstructionPointerToCoverage[scriptHash] = instructionPointerToCoverage;
            HashSet<string> filenames = new();
            contractScriptHashToSourceLineFilenames[scriptHash] = filenames;

            uint lineNum;
            for (lineNum = 0; lineNum < lines.Length; ++lineNum)
            {
                // foreach (var field in typeof(DumpNefPatterns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                //     ConsoleHelper.Info($"{field.Name}: {field.GetValue(dumpNefPatterns)}");
                Match match;
                match = dumpNefPatterns.sourceCodeRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    GroupCollection sourceCodeGroups = match.Groups;
                    uint sourceCodeLineNum = uint.Parse(sourceCodeGroups[2].ToString());
                    match = dumpNefPatterns.opCodeRegex.Match(lines[lineNum + 1]);
                    if (match.Success)
                    {
                        GroupCollection opcodeGroups = match.Groups;
                        uint instructionPointer = uint.Parse(opcodeGroups[1].ToString());
                        string filename = sourceCodeGroups[1].ToString();
                        filenames.Add(filename);
                        SourceFilenameAndLineNum sourceFilenameAndLineNum = new SourceFilenameAndLineNum { sourceFilename=filename, lineNum=sourceCodeLineNum };// , sourceContent = sourceCodeGroups[3].ToString() };
                        InstructionPointerToSourceLineNum[instructionPointer] = sourceFilenameAndLineNum;
                        sourceFilenameAndLineNums.Add(sourceFilenameAndLineNum);
                    }
                    continue;
                }
                match = dumpNefPatterns.opCodeRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    GroupCollection opcodeGroups = match.Groups;
                    uint instructionPointer = uint.Parse(opcodeGroups[1].ToString());
                    string[] opcodeAndOperand = opcodeGroups[2].ToString().Split();
                    instructionPointerToOpCode[instructionPointer] = (OpCode)Enum.Parse(typeof(OpCode), opcodeAndOperand[0]);
                    instructionPointerToCoverage[instructionPointer] = false;
                    continue;
                }
            }
            SourceFilenameAndLineNum parseState = new SourceFilenameAndLineNum { sourceFilename = "Undefined", lineNum = 0, sourceContent = "Undefined" };
            for (lineNum = 0; lineNum < lines.Length; ++lineNum)
            {
                Match match;
                match = dumpNefPatterns.methodStartRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    parseState.sourceFilename = match.Groups[1].ToString();
                    parseState.lineNum = 0;
                    parseState.sourceContent = match.Groups[1].ToString();
                    continue;
                }
                match = dumpNefPatterns.methodEndRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    parseState.sourceFilename = "Undefined";
                    parseState.lineNum = 0;
                    parseState.sourceContent = "Undefined";
                    continue;
                }
                match = dumpNefPatterns.sourceCodeRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    parseState.sourceFilename = match.Groups[1].ToString();
                    parseState.lineNum = uint.Parse(match.Groups[2].ToString());
                    parseState.sourceContent = match.Groups[3].ToString();
                    continue;
                }
                match = dumpNefPatterns.opCodeRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    AllInstructionPointerToSouceLineNum[uint.Parse(match.Groups[1].ToString())] = parseState;
                    continue;
                }
            }
            JObject json = new();
            json[param0] = true;
            return json;
        }

        [RpcMethod]
        protected virtual JToken ListDebugInfo(JArray _params)
        {
            JArray scriptHashes = new JArray();
            foreach (UInt160 s in contractScriptHashToInstructionPointerToSourceLineNum.Keys)
            {
                scriptHashes.Add(s.ToString());
            }
            return scriptHashes;
        }

        [RpcMethod]
        protected virtual JToken ListFilenamesOfContract(JArray _params)
        {
            string scriptHashStr = _params[0]!.AsString();
            UInt160 scriptHash = UInt160.Parse(scriptHashStr);
            List<string> filenameList = contractScriptHashToSourceLineFilenames[scriptHash].ToList();
            filenameList.Sort();
            JArray filenames = new JArray();
            foreach (string filename in filenameList)
            {
                filenames.Add(filename);
            }
            return filenames;
        }

        [RpcMethod]
        protected virtual JToken DeleteDebugInfo(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s!.AsString();
                UInt160 scriptHash = UInt160.Parse(str);
                contractScriptHashToSourceLineNums.Remove(scriptHash, out _);
                contractScriptHashToInstructionPointerToSourceLineNum.Remove(scriptHash, out _);
                contractScriptHashToSourceLineFilenames.Remove(scriptHash, out _);
                contractScriptHashToInstructionPointerToOpCode.Remove(scriptHash, out _);
                contractScriptHashToInstructionPointerToCoverage.Remove(scriptHash, out _);
                contractScriptHashToNefDbgNfo.Remove(scriptHash, out _);

                contractScriptHashToAssemblyBreakpoints.Remove(scriptHash, out _);
                contractScriptHashToSourceCodeBreakpoints.Remove(scriptHash, out _);
                json[str] = true;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JToken? GetMethodByInstructionPointer(JArray _params)
        {
            UInt160 scriptHash = UInt160.Parse(_params[0]!.AsString());
            uint instrcutionPointer = uint.Parse(_params[1]!.AsString());
            try
            {
                foreach (JObject? method in (JArray)contractScriptHashToNefDbgNfo[scriptHash]["methods"]!)
                {
                    string[] rangeStr = method!["range"]!.AsString().Split("-");
                    if (instrcutionPointer >= uint.Parse(rangeStr[0]) && instrcutionPointer <= uint.Parse(rangeStr[1]))
                        return method;
                }
            }
            catch (KeyNotFoundException)
            {
                string? contractName = NativeContract.ContractManagement.GetContract(system.StoreView, scriptHash)?.Manifest.Name;
                throw new ArgumentException($"Scripthash {scriptHash} {contractName} not registered for debugging. Call SetDebugInfo(scriptHash, nefDbgNfo, dumpNef) first");
            }
            return JObject.Null;
        }
    }
}
