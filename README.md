**The last tool you need for repeatable, lightning fast, remote, fully automatic smart contract testing & debugging on Neo N3 blockchain, in all networks & all programming languages!**

`Fairy.csproj` builds an extended RpcServer plugin in your local neo-cli node, **to "fork" the N3 network**. Through RPC calls, You can execute your own [**fairy transactions**](https://github.com/Hecate2/neo-fairy-test/blob/23f3c67563bc8c84b6aba70ea713cc488bba6265/Fairy.Tester.cs#L24) or deploy your own [**fairy contracts**](https://github.com/Hecate2/neo-fairy-test/blob/23f3c67563bc8c84b6aba70ea713cc488bba6265/Fairy.Utils.cs#L20C26-L20C26), with access to the environment of all other on-chain contracts, but without having to write your transactions onto the real chain. The executions of your fairy transactions are saved in your [snapshots in the memory of neo-cli](https://github.com/Hecate2/neo-fairy-test/blob/23f3c67563bc8c84b6aba70ea713cc488bba6265/Fairy.Tester.Snapshot.cs#L9). Access these snapshots with session strings defined by yourself. You can [change the timestamp](https://github.com/Hecate2/neo-fairy-test/blob/23f3c67563bc8c84b6aba70ea713cc488bba6265/Fairy.Engine.cs#L145) (the returned value of `Runtime.Time` called by smart contracts) and the [random number](https://github.com/Hecate2/neo-fairy-test/blob/23f3c67563bc8c84b6aba70ea713cc488bba6265/Fairy.Engine.cs#L177C26-L177C26) generated for smart contracts at will, by setting the timestamp or the random number of a snapshot, through RPC. 

**No GAS fee** is needed for your fairy transactions. This is a great help when your contract heavily manipulates GAS and you want to ensure correct GAS transferring. If you do need to compute the GAS system fee and network fee, just read `["networkfee"]` and `["gasconsumed"]` in the result of invoked fairy transactions. Network fee is calculated only when the correct wallet is opened and the transaction can be validly signed by the opened wallet. 

**It's recommended to directly install Fairy in the mainnet neo-cli** (yet it's also good to use the testnet or any private net). **Do not worry about GAS fees. Fairy can set your fairy balance** for GAS and NEO, and any other token if you know the format of storage key and value.

