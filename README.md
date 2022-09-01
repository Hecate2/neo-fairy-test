**The last tool you need for repeatable, lighting fast, fully automatic smart contract testing on Neo N3 blockchain!**

`Fairy.csproj` builds an extended RpcServer plugin in your local neo-cli node. Through RPC calls, You can execute your own **fairy transactions** or deploy your own **fairy contracts**, with access to the environment of all other on-chain contracts, but without having to write your transactions onto the chain. The executions of your fairy transactions are saved in your snapshots in the memory of neo-cli. Access these snapshots with session strings defined by yourself. You can change the timestamp (the returned value of `Runtime.Time` called by smart contracts) and the random number generated for smart contracts at will. 

No GAS fee is needed for your fairy transactions. This is a great help when your contract heavily manipulates GAS and you want to ensure correct GAS transferring. If you do need to compute the GAS system fee and network fee, just read `["networkfee"]` and `["gasconsumed"]` in the result of invoked fairy transactions. Network fee is calculated only when the correct wallet is opened and the transaction can be validly signed by the opened wallet.  

Fairy can also debug your contracts. [DumpNef](https://github.com/devhawk/DumpNef) is needed for debugging. Build snapshots with testing APIs, and start debugging by setting debug info, setting breakpoints and invoking function with the debugging API. 

Detailed traceback is returned in result`["traceback"]` if there is any fault. Sample:

```
"ASSERT is executed with false result."
CallingScriptHash=0x5c1068339fae89eb1a743909d0213e1d99dc5dc9
CurrentScriptHash=0x5c1068339fae89eb1a743909d0213e1d99dc5dc9
EntryScriptHash=0xbfe1ac44ec31bdac17591588c0bbba0c63b7be99
   at Neo.VM.ExecutionEngine.ExecuteInstruction() in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\neo-vm\ExecutionEngine.cs:line 374
   at Neo.VM.ExecutionEngine.ExecuteNext() in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\neo-vm\ExecutionEngine.cs:line 1436
InstructionPointer=2281, OpCode ASSERT, Script Length=8518
InstructionPointer=4458, OpCode JMP, Script Length=8518
InstructionPointer=502, OpCode STLOC2, Script Length=806
InstructionPointer=21375, OpCode RET, Script Length=21375
-------Logs-------(1)
[0x5c1068339fae89eb1a743909d0213e1d99dc5dc9] AnyUpdateShortSafe: Transfer failed
```

```
"ASSERT is executed with false result."
CallingScriptHash=0x5c1068339fae89eb1a743909d0213e1d99dc5dc9
CurrentScriptHash=0x5c1068339fae89eb1a743909d0213e1d99dc5dc9
EntryScriptHash=0x4250ef2854561ee4ecd3567135cc2da65910938b
   at Neo.VM.ExecutionEngine.ExecuteInstruction() in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\neo-vm\ExecutionEngine.cs:line 374
   at Neo.VM.ExecutionEngine.ExecuteNext() in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\neo-vm\ExecutionEngine.cs:line 1436
InstructionPointer=2281, OpCode ASSERT, Script Length=8518
InstructionPointer=5699, OpCode LDLOC5, Script Length=8518
InstructionPointer=5365, OpCode JMP, Script Length=8518
InstructionPointer=502, OpCode STLOC2, Script Length=806
InstructionPointer=21390, OpCode RET, Script Length=21390
-------Logs-------(1)
[0x5c1068339fae89eb1a743909d0213e1d99dc5dc9] AnyUpdateShortSafe: No enough token to lend
```

The traceback of the debugger is even more detailed, with your DebugInfo registered:

```
Called Contract Does Not Exist: 0x2a6cd301cad359fc85e42454217e51485fffe745
CallingScriptHash=0x389193b1789bdecb40e817d74d90c1e124c6dbab
CurrentScriptHash=0xa59de097d11bc7130e51c5dcd7aa8e11fd9d4304
EntryScriptHash=0x389193b1789bdecb40e817d74d90c1e124c6dbab
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Span`1& arguments, Signature sig, Boolean constructor, Boolean wrapExceptions)
   at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
   at System.Reflection.MethodBase.Invoke(Object obj, Object[] parameters)
   at Neo.SmartContract.ApplicationEngine.OnSysCall(InteropDescriptor descriptor) in C:\Users\RhantolkYtriHistoria\NEO\neo\src\neo\SmartContract\ApplicationEngine.cs:line 522
   at Neo.SmartContract.ApplicationEngine.OnSysCall(UInt32 method) in C:\Users\RhantolkYtriHistoria\NEO\neo\src\neo\SmartContract\ApplicationEngine.cs:line 506
   at Neo.VM.ExecutionEngine.ExecuteInstruction(Instruction instruction) in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\Neo.VM\ExecutionEngine.cs:line 439
   at Neo.VM.ExecutionEngine.ExecuteNext() in C:\Users\RhantolkYtriHistoria\NEO\neo-vm\src\Neo.VM\ExecutionEngine.cs:line 1454
