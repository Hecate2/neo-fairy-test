using Microsoft.Extensions.Configuration;
using Neo.ConsoleService;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Native;

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

        public uint SyncUntilBlock { get; internal set; } = uint.MaxValue;
        public readonly TimeSpan SleepTimeMs = TimeSpan.FromMilliseconds(3600_000);
        public CancellationTokenSource? CancelSyncSleep;

        Settings settings;
        NeoSystem system;
        readonly List<Fairy> fairyServers = new();

        public FairyPlugin() : base()
        {
            Blockchain.Committing += SyncControl;
        }

        protected override void Configure()
        {
            base.Configure();
            settings = new Settings(GetConfiguration());
        }

        protected RpcServerSettings CreateDefaultFairyServerSettings(string ipAddress, uint? network = null)
        {
            network ??= system.Settings.Network!;
            RpcServerSettings s = RpcServerSettings.Default with
            {
                Network = (uint)network,
                BindAddress = System.Net.IPAddress.Parse(ipAddress),
                Port = 16868,
                SslCert = "",
                SslCertPassword = "",
                TrustedAuthorities = [],
                RpcUser = "",
                RpcPass = "",
                MaxGasInvoke = 200,
                MaxFee = 1_000_0000,
                MaxConcurrentConnections = 40,
                MaxIteratorResultItems = 100,
                DisabledMethods = [],
                SessionEnabled = true,
                SessionExpirationTime = TimeSpan.FromSeconds(86400),
            };
            return s;
        }

        protected Fairy? TryStartFairyServer(RpcServerSettings s)
        {
            if (s.Network != system.Settings.Network)
            {
                ConsoleHelper.Warning($"Invalid server {nameof(Network)} from `{nameof(RpcServer)}.json` config. Expected {system.Settings.Network} from Neo.CLI, got {s.Network} from RpcServer.json");
                return null;
            }
            Fairy fairy = new(system, s, this);
            try
            {
                fairy.StartRpcServer();
                fairy.StartWebsocketServer();
            }
            catch (Exception e)
            {
                ConsoleHelper.Warning($"Failed to start Fairy server {s.BindAddress}:{s.Port}. Check whether your IP address and port are available.");
                ConsoleHelper.Error(e.ToString());
                return null;
            }
            fairyServers.Add(fairy);
            return fairy;
        }

        protected override void OnSystemLoaded(NeoSystem system)
        {
            this.system = system;
            bool hasServer = false;
            if (settings != null)
                foreach (RpcServerSettings s in settings.Servers)
                    if (TryStartFairyServer(s) != null)
                        hasServer = true;
            if (hasServer == false)
            {
                string serverCount = settings is null ? "null" : settings.Servers.Count.ToString();
                ConsoleHelper.Warning($"Got {serverCount} servers from config, with no valid server. Using default!");
                foreach (RpcServerSettings s in new RpcServerSettings[] { CreateDefaultFairyServerSettings("0.0.0.0"), CreateDefaultFairyServerSettings("::") })
                    TryStartFairyServer(s);
            }

            string pauseFileName = "pause.txt";
            string pauseFileFullPath = System.IO.Path.Combine(RootPath, pauseFileName);
            if (File.Exists(pauseFileFullPath))
            {
                StreamReader sr = new StreamReader(pauseFileFullPath);
                string? line = sr.ReadLine();
                if (uint.TryParse(line, out uint blockIndex))
                    SyncUntilBlock = blockIndex;
                else
                    SyncUntilBlock = 0;
                if (SyncUntilBlock < uint.MaxValue)
                    ConsoleHelper.Warning($"Block sync at index > {SyncUntilBlock} paused by Fairy plugin, because {pauseFileName} exists in {RootPath}. Execute `sync` in Neo.CLI to continue block synchronization.");
            }
        }

        /// <summary>
        /// Sleep at block committing, if user does not want to synchronoze new blocks
        /// </summary>
        protected void SyncControl(NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            while (block.Index > SyncUntilBlock)
            {
                string error = $"""{DateTime.Now.ToString("yyyy-MM-dd h:mm:ss tt")} Block synchronization at index {block.Index} > {SyncUntilBlock} paused by fairy. Current block height {NativeContract.Ledger.CurrentIndex(system.StoreView)}. Execute `sync` in Neo.CLI to continue sync, or `sync 0` to pause sync. Reminding you again in {SleepTimeMs.Days}d {SleepTimeMs.Hours}h:{SleepTimeMs.Minutes}m:{SleepTimeMs.Seconds}.{SleepTimeMs.Milliseconds}s""";
                ConsoleHelper.Warning(error);
                CancelSyncSleep = new();
                CancelSyncSleep.Token.WaitHandle.WaitOne((int)SleepTimeMs.TotalMilliseconds);
            }
        }

        [ConsoleCommand("sync", Category = "Fairy Commands", Description = "Pause or continue block synchronization")]
        protected void OnFairySyncCommand(uint blockIndex = uint.MaxValue)
        {
            SyncUntilBlock = blockIndex;
            CancelSyncSleep?.Cancel();
            ConsoleHelper.Info($"Sync until block {blockIndex}");
        }

        [ConsoleCommand("fairy", Category = "Fairy Commands", Description = "List Fairy snapshots")]
        protected void OnFairyCommand()
        {
            foreach (Fairy fairy in fairyServers)
            {
                Console.WriteLine($">>> Fairy@{fairy.settings.BindAddress}:{fairy.settings.Port}");
                ConsoleHelper.Info("★ Test snapshots:");
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

                ConsoleHelper.Info("☆ DebugInfo registration:");
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
            if (SyncUntilBlock < uint.MaxValue)
                ConsoleHelper.Warning($"Fairy sync until block index {SyncUntilBlock}");
        }
    }
}
