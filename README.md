**The last tool you need for repeatable, lighting fast, fully automatic smart contract testing!**

Let https://github.com/neo-project/neo-modules/blob/master/src/RpcServer/RpcServer.csproj include the file `RpcServer.WithSession.cs`. Compile and run your own RpcServer (with full source codes of neo, neo-vm, etc.) in your local neo-cli node. **DO NOT USE NUGET PACKAGES!** You may need to change some codes in neo or neo-vm making some methods public. 

You can execute your own **fairy transactions**, with access to the environment of all other on-chain contracts, but without having to write your transactions onto the chain. The executions of your fairy transactions are saved in your snapshots in the memory of neo-cli. Access these snapshots with session strings defined by yourself. You can change the timestamp (the returned value of `Runtime.Time` called by smart contracts) at will. Happy testing on the mainnet!  

No GAS fee is needed for your fairy transactions. This is a great help when your contract heavily manipulates GAS and you want to ensure correct GAS transferring. If you do need to compute the GAS system fee and network fee, just read `["networkfee"]` and `["gasconsumed"]` in the result of invoked fairy transactions. Network fee is calculated only when the correct wallet is opened and the transaction can be validly signed by the opened wallet.  

Non official client: https://github.com/Hecate2/neo-test-client


