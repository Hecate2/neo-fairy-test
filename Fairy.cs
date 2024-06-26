using Neo.ConsoleService;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        public NeoSystem system;
        public RpcServerSettings settings;

        public Fairy(NeoSystem system, RpcServerSettings settings) : base(system, settings)
        {
            this.system = system;
            this.settings = settings;
            RegisterWebSocketNeoGoCompatible();
            RegisterBlockchainEvents();
            RegisterWebsocketMethods(this);
            ConsoleHelper.Info($"★ Fairy server running at {settings.BindAddress}:{settings.Port}.\nBy default, Fairy plugin should not be exposed to the public.");
            InitializeTimer();
            if (settings.SessionEnabled && settings.SessionExpirationTime.TotalMilliseconds > 0)
                ConsoleHelper.Info($"Unused sessions in {settings.SessionExpirationTime.Days}d {settings.SessionExpirationTime.Hours}h:{settings.SessionExpirationTime.Minutes}m:{settings.SessionExpirationTime.Seconds}.{settings.SessionExpirationTime.Milliseconds}s will be cleared by Fairy.");
            FairyWallet defaultWallet = new FairyWallet(system.Settings);
            this.defaultFairyWallet = defaultWallet;
            ConsoleHelper.Info($"\ndefaultFairyWallet:\n{defaultWallet.account.ScriptHash}\n{defaultWallet.account.Address}\n{defaultWallet.account.key.PublicKey}\n");
        }
    }
}
