**The last tool you need for repeatable, lighting fast, fully automatic smart contract testing!**

`Fairy.csproj` builds an extended RpcServer plugin in your local neo-cli node. Through RPC calls, You can execute your own **fairy transactions** or deploy your own **fairy contracts**, with access to the environment of all other on-chain contracts, but without having to write your transactions onto the chain. The executions of your fairy transactions are saved in your snapshots in the memory of neo-cli. Access these snapshots with session strings defined by yourself. You can change the timestamp (the returned value of `Runtime.Time` called by smart contracts) at will. Happy testing on the mainnet!  

No GAS fee is needed for your fairy transactions. This is a great help when your contract heavily manipulates GAS and you want to ensure correct GAS transferring. If you do need to compute the GAS system fee and network fee, just read `["networkfee"]` and `["gasconsumed"]` in the result of invoked fairy transactions. Network fee is calculated only when the correct wallet is opened and the transaction can be validly signed by the opened wallet.  

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

`Fairy.Debugger` relies on `Fairy.Tester`. `Fairy.Coverage` relies on `Fairy.Debugger`.

Try the command `fairy` in `neo-cli`!

Non official client: https://github.com/Hecate2/neo-test-client

Instructions for running neo-cli and RpcServer from full source codes for debugging: https://github.com/Hecate2/how-to-debug-neo

#### Building

Consider cloning [neo-modules](https://github.com/neo-project/neo-modules) and building such a directory: `neo-modules/src/Fairy` and place everything in my repo into your `neo-modules/src/Fairy`. Build `Fairy.csproj`.

Alternatively you can use `Fairy.sln` to build your own solution.

You probably have to change the directories of dependencies in `Fairy.csproj`