File NFTLoan.cs, line 247: ExecutionEngine.Assert((bool)Contract.Call(externalTokenContract, "transfer", CallFlags.All, renter, Runtime.ExecutingScriptHash, amountForRent, externalTokenId, TRANSACTION_DATA), "Transfer failed");
	ScriptHash=0xa59de097d11bc7130e51c5dcd7aa8e11fd9d4304, InstructionPointer=4448, OpCode SYSCALL, Script Length=8518
	ScriptHash=0x389193b1789bdecb40e817d74d90c1e124c6dbab, InstructionPointer=96, OpCode , Script Length=96
```

`Fairy.Debugger` relies on `Fairy.Tester`. `Fairy.Coverage` relies on `Fairy.Debugger`.

Instructions for running neo-cli and RpcServer from full source codes for debugging: https://github.com/Hecate2/how-to-debug-neo

#### Building

Consider cloning [neo-modules](https://github.com/neo-project/neo-modules) and building such a directory: `neo-modules/src/Fairy` and place everything in my repo into your `neo-modules/src/Fairy`. Build `Fairy.csproj`.

Alternatively you can use `Fairy.sln` to build your own solution.

You probably have to change the directories of dependencies in `Fairy.csproj`

#### Usage

1. Place Fairy as a plugin of neo-cli (`neo-cli/bin/Debug/net6.0/Plugins/Fairy/{Fairy.dll + config.json}`). **Happy testing on the mainnet**!  Since you can run all the fairy transactions virtually, it is recommended to use the mainnet in order to have access to the real environment for production. 
2. Please read the source codes for help about APIs. I have not written any docs.
3. Non official client: https://github.com/Hecate2/neo-test-client . Watch the fully automatic and repeatable test cases in the repository if you do not know how to use Fairy. 
4. Create new empty wallets for testing. Fairy is derived from RpcServer which can really relay transactions that affects your wallet. To prevent mis-operation, it is not recommended to use wallets with any value of asset. You can virtually set your asset balance with Fairy. 
5. Through RPC, `SetGasBalance(session, account, balance)` to help yourself get 100_0000_0000 (or any amount of) GAS. ([Fairy.Utils.cs](Fairy.Utils.cs)). Use any string as your session name. If the session name is not recognized by Fairy, a new snapshot from current blockchain state will be generated for your session. **Keep using the same session name for continuous executions.** 
6. `VirtualDeploy` your contract. ([Fairy.Utils.cs](Fairy.Utils.cs))
7. `InvokeFunctionWithSession` ([Fairy.Tester.cs](Fairy.Tester.cs))
8. Set timestamp and runtime random number obtained by smart contracts at will! ([Fairy.Engine.cs](Fairy.Engine.cs))
9. If you want to debug a call, you shall first use [DumpNef](https://github.com/devhawk/DumpNef) to let you and Fairy understand the mapping from the assembly instructions to the source codes. Make sure that an `nefdbgnfo` file is generated by your smart contract compiler (`nccs YourCSharpSourceCodeProjectDir --debug` for C# compiler). Then dumpnef and make sure that your dumped text file includes source codes (`dumpnef YourContract.nef > YourContract.nef.txt`). Now switch your fairy client, `SetDebugInfo`([Fairy.Debugger.DebugInfo.cs](Fairy.Debugger.DebugInfo.cs)) and `SetSourceCodeBreakpoints`([Fairy.Debugger.Breakpoint.cs](Fairy.Debugger.Breakpoint.cs)). Then run your debug session with `DebugFunctionWithSession(session, ...)`([Fairy.Debugger.cs](Fairy.Debugger.cs)). The runtime environment is inherited from the same session name constructed by `InvokeFunctionWithSession`. 
10. The debugger shall break on breakpoints or exceptions. Use APIs in [Fairy.Debugger.cs](Fairy.Debugger.cs) for happy debugging!
11. Try the command `fairy` in `neo-cli`, which lists all of your snapshots and DebugInfo! Just type `fairy` in the black cli window (which prints `Fairy server running at X.X.X.X:XXXX` when initialized) and press `ENTER`. 