Fairy can also **debug** your contracts with **breakpoints, single-step execution and variable value inspectors at runtime**. [DumpNef](https://github.com/Hecate2/DumpNef) is needed for debugging, if your contract is not in C#.

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

Neo <= 3.7.4:

Consider cloning [neo-modules](https://github.com/neo-project/neo-modules) and building such a directory: `neo-modules/src/Fairy` and place everything in my repo into your `neo-modules/src/Fairy`. Build `Fairy.csproj`.

Alternatively you can use `Fairy.sln` to build your own solution.

You probably have to change the directories of dependencies in `Fairy.csproj`

Neo > 3.7.4:

neo-modules have been archived and migrated into neo repository. Just build from `neo/src/Plugins`.

#### Usage

1. Place Fairy as a plugin of neo-cli (`neo-cli/bin/Debug/net8.0/Plugins/Fairy/{Fairy.dll + config.json + fairy.json + RpcServer.json}`). You may also need to place an [RpcServer.dll](https://github.com/neo-project/neo-modules/releases) at `neo-cli/bin/Debug/net8.0`. **Happy testing on the mainnet**!  Since you can run all the fairy transactions virtually, **it is recommended to use the mainnet** in order to have access to the real environment for production. 
2. (Optional) Create `pause.txt` and fill an `uint` beside `Fairy.dll`, to pause block synchronization for indexes greater than the number you filled. If you do not fill anything in `pause.txt`, synchronization stops for index > 0. If there is no `pause.txt` file at all, synchronization always continues. Note that the genesis block has index 0, and is always synchronized.
3. Please read the source codes for help about APIs. I have not written any docs.
4. Highly recommended **non-official client**: https://github.com/Hecate2/neo-fairy-client . Watch the fully automatic and repeatable test cases in the repository if you do not know how to use Fairy. 
5. Use the default fairy wallet `fairy.json` in the releases, or create new empty wallets for testing. Fairy is derived from RpcServer which can really relay transactions that affects your wallet. **To prevent mis-operation, it is not recommended to use wallets with any value of asset. You can virtually set your asset balance with Fairy.** 
6. Through RPC, `SetGasBalance(session, account, balance)` to help yourself get 100_0000_0000 (or any amount of) GAS. ([Fairy.Utils.cs](Fairy.Utils.cs)). **Use any string as your session name. If the session name is not recognized by Fairy, a new snapshot from current blockchain state will be generated for your session. Keep using the same session name for continuous executions.** 
7. `VirtualDeploy` your contract. ([Fairy.Utils.cs](Fairy.Utils.cs))
8. Test your contract with `InvokeFunctionWithSession` ([Fairy.Tester.cs](Fairy.Tester.cs)). Using the same session name, your test calls keep writing storage to the snapshot marked by the session. 
9. Set timestamp (`SetSnapshotTimestamp`) and runtime random number (`SetSnapshotRandom`) obtained by smart contracts at will! ([Fairy.Engine.cs](Fairy.Engine.cs))
10. Copy or rename your snapshots when you want to test different calls starting from a same state. Note that timestamp and random number settings are not inherited when a snapshot is copied.
11. If you want to debug a call, you shall either compile your contract with `--assembly` flag (only available in the CSharp compiler `nccs` for now; `nccs YourCSharpSourceCodeProjectDir --debug --assembly`), or use a standalone tool [DumpNef](https://github.com/devhawk/DumpNef) (that works with any source code language) to let you and Fairy understand the mapping from the assembly instructions to the source codes. Make sure that an `nefdbgnfo` file is generated by your smart contract compiler (`nccs YourCSharpSourceCodeProjectDir --debug` for C# compiler). Then dumpnef and make sure that your dumped text file includes source codes (`dumpnef YourContract.nef > YourContract.nef.txt`). Now with your fairy client, `SetDebugInfo`([Fairy.Debugger.DebugInfo.cs](Fairy.Debugger.DebugInfo.cs))(contractScriptHash, base64(.nefdbgnfoFile), YourContract.nef.txt) and `SetSourceCodeBreakpoints`([Fairy.Debugger.Breakpoint.cs](Fairy.Debugger.Breakpoint.cs)). **You can refer to the dumpnef txt file to know which source code lines have assembly codes.** Then run your debug session with `DebugFunctionWithSession(session, ...)`([Fairy.Debugger.cs](Fairy.Debugger.cs)). **The runtime environment in debugging is inherited from the test snapshot of the same session name**, constructed by `InvokeFunctionWithSession`, **but the debugger never writes storage into the test snapshot**. This means that debug calls do not affect test calls. Even if you run debugging from the same test snapshot twice, the initial storage environment for debugging remains the same.
12. The debugger shall break on breakpoints or exceptions. Use `DebugStepOver`, `DebugStepIn`, `DebugStepOut` APIs in [Fairy.Debugger.cs](Fairy.Debugger.cs) for happy debugging! Use `GetVariableValueByName` to find the value of a variable directly by its name in your contract source code. **Conditional breakpoints and expression evaluation are not supported**, because we do not have a nice compiler that can be bundled with Fairy for all smart contract programming languages. **Yet you can script with neo-fairy-client (or any favored http client) for such purposes.**
13. Try the command `fairy` in `neo-cli`, which lists all of your snapshots and DebugInfo! Just type `fairy` in the black cli window (which prints `Fairy server running at X.X.X.X:XXXX` when initialized) and press `ENTER`.  Also try the command `sync` to continue block synchronization, or `sync 0` to pause synchronization for any block index > 0.
14. **You can relay transactions generated by Fairy to the real blockchain.** Signature is performed by the wallet opened by https://github.com/Hecate2/neo-fairy-test/blob/master/Fairy.Wallet.cs .  Multisig is not supported for now. Note that you should set the signer to be the address of the opened wallet when you `InvokeFunctionWithSession`. If the signer in your invocation does not match the wallet account, the transaction will not take effect on the real blockchain.
15. Use [`AwaitConfirmedTransaction`](https://github.com/Hecate2/neo-fairy-test/blob/6dafe38e7aa0852cfd5ce67d4c7dded6cd923017/Fairy.Utils.cs#L310) to wait for a relayed transaction on the real blockchain. This method return when the awaited transaction is detected in a new block.

#### Playing with oracles?

[Do what is needed to trigger the oracle](https://github.com/Hecate2/neo-fairy-client/blob/master/test_oracle.py) (with a fairy session). If the oracle is activated, there will be info in `['result']['oraclerequests']` in the returned content of `invokeFuntionWithSession`. **Then call `OracleFinish` to perform a simulated oracle callback, with any oracle response you want, pretending that you are the oracle contract**. The methodology is to use signer == OracleContract `0xfe924b7cfe89ddd271abaf7210a80a7e11178758` (You do not have to do it by youself). Fairy accepts any signer even without signature! 

And, for convenient json path simulation, `OracleJsonPath` method is provided.

#### WebSocket features

Quick example: connect to `ws://localhost:16869` and send

```json
{"jsonrpc":"2.0","method":"subscribecommittedblock","params":[],"needresponse":true,"id":1}
```

to subscribe new blocks from websocket. Fairy will respond immediately (for `"needresponse":true`) with:

```json
{"jsonrpc":"2.0","id":null,"result":"subscribecommittedblock"}
```

and then give you new blocks continuously like this:

```json
{"hash":"0x02291714d9488ff56792acd63b681b15564030ef844639c050c7687fda2d3855","size":1724,"version":0,"previousblockhash":"0x52faaa735cfa72465ea993cc72ffba10e935618ec6a7ab46eee912828d38f7cb","merkleroot":"0x418e133daedc17c9d2f5a2b7100202d7c4d775874f4bc597502aa9b27168caee","time":1676369532387,"nonce":"AD3D9827B0479307","index":2968580,"primary":6,"nextconsensus":"NSiVJYZej4XsxG5CUpdwn7VRQk8iiiDMPM","witnesses":[{"invocation":"DECve4OKIDurU1a8NCfiLQpD\u002B7Y5/dZyjZUJow6KADyc4geKhAW1ERGneyOI9P2xgJdTJXD8/tPfyAbx0Ea9RLi4DED\u002BM5nwFriXHHR1GvQ0wuYN4frRO5RFw3y/OIOqtfUrhDazdxSvTt9lNkOjfm8gfPCqlSMwhTc3imsodkPoy6BPDECeK3mYYwM9DrF5wPrcEIhsPf/CAFjZpL4F2BQ2mwpQdCGLoxDjRAlQ\u002BHG9FVhrMW4KmXItsWylIkJ7xjNZEFYSDEB3qyrb5txOZOWa\u002BHggeIEuHsIlErBtlEZyWuAGIRDGfNUAzq2c7mHpb16Wk2nn0ho9sCFzpcyZj\u002BgYtn65hguaDEBZZsKLrSdUWpvedi1zwECJ87dvnUZhxlzqwIQOZMUoooT5tw6dCTzS3l2Qh\u002BK2UNtArsz6DNbWXQbTKtMQNrXI","verification":"FQwhAjmjdDZlL0GzuALKRMvLfWXTqguIyaA4AkO9vhqqXLNbDCECSG/RVwLESQomcDESpcwdCSP9aXozQGvVocAOABOwmnAMIQKq7DhHD2qtAELG6HfP2Ah9Jnaw9Rb93TYoAbm9OTY5ngwhA7IJ/U9TpxcOpERODLCmu2pTwr0BaSaYnPhfmw\u002B6F6cMDCEDuNnVdx2PUTqghpucyNUJhkA7eMbaNokGOMPUalrc4EoMIQLKDidpe5wkj28W4IX9AGHib0TahbWO6DXBEMql7DulVAwhA9nosWvZsi0zRdbUzeMb4cPh0WFTLj0MzsuV7OLrWDNuF0Ge0Nw6"}],"tx":[{"hash":"0x143fdd313120239348eb20e0046dc0cfe66f648de9d6db72e9a16805c0c74239","size":252,"version":0,"nonce":3231413040,"sender":"NdqZnTe6TxbM1SFtWUkLTRBvwJCB8T41Dr","sysfee":"997775","netfee":"123552","validuntilblock":2974339,"signers":[{"account":"0x0cf876ffa858472948b0d93ad36e9a93c7109dc4","scopes":"CalledByEntry"}],"attributes":[],"script":"CwPRjxL8BgAAAAwUYdkDMxhbQWB6ZMj\u002B9Gs9pCz19P8MFMSdEMeTmm7TOtmwSClHWKj/dvgMFMAfDAh0cmFuc2ZlcgwUz3bii9AGLEpHjuNVYQETGfPPpNJBYn1bUg==","witnesses":[{"invocation":"DEBky/KzQZCGr7WkhIDYR2DQ/t2gWuX3dsfNjXPmNK1sFWNvOg1vA92d94en4k2ErXePcg81h5cmq4TW3u2/ZaWv","verification":"DCEC\u002B7ReDS15pdZq5gtJzz6uwUzm76//h9WvMfFiTL4ofBhBVuezJw=="}]},{"hash":"0xe161279f84a74c4ee201c9c31a715f342cca62c50c7542c84b3aca69b5c85c61","size":775,"version":0,"nonce":2158543539,"sender":"NaVMHVqpZNprzZKED1trLZYPfeExogEynq","sysfee":"20708703","netfee":"175852","validuntilblock":2973580,"signers":[{"account":"0xf2d522a72cd5e8b8bc20544228ff22225eefe19f","scopes":"CustomContracts","allowedcontracts":["0xd6abe115ecb75e1fa0b42f5e85934ce8c1ae2893","0x1005d400bcc2a56b7352f09e273be3f9933a5fb1","0x799bbfcbc97b5a425e14089aeb06753cb3190560"]}],"attributes":[],"script":"DFhsSExIaDcwV3dHRjNDSlJPNWNESmozam9JQUNPZVB5QzM5Q043S2p1eVhPSWlpQjIwenZReUdHb0s5eXVZZTZqeTJ2MCtyVk8vbTVkbGdGbTEyOEhUZz09DVgBZXlKd2NtbGpaWE1pT2lCN0lrWk1UU0k2SUNJNU5qVXlPRFV3TURBd01EQXdNREF3TURBd0lpd2dJa1ZVU0NJNklDSXhOVEV6TWprM05UQXdNREF3TURBd01EQTJPREl4TWpFaUxDQWlRbFJESWpvZ0lqSXhPREl6T1RJd01EQXdNREF3TURBd01qa3hNRE00TXpBaUxDQWlRazVDSWpvZ0lqSTVNemswTnpjd01EQXdNREF3TURBd01EQXdNREF3SWl3Z0ltSk9SVThpT2lBaU9EUXpOVEU1T1RrNU9UazVPVGs1T1RreU9EazBJaXdnSW1aWFFsUkRJam9nSWpJeE9ESXpPVEl3TURBd01EQXdNREF3TWpreE1ETTRNekFpZlN3Z0ltUmxZMmx0WVd4eklqb2dNakFzSUNKbGVIQnBjbVZ6SWpvZ01UWTNOak0yT1RZek9IMD0D7a0G0wUAAAAMFJ/h714iIv8oQlQgvLjo1SynItXyDBSxXzqT\u002BeM7J57wUnNrpcK8ANQFEAwUkyiuwehMk4VeL7SgH1637BXhq9YWwB8MCm1pbnRGVG9rZW4MFGAFGbM8dQbrmggUXkJae8nLv5t5QWJ9W1I=","witnesses":[{"invocation":"DEAZEf1ICIBEBIhi6K6xpH4hr/YL5KktJEBDCTsUUz1uBlqmpOnUZpvpf1jCUhhHVvtCNEnXCVP9zH1mO07tLQKM","verification":"DCECMa81pMlXWl91r45PfoysYzQf0jlm0MwpwfLuifwSHkZBVuezJw=="}]}]}
```

#### About signature simulation

Here we do have some conflicting demands:

1. Playing without real private keys
2. Let the signatures (witnesses) be valid in the execution of smart contracts
3. Calculate the network fee of the transaction

**Now we are going to explain how these demands are met:**

For each invocation of function or script, you can assign the signers of this fairy transaction (let's call it T). Fairy builds T such that all the senders assigned by you are filled in the transaction. Then, T (**with signers & WitnessScopes claimed, but without actual signature**) is executed by FairyEngine and committed to the Fairy snapshot, **without verifying the signature**. During the execution in FairyEngine, all the witness checks can be passed.

And how to calculate the network fee? `Fairy.Wallet.cs` offers a default `FairyWallet`. Its `GetAccount` method **always returns the first account in the wallet if the given scripthash is not found**. In this way, FairyWallet signs the transaction using its own account, instead of the signers assigned by you. 

**Remember not to relay such transactions to the real blockchain!** You can still use the `OpenWallet` and `InvokeFunction` methods of `RpcServer` to build real transactions and actually relay them. 

Sometimes you may still need to simulate the actual single- or multi-sig for a transaction, and in such cases you are willing to **give out the actual private keys**. I have let Fairy support single-signature in `ForceSignMessage` and `ForceSignTransaction`. Multi-account is supported by FairyWallet, but for now we are not able to sign with multiple accounts.

