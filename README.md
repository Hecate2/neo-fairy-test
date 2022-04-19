**The last tool you need for repeatable, lighting fast, fully automatic smart contract testing!**

Let https://github.com/neo-project/neo-modules/blob/master/src/RpcServer/RpcServer.csproj include the file `RpcServer.WithSession.cs`. Compile and run your own RpcServer (with full source codes of neo, neo-vm, etc.) in your local neo-cli node. **DO NOT USE NUGET PACKAGES!** You may need to change some codes in neo or neo-vm making some methods public. 

You can execute transactions and save your snapshots in the memory of neo-cli. Access these snapshots with session strings defined by yourself. You can change the timestamp (the returned value of `Runtime.Time` called by smart contracts) at will. 

Non official client: https://github.com/Hecate2/neo-test-client

