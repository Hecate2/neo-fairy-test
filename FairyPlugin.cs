using Neo.ConsoleService;
using Neo.SmartContract.Native;
using Microsoft.Extensions.Configuration;

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
        NeoSystem system;
        Fairy fairy;

        protected override void Configure()
        {
            base.Configure();
            settings = new Settings(GetConfiguration());
        }

        protected override void OnSystemLoaded(NeoSystem system)
        {
            RpcServerSettings s = settings.Servers.FirstOrDefault(p => p.Network == system.Settings.Network);
            this.system = system;
            fairy = new(system, s);
            fairy.StartRpcServer();
        }

        [ConsoleCommand("fairy", Category = "Fairy Commands", Description = "List Fairy snapshots")]
        private void OnFairyCommand()
        {
            Fairy.RuntimeArgs runtimeArgs;
            ConsoleHelper.Info("Test snapshots:");
            if (fairy.sessionToEngine.Keys.Count > 0)
            {
                Console.WriteLine("session\t\t\tRuntimeArgs");
                foreach (string k in fairy.sessionToEngine.Keys)
                {
                    if (!fairy.sessionToRuntimeArgs.TryGetValue(k, out runtimeArgs))
                        runtimeArgs = new Fairy.RuntimeArgs();
                    ConsoleHelper.Info($"{k}\t\t\t{runtimeArgs.ToString()}");
                }
            }
            else
            {
                ConsoleHelper.Warning($"No test snapshot created!");
            }
            Console.WriteLine("------");

            ConsoleHelper.Info("Debug snapshots:");
            if (fairy.debugSessionToEngine.Keys.Count > 0)
            {
                Console.WriteLine("session\t\t\tRuntimeArgs");
                foreach (string k in fairy.debugSessionToEngine.Keys)
                {
                    if (!fairy.sessionToRuntimeArgs.TryGetValue(k, out runtimeArgs))
                        runtimeArgs = new Fairy.RuntimeArgs();
                    ConsoleHelper.Info($"{k}\t\t\t{runtimeArgs.ToString()}");
                }
            }
            else
            {
                ConsoleHelper.Warning($"No debug snapshot created!");
            }
            Console.WriteLine("------");

            ConsoleHelper.Info("DebugInfo registration:");
            if (fairy.contractScriptHashToSourceLineFilenames.Keys.Count > 0)
            {
                Console.Error.WriteLine($"test snapshot\t\t\tcontract name\t\t\tscript hash");
                foreach (UInt160 k in fairy.contractScriptHashToSourceLineFilenames.Keys)
                {
                    string? contractName = null;
                    string? testSession = null;
                    foreach (string s in fairy.sessionToEngine.Keys)
                    {
                        contractName = NativeContract.ContractManagement.GetContract(fairy.sessionToEngine[s].Snapshot, k)?.Manifest.Name;
                        if (contractName != null)
                        {
                            testSession = s;
                            break;
                        }
                    }
                    if (contractName == null)
                        contractName = "Unknown Contract";
                    ConsoleHelper.Info($"{testSession}\t\t\t\t{contractName}\t\t\t{k}");
                    ConsoleHelper.Info(String.Join(", ", fairy.contractScriptHashToSourceLineFilenames[k]));
                    Console.WriteLine("---");
                }
            }
            else
            {
                ConsoleHelper.Warning($"No DebugInfo registration!");
            }
        }
    }
}
