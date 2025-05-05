// Copyright (C) 2015-2025 The Neo Project.
//
// Fairy.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.ConsoleService;
using Neo.Json;
using Neo.Plugins.RpcServer;
using Neo.SmartContract.Native;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer.RpcServer
    {
        public class FairyRpcMethodAttribute : RpcMethodAttribute { }
        public NeoSystem system;
        public RpcServerSettings settings;
        public FairyPlugin? fairyPlugin;

        public Fairy(NeoSystem system, RpcServerSettings settings, FairyPlugin? fairyPlugin = null) : base(system, settings)
        {
            this.system = system;
            this.settings = settings;
            this.fairyPlugin = fairyPlugin;
            RegisterWebSocketNeoGoCompatible();
            RegisterBlockchainEvents();
            RegisterWebsocketMethods(this);
            ConsoleHelper.Info($"★ Fairy server running at {settings.BindAddress}:{settings.Port}.\nBy default, Fairy plugin should not be exposed to the public.");
            InitializeTimer();
            if (settings.SessionEnabled && settings.SessionExpirationTime.TotalMilliseconds > 0)
                ConsoleHelper.Info($"Unused sessions in {settings.SessionExpirationTime.Days}d {settings.SessionExpirationTime.Hours}h:{settings.SessionExpirationTime.Minutes}m:{settings.SessionExpirationTime.Seconds}.{settings.SessionExpirationTime.Milliseconds}s will be cleared by Fairy.");
            FairyWallet defaultWallet = new FairyWallet(system.Settings);
            defaultFairyWallet = defaultWallet;
            ConsoleHelper.Info($"\ndefaultFairyWallet:\n{defaultWallet.account.ScriptHash}\n{defaultWallet.account.Address}\n{defaultWallet.account.key.PublicKey}\n");
        }

        [FairyRpcMethod]
        protected virtual JObject HelloFairy(JArray _params)
        {
            JObject result = new()
            {
                ["syncuntilblock"] = fairyPlugin?.SyncUntilBlock,
                ["currentindex"] = GetBlockHeaderCount(),
            };
            return result;
        }

        protected JToken GetBlockHeaderCount()
        {
            return (system.HeaderCache.Last?.Index ?? NativeContract.Ledger.CurrentIndex(system.StoreView)) + 1;
        }
    }
}
