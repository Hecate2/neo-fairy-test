using Neo.Json;
using Neo.SmartContract.Native;
using Neo.VM;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public struct SourceFilenameAndLineNum
        {
            public string sourceFilename;
            public uint lineNum;
            public string? sourceContent;  // We do not consider sourceContent for equality
            public override readonly bool Equals(object? obj) => obj is SourceFilenameAndLineNum other && this.Equals(other);
            public readonly bool Equals(SourceFilenameAndLineNum p) => sourceFilename == p.sourceFilename && lineNum == p.lineNum;
            public override readonly int GetHashCode() => (sourceFilename, lineNum).GetHashCode();
            public static bool operator ==(SourceFilenameAndLineNum lhs, SourceFilenameAndLineNum rhs) => lhs.Equals(rhs);
            public static bool operator !=(SourceFilenameAndLineNum lhs, SourceFilenameAndLineNum rhs) => !(lhs == rhs);
        }
        public static readonly SourceFilenameAndLineNum defaultSource = new SourceFilenameAndLineNum { sourceFilename = "", lineNum = 0, sourceContent = "" };
        public readonly ConcurrentDictionary<UInt160, HashSet<SourceFilenameAndLineNum>> contractScriptHashToAllSourceLineNums = new();
        // stores a mapping of all instructions to corresponding source code lines. Used only for finding the source code from assembly.
        public readonly ConcurrentDictionary<UInt160, Dictionary<uint, SourceFilenameAndLineNum>> contractScriptHashToAllInstructionPointerToSourceLineNum = new();
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

        [FairyRpcMethod]
        protected virtual JToken SetDebugInfo(JArray _params)
        {
            string param0 = _params[0]!.AsString();
            UInt160 scriptHash = UInt160.Parse(param0);
            // nccs YourContractProject.csproj --debug
            // find .nefdbgnfo beside your .nef contract, and
            // give me the base64encode(content) of .nefdbgnfo file
            JObject nefDbgNfo = (JObject)JObject.Parse(Unzip(Convert.FromBase64String(_params[1]!.AsString())))!;
            contractScriptHashToNefDbgNfo[scriptHash] = nefDbgNfo;
            // https://github.com/devhawk/DumpNef  (Neo < 3.5.*)
            // https://github.com/Hecate2/DumpNef  (Neo >= 3.6)
            // https://github.com/neo-project/neo-devpack-dotnet/blob/b65b43f7d39687549ee22d33c1898c809d281ea9/src/Neo.Compiler.CSharp/Program.cs#L297
            // dumpnef contract.nef > contract.nef.txt
            // give me the content of that txt file!
            string dumpNef = _params[2]!.AsString();
            string[] lines = dumpNef.Replace("\r", "").Split("\n", StringSplitOptions.RemoveEmptyEntries);

            HashSet<SourceFilenameAndLineNum> allSourceFilenameAndLineNums = new();
            contractScriptHashToAllSourceLineNums[scriptHash] = allSourceFilenameAndLineNums;
            Dictionary<uint, SourceFilenameAndLineNum> AllInstructionPointerToSourceLineNum = new();
            contractScriptHashToAllInstructionPointerToSourceLineNum[scriptHash] = AllInstructionPointerToSourceLineNum;
            Dictionary<uint, OpCode> instructionPointerToOpCode = new();
            contractScriptHashToInstructionPointerToOpCode[scriptHash] = instructionPointerToOpCode;
            Dictionary<uint, bool> instructionPointerToCoverage = new();
            contractScriptHashToInstructionPointerToCoverage[scriptHash] = instructionPointerToCoverage;
            HashSet<string> filenames = new();
            contractScriptHashToSourceLineFilenames[scriptHash] = filenames;

            uint lineNum;
            Match sourceCodeMatch, opCodeMatch;
            SourceFilenameAndLineNum sourceFilenameAndLineNum = defaultSource;
            for (lineNum = 0; lineNum < lines.Length; ++lineNum)
            {
                // foreach (var field in typeof(DumpNefPatterns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                //     ConsoleHelper.Info($"{field.Name}: {field.GetValue(dumpNefPatterns)}");
                sourceCodeMatch = dumpNefPatterns.sourceCodeRegex.Match(lines[lineNum]);
                if (sourceCodeMatch.Success)  // Current line is a line of source code
                {
                    GroupCollection sourceCodeGroups = sourceCodeMatch.Groups;
                    string filename = sourceCodeGroups[1].ToString();
                    uint sourceCodeLineNum = uint.Parse(sourceCodeGroups[2].ToString());
                    string sourceContent = sourceCodeGroups[3].ToString();
                    filenames.Add(filename);
                    sourceFilenameAndLineNum = new SourceFilenameAndLineNum { sourceFilename = filename, lineNum = sourceCodeLineNum, sourceContent = sourceContent };
                    continue;
                }
                opCodeMatch = dumpNefPatterns.opCodeRegex.Match(lines[lineNum]);
                if (opCodeMatch.Success)
                {
                    GroupCollection opcodeGroups = opCodeMatch.Groups;
                    uint instructionPointer = uint.Parse(opcodeGroups[1].ToString());
                    AllInstructionPointerToSourceLineNum[instructionPointer] = sourceFilenameAndLineNum;
                    allSourceFilenameAndLineNums.Add(sourceFilenameAndLineNum);
                    string[] opcodeAndOperand = opcodeGroups[2].ToString().Split();
                    instructionPointerToOpCode[instructionPointer] = (OpCode)Enum.Parse(typeof(OpCode), opcodeAndOperand[0]);
                    instructionPointerToCoverage[instructionPointer] = false;
                    continue;
                }
            }
            JObject json = new();
            json[param0] = true;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken ListDebugInfo(JArray _params)
        {
            JArray scriptHashes = new JArray();
            foreach (UInt160 s in contractScriptHashToAllInstructionPointerToSourceLineNum.Keys)
            {
                scriptHashes.Add(s.ToString());
            }
            return scriptHashes;
        }

        [FairyRpcMethod]
        protected virtual JToken ListFilenamesOfContract(JArray _params)
        {
            string scriptHashStr = _params[0]!.AsString();
            UInt160 scriptHash = UInt160.Parse(scriptHashStr);
            List<string> filenameList = contractScriptHashToSourceLineFilenames[scriptHash].ToList();
            filenameList.Sort();
            JArray filenames = [.. filenameList];
            return filenames;
        }

        [FairyRpcMethod]
        protected virtual JToken DeleteDebugInfo(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s!.AsString();
                UInt160 scriptHash = UInt160.Parse(str);
                contractScriptHashToAllSourceLineNums.Remove(scriptHash, out _);
                contractScriptHashToAllInstructionPointerToSourceLineNum.Remove(scriptHash, out _);
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

        [FairyRpcMethod]
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
