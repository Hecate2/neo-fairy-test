using Neo.SmartContract.Native;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neo.Plugins
{
    public class FairyPlugin : RpcServerPlugin
    {
        public override string Name => "Fairy";
        public override string Description => "Test and debug fairy transactions through RPC";

        class Settings
        {
            public IReadOnlyList<RpcServerSettings> Servers { get; }

            public Settings(IConfigurationSection section)
            {
                Servers = section.GetSection(nameof(Servers)).GetChildren().Select(p => RpcServerSettings.Load(p)).ToArray();
            }
        }

        Settings settings;

        protected override void Configure()
        {
            base.Configure();
            settings = new Settings(GetConfiguration());
        }

        protected override void OnSystemLoaded(NeoSystem system)
        {
            RpcServerSettings s = settings.Servers.FirstOrDefault(p => p.Network == system.Settings.Network);

            Fairy fairy = new(system, s);
            fairy.StartRpcServer();
        }
    }
}
