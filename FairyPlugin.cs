using Microsoft.Extensions.Configuration;
using Neo.ConsoleService;
using Neo.SmartContract.Native;
using System.Runtime.Serialization.Formatters;

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
        readonly List<Fairy> fairyServers = new();

        protected override void Configure()
        {
            base.Configure();
            settings = new Settings(GetConfiguration());
        }

        protected override void OnSystemLoaded(NeoSystem system)
        {
            this.system = system;
            bool hasServer = false;
            foreach (RpcServerSettings s in settings.Servers)
            {
                if (s.Network == system.Settings.Network)
                {
                    hasServer = true;
                    Fairy fairy = new(system, s);
                    fairy.StartRpcServer();
                    fairy.StartWebsocketServer();
                    fairyServers.Add(fairy);
                }
            }
            if (hasServer == false)
            {
                ConsoleHelper.Warning("No valid server from config. Using default!");
                RpcServerSettings s = RpcServerSettings.Default;
                Fairy fairy = new(system, s);
                fairy.StartRpcServer();
                fairy.StartWebsocketServer();
                fairyServers.Add(fairy);
            }
        }

        [ConsoleCommand("fairy", Category = "Fairy Commands", Description = "List Fairy snapshots")]
        private void OnFairyCommand()
        {
            foreach (Fairy fairy in fairyServers)
            {
                Console.WriteLine($">>> Fairy@{fairy.settings.BindAddress}:{fairy.settings.Port}");
                ConsoleHelper.Info("Test snapshots:");
                if (fairy.sessionStringToFairySession.Keys.Count > 0)
                {
                    Console.WriteLine("session name:\t\t\t");
                    foreach (string k in fairy.sessionStringToFairySession.Keys)
                    {
                        ConsoleHelper.Info($"{k}\t\t\t{fairy.sessionStringToFairySession[k].ToString()}");
                    }
                }
                else
                {
                    ConsoleHelper.Warning($"No test snapshot created!");
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
                        foreach (string s in fairy.sessionStringToFairySession.Keys)
                        {
                            contractName = NativeContract.ContractManagement.GetContract(fairy.sessionStringToFairySession[s].engine.Snapshot, k)?.Manifest.Name;
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
}
