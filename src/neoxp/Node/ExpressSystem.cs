using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Neo;
using Neo.BlockchainToolkit;
using Neo.BlockchainToolkit.Models;
using Neo.Consensus;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using NeoExpress.Models;
using static Neo.Ledger.Blockchain;

namespace NeoExpress.Node
{
    public partial class ExpressSystem : IDisposable
    {
        const string APP_LOGS_STORE_PATH = "app-logs-store";
        const string NOTIFICATIONS_STORE_PATH = "notifications-store";

        readonly ExpressChain chain;
        readonly ExpressConsensusNode node;
        readonly NeoSystem neoSystem;
        readonly ApplicationEngineProviderPlugin? appEngineProviderPlugin;
        readonly ConsolePlugin consolePlugin;
        readonly DBFTPlugin dbftPlugin;
        readonly PersistenceWrapper persistenceWrapper;
        readonly StorageProviderPlugin storageProviderPlugin;
        readonly WebServerPlugin webServerPlugin;
        readonly IStore appLogsStore;
        readonly IStore notificationsStore;


        public void Dispose()
        {
            neoSystem.Dispose();
        }

        public ExpressSystem(ExpressChain chain, ExpressConsensusNode node, IStorageProvider storageProvider, IConsole console, bool trace, uint? secondsPerBlock)
        {
            this.chain = chain;
            this.node = node;

            appLogsStore = storageProvider.GetStore(APP_LOGS_STORE_PATH);
            notificationsStore = storageProvider.GetStore(NOTIFICATIONS_STORE_PATH);

            var settings = GetProtocolSettings(chain, secondsPerBlock);

            consolePlugin = new ConsolePlugin(console);
            appEngineProviderPlugin = trace ? new ApplicationEngineProviderPlugin() : null;
            storageProviderPlugin = new StorageProviderPlugin(storageProvider);
            persistenceWrapper = new PersistenceWrapper(this.OnPersist);
            webServerPlugin = new WebServerPlugin(chain, node);
            dbftPlugin = new DBFTPlugin(GetConsensusSettings(chain));
            neoSystem = new NeoSystem(settings, storageProviderPlugin.Name);
        }

        public async Task RunAsync(CancellationToken token)
        {
            if (node.IsRunning()) { throw new Exception("Node already running"); }

            var tcs = new TaskCompletionSource<bool>();
            _ = Task.Run(() =>
            {
                try
                {
                    var wallet = DevWallet.FromExpressWallet(neoSystem.Settings, node.Wallet);
                    var defaultAccount = node.Wallet.Accounts.Single(a => a.IsDefault);

                    using var mutex = new Mutex(true, NodeUtility.GLOBAL_PREFIX + defaultAccount.ScriptHash);

                    neoSystem.StartNode(new Neo.Network.P2P.ChannelsConfig
                    {
                        Tcp = new IPEndPoint(IPAddress.Loopback, node.TcpPort),
                        WebSocket = new IPEndPoint(IPAddress.Loopback, node.WebSocketPort),
                    });
                    webServerPlugin.Start();
                    dbftPlugin.Start(wallet);

                    // DevTracker looks for a string that starts with "Neo express is running" to confirm that the instance has started
                    // Do not remove or re-word this console output:
                    consolePlugin.WriteLine($"Neo express is running ({storageProviderPlugin.Provider.GetType().Name})");

                    var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(token, webServerPlugin.CancellationToken);
                    linkedToken.Token.WaitHandle.WaitOne();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });
            await tcs.Task.ConfigureAwait(false);
        }

        private void OnPersist(Block block, IReadOnlyList<ApplicationExecuted> appExecutions)
        {
            throw new NotImplementedException();
        }

        static Neo.ProtocolSettings GetProtocolSettings(ExpressChain chain, uint? secondsPerBlock)
        {
            var _secondsPerBlock = secondsPerBlock.HasValue
                ? secondsPerBlock.Value
                : chain.TryReadSetting<uint>("chain.SecondsPerBlock", uint.TryParse, out var secondsPerBlockSetting)
                    ? secondsPerBlockSetting
                    : 0;
            return chain.GetProtocolSettings(_secondsPerBlock);
        }

        static Neo.Consensus.Settings GetConsensusSettings(ExpressChain chain)
        {
            var settings = new Dictionary<string, string>()
                {
                    { "PluginConfiguration:Network", $"{chain.Network}" }
                };

            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new Neo.Consensus.Settings(config.GetSection("PluginConfiguration"));
        }
    }
}
