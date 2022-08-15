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
            ConsoleHelper.Info($"Fairy server running at {settings.BindAddress}:{settings.Port}.\nBy default, Fairy plugin should not be exposed to the public.");
        }
    }
}
