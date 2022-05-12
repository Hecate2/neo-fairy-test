// include this file in neo-modules/src/RpcServer/RpcServer.csproj
// and build your own RpcServer

using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Neo.Plugins
{
    public partial class RpcServer
    {
        public struct SourceFilenameAndLineNum { public string SourceFilename; public uint LineNum;}
        readonly ConcurrentDictionary<UInt160, HashSet<SourceFilenameAndLineNum>> contractScriptHashToSourceLineNums = new();
        readonly ConcurrentDictionary<UInt160, Dictionary<uint, SourceFilenameAndLineNum>> contractScriptHashToInstructionPointerToSourceLineNum = new();
        readonly ConcurrentDictionary<UInt160, HashSet<string>> contractScriptHashToSourceLineFilenames = new();
        readonly ConcurrentDictionary<UInt160, Dictionary<uint, OpCode>> contractScriptHashToInstructionPointerToOpCode = new();
        readonly ConcurrentDictionary<UInt160, JObject> contractScriptHashToNefDbgNfo = new();
        struct DumpNefPatterns
        {
            public Regex opCodeRegex = new Regex(@"^(\d+)\s(.*?)\s?(#\s.*)?$");  // 8039 SYSCALL 62-7D-5B-52 # System.Contract.Call SysCall
            public Regex sourceCodeRegex = new Regex(@"^#\sCode\s(.*\.cs)\sline\s(\d+):\s""(.*)""$");  // # Code NFTLoan.cs line 523: "ExecutionEngine.Assert((bool)Contract.Call(token, "transfer", CallFlags.All, tenant, Runtime.ExecutingScriptHash, neededAmount, tokenId, TRANSACTION_DATA), "NFT payback failed");"
            public Regex methodStartRegex = new Regex(@"^# Method\sStart\s(.*)$");  // # Method Start NFTLoan.NFTLoan.FlashBorrowDivisible
            public Regex methodEndRegex = new Regex(@"^# Method\sEnd\s(.*)$");  // # Method End NFTLoan.NFTLoan.FlashBorrowDivisible
        }
        readonly DumpNefPatterns dumpNefPatterns = new();

        [RpcMethod]
        protected virtual JObject SetDebugInfo(JArray _params)
        {
            string param0 = _params[0].AsString();
            UInt160 scriptHash = UInt160.Parse(param0);
            // nccs YourContractProject.csproj --debug
            // find .nefdbgnfo beside your .nef contract, and open .nefdbgnfo as a zip file
            // give me the content of the only .debug.json file in the zip
            JObject nefDbgNfo = JObject.Parse(_params[1].AsString());
            contractScriptHashToNefDbgNfo[scriptHash] = nefDbgNfo;
            // https://github.com/devhawk/DumpNef
            // dumpnef contract.nef > contract.nef.txt
            // give me the content of that txt file!
            string dumpNef = _params[2].AsString();
            string[] lines = dumpNef.Replace("\r", "").Split("\n", StringSplitOptions.RemoveEmptyEntries);

            HashSet<SourceFilenameAndLineNum> sourceFilenameAndLineNums = new();
            contractScriptHashToSourceLineNums[scriptHash] = sourceFilenameAndLineNums;
            Dictionary<uint, SourceFilenameAndLineNum> InstructionPointerToSourceLineNum = new();
            contractScriptHashToInstructionPointerToSourceLineNum[scriptHash] = InstructionPointerToSourceLineNum;
            Dictionary<uint, OpCode> instructionPointerToOpCode = new();
            contractScriptHashToInstructionPointerToOpCode[scriptHash] = instructionPointerToOpCode;
            HashSet<string> filenames = new();
            contractScriptHashToSourceLineFilenames[scriptHash] = filenames;

            uint lineNum;
            for (lineNum = 0; lineNum < lines.Length; ++lineNum)
            {
                // foreach (var field in typeof(DumpNefPatterns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                //     Console.WriteLine($"{field.Name}: {field.GetValue(dumpNefPatterns)}");
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
                        SourceFilenameAndLineNum sourceFilenameAndLineNum = new SourceFilenameAndLineNum { SourceFilename=filename, LineNum=sourceCodeLineNum };
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
                    continue;
                }
            }
            JObject json = new();
            json[param0] = true;
            return json;
        }

        [RpcMethod]
        protected virtual JObject ListDebugInfo(JArray _params)
        {
            JArray scriptHashes = new JArray();
            foreach (UInt160 s in contractScriptHashToInstructionPointerToSourceLineNum.Keys)
            {
                scriptHashes.Add(s.ToString());
            }
            return scriptHashes;
        }

        [RpcMethod]
        protected virtual JObject ListFilenamesOfContract(JArray _params)
        {
            string scriptHashStr = _params[0].AsString();
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
        protected virtual JObject DeleteDebugInfo(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s.AsString();
                UInt160 scriptHash = UInt160.Parse(str);
                contractScriptHashToInstructionPointerToSourceLineNum.Remove(scriptHash, out _);
                contractScriptHashToNefDbgNfo.Remove(scriptHash, out _);
                contractScriptHashToSourceLineFilenames.Remove(scriptHash, out _);
                contractScriptHashToAssemblyBreakpoints.Remove(scriptHash, out _);
                contractScriptHashToSourceCodeBreakpoints.Remove(scriptHash, out _);
                json[str] = true;
            }
            return json;
        }
    }
}
