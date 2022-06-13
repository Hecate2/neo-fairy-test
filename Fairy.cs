using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        NeoSystem system;
        RpcServerSettings settings;

        public Fairy(NeoSystem system, RpcServerSettings settings) : base(system, settings)
        {
            this.system = system;
            this.settings = settings;
            Console.WriteLine($"Fairy server running at {settings.BindAddress}:{settings.Port}.\nBy default, Fairy plugin should not be exposed to the public.");
        }
    }
}
