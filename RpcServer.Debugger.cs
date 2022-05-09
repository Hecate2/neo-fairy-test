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
        Dictionary<UInt160, Dictionary<int, int>> contractScriptHashToSourceLineNumToInstructionPointer = new();
        Dictionary<UInt160, JObject> contractScriptHashToNefDbgNfo = new();
        struct DumpNefPatterns
        {
            public Regex opCodeRegex = new Regex(@"^(\d+)\s(.*?)(#\s.*)?$");  // 8039 SYSCALL 62-7D-5B-52 # System.Contract.Call SysCall
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
            int lineNum;
            for (lineNum = 0; lineNum < lines.Length; ++lineNum)
            {
                // foreach (var field in typeof(DumpNefPatterns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                //     Console.WriteLine($"{field.Name}: {field.GetValue(dumpNefPatterns)}");
                Match match;
                match = dumpNefPatterns.sourceCodeRegex.Match(lines[lineNum]);
                if (match.Success)
                {
                    GroupCollection sourceCodeGroups = match.Groups;
                    int sourceCodeLineNum = int.Parse(sourceCodeGroups[2].ToString());
                    ++lineNum;
                    match = dumpNefPatterns.opCodeRegex.Match(lines[lineNum]);
                    if (match.Success)
                    {
                        GroupCollection opcodeGroups = match.Groups;
                        int instructionPointer = int.Parse(opcodeGroups[1].ToString());
                        Dictionary<int, int> sourceLineNumToInstructionPointer;
                        if (!contractScriptHashToSourceLineNumToInstructionPointer.TryGetValue(scriptHash, out sourceLineNumToInstructionPointer))
                        {
                            sourceLineNumToInstructionPointer = new Dictionary<int, int>();
                            contractScriptHashToSourceLineNumToInstructionPointer[scriptHash] = sourceLineNumToInstructionPointer;
                        }
                        sourceLineNumToInstructionPointer[sourceCodeLineNum] = instructionPointer;
                    }
                    continue;
                }
            }
            JObject json = new();
            json[param0] = true;
            return json;
        }
    }
}
