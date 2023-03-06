using Akka.Util;
using Neo;
using Neo.IO;
using Neo.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        protected uint subscriptionId = 0;
        protected SemaphoreSlim subscriptionIdSemaphore = new(1);
        protected Dictionary<string, ConcurrentSet<WebSocketSubscriptionNeoGoCompatible>> methodNameToSubscriptions = new();
        protected ConcurrentDictionary<uint, WebSocketSubscriptionNeoGoCompatible> idToSubscriptions = new();

        public struct WebSocketSubscriptionNeoGoCompatible
        {
            public uint subscriptionId;
            public WebSocket webSocket;
            public string method;
            public JObject @params;
            public JObject ToJson()
            {
                JObject json = new();
                json["id"] = subscriptionId;
                json["method"] = method;
                json["params"] = @params;
                return json;
            }
        }

        protected void RegisterWebSocketNeoGoCompatible()
        {
            methodNameToSubscriptions["block_added"] = new();
            methodNameToSubscriptions["transaction_added"] = new();
            methodNameToSubscriptions["notification_from_execution"] = new();
            methodNameToSubscriptions["transaction_executed"] = new();
            methodNameToSubscriptions["notary_request_event"] = new();

            Blockchain.Committing += OnBlockAdded;
            system.MemPool.TransactionAdded += OnTransactionAdded;
            Blockchain.Committing += OnNotification;
            Blockchain.Committing += OnTransactionExecuted;
        }

        protected void CloseSubscriptions(IEnumerable<WebSocketSubscriptionNeoGoCompatible> subscriptionsToClose)
        {
            foreach (WebSocketSubscriptionNeoGoCompatible subscription in subscriptionsToClose)
            {
                methodNameToSubscriptions[subscription.method].TryRemove(subscription);
                idToSubscriptions.TryRemove(subscription.subscriptionId, out _);
                subscription.webSocket.Dispose();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="content">if null, only checks and handles unexpected websocket states</param>
        /// <returns>whether the websocket subscription should be closed by <see cref="CloseSubscriptions"/></returns>
        protected async Task<bool> WebSocketSendAsync(WebSocketSubscriptionNeoGoCompatible subscription, JToken? content)
        {
            try
            {
                switch (subscription.webSocket.State)
                {
                    case WebSocketState.Open:
                        if (content == null) return false;
                        await subscription.webSocket.SendAsync(content.ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                        return false;
                    case WebSocketState.Closed:
                        await subscription.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, subscription.webSocket.State.ToString(), CancellationToken.None);
                        goto default;
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                await subscription.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, ex.StackTrace, CancellationToken.None);
                return true;
            }
        }

        protected async void OnBlockAdded(NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            ConcurrentSet<WebSocketSubscriptionNeoGoCompatible> subscriptionsToClose = new();
            ConcurrentQueue<Task> webSocketTasks = new();
            foreach (WebSocketSubscriptionNeoGoCompatible subscription in methodNameToSubscriptions["block_added"])
            {
                JObject _params = subscription.@params;
                if (_params.ContainsProperty("till") && _params["till"].AsNumber() < block.Index)
                {
                    // close this subscription, because it is impossible to have later blocks fulfilling the "till" criteria
                    subscriptionsToClose.TryAdd(subscription);
                    continue;
                }
                if (_params.ContainsProperty("primary") && _params["primary"].AsNumber() != block.PrimaryIndex) continue;
                if (_params.ContainsProperty("since") && _params["since"].AsNumber() > block.Index) continue;

                JObject returnedJson = new();
                returnedJson["jsonrpc"] = "2.0";
                returnedJson["method"] = "transaction_added";
                JArray @params = new() { block.ToJson(system.Settings) };
                returnedJson["params"] = @params;
                webSocketTasks.Append(Task.Run(async () =>
                {
                    if (await WebSocketSendAsync(subscription, returnedJson))
                        subscriptionsToClose.TryAdd(subscription);
                }));
            }
            await Task.WhenAll(webSocketTasks);
            CloseSubscriptions(subscriptionsToClose);
        }

        protected async void OnTransactionAdded(object? sender, Transaction tx)
        {
            ConcurrentSet<WebSocketSubscriptionNeoGoCompatible> subscriptionsToClose = new();
            ConcurrentQueue<Task> webSocketTasks = new();
            foreach (WebSocketSubscriptionNeoGoCompatible subscription in methodNameToSubscriptions["transaction_added"])
            {
                JObject _params = subscription.@params;
                if (_params.ContainsProperty("sender"))
                {
                    string wantedSender = _params["sender"].AsString();
                    UInt160.TryParse(wantedSender, out UInt160 wantedSenderUInt160);
                    if (!wantedSender.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                    {
                        // little-endian; reverse the UInt160
                        wantedSenderUInt160 = new UInt160(wantedSenderUInt160.ToArray().Reverse().ToArray());  // correct order
                        // _params["sender"] = wantedSenderUInt160Reversed.ToString();  // big-endian
                    }
                    if (wantedSenderUInt160 != tx.Sender)
                    {
                        foreach (Signer signer in tx.Signers)
                            if (signer.Account == wantedSenderUInt160)
                                goto sendMessage;
                        continue;  // wanted sender not in tx.Sender or tx.Signers; do not send anything in websocket
                    }
                }
            sendMessage:
                JObject returnedJson = new();
                returnedJson["jsonrpc"] = "2.0";
                returnedJson["method"] = "transaction_added";
                JArray @params = new() { tx.ToJson(system.Settings) };
                returnedJson["params"] = @params;
                webSocketTasks.Append(Task.Run(async () =>
                {
                    if (await WebSocketSendAsync(subscription, returnedJson))
                        subscriptionsToClose.TryAdd(subscription);
                }));
            }
            await Task.WhenAll(webSocketTasks);
            CloseSubscriptions(subscriptionsToClose);
        }

        protected async void OnNotification(NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            ConcurrentSet<WebSocketSubscriptionNeoGoCompatible> subscriptionsToClose = new();
            ConcurrentQueue<Task> webSocketTasks = new();
            foreach (WebSocketSubscriptionNeoGoCompatible subscription in methodNameToSubscriptions["notification_from_execution"])
            {
                JObject _params = subscription.@params;
                foreach (Blockchain.ApplicationExecuted app in applicationExecutedList)
                {
                    foreach (NotifyEventArgs notification in app.Notifications)
                    {
                        if (_params.ContainsProperty("contract"))
                        {
                            string wantedContract = _params["sender"].AsString();
                            UInt160.TryParse(wantedContract, out UInt160 wantedContractUInt160);
                            if (!wantedContract.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                            {
                                // little-endian; reverse the UInt160
                                wantedContractUInt160 = new UInt160(wantedContractUInt160.ToArray().Reverse().ToArray());  // correct order
                                // _params["contract"] = wantedSenderUInt160Reversed.ToString();  // big-endian
                            }
                            if (wantedContractUInt160 != notification.ScriptHash)
                                continue;
                        }
                        if (_params.ContainsProperty("name") && _params["name"].AsString() != notification.EventName)
                            continue;
                        JObject returnedJson = new();
                        returnedJson["jsonrpc"] = "2.0";
                        returnedJson["method"] = "notification_from_execution";
                        JObject notificationJson = new();
                        notificationJson["container"] = app.Transaction?.Hash.ToString();
                        notificationJson["name"] = notification.EventName;
                        notificationJson["contract"] = notification.ScriptHash.ToString();
                        notificationJson["contractname"] = NativeContract.ContractManagement.GetContract(latestSnapshot, notification.ScriptHash)?.Manifest.Name;
                        notificationJson["state"] = notification.State.ToJson();
                        JArray @params = new() { notificationJson };
                        returnedJson["params"] = @params;
                        webSocketTasks.Append(Task.Run(async () =>
                        {
                            if (await WebSocketSendAsync(subscription, returnedJson))
                                subscriptionsToClose.TryAdd(subscription);
                        }));
                    }
                }
            }
            await Task.WhenAll(webSocketTasks);
            CloseSubscriptions(subscriptionsToClose);
        }

        protected async void OnTransactionExecuted(NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            ConcurrentSet<WebSocketSubscriptionNeoGoCompatible> subscriptionsToClose = new();
            ConcurrentQueue<Task> webSocketTasks = new();
            foreach (WebSocketSubscriptionNeoGoCompatible subscription in methodNameToSubscriptions["transaction_executed"])
            {
                JObject _params = subscription.@params;
                foreach (Blockchain.ApplicationExecuted app in applicationExecutedList)
                {
                    if (_params.ContainsProperty("container"))
                    {
                        string wantedTxOrBlock = _params["container"].AsString();
                        UInt256.TryParse(wantedTxOrBlock, out UInt256 wantedTxOrBlockUInt256);
                        if (!wantedTxOrBlock.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                        {
                            // little-endian; reverse the UInt256
                            wantedTxOrBlockUInt256 = new UInt256(wantedTxOrBlockUInt256.ToArray().Reverse().ToArray());  // correct order
                            // _params["contract"] = wantedSenderUInt160Reversed.ToString();  // big-endian
                        }

                        if (wantedTxOrBlockUInt256 == block.Hash)
                        {
                            subscriptionsToClose.TryAdd(subscription);  // impossble to have another container of the same hash; remove subscription
                            if (_params.ContainsProperty("state") && _params["state"].AsString() != Enum.GetName(app.VMState))
                                continue;
                            else
                                goto sendMessage;
                        }
                        if (wantedTxOrBlockUInt256 == app.Transaction?.Hash)
                        {
                            subscriptionsToClose.TryAdd(subscription);  // impossble to have another container of the same hash; remove subscription
                            if (_params.ContainsProperty("state") && _params["state"].AsString() != Enum.GetName(app.VMState))
                                continue;
                            else
                                goto sendMessage;
                        }
                        continue;
                    }
                    else
                    {
                        if (_params.ContainsProperty("state") && _params["state"].AsString() != Enum.GetName(app.VMState))
                            continue;
                    }
                sendMessage:
                    JObject returnedJson = new();
                    returnedJson["jsonrpc"] = "2.0";
                    returnedJson["method"] = "transaction_executed";
                    JArray @params = new() { app.Transaction?.ToJson(system.Settings) };
                    returnedJson["params"] = @params;
                    webSocketTasks.Append(Task.Run(async () =>
                    {
                        if (await WebSocketSendAsync(subscription, returnedJson))
                            subscriptionsToClose.TryAdd(subscription);
                    }));
                }
            }
            await Task.WhenAll(webSocketTasks);
            CloseSubscriptions(subscriptionsToClose);
        }

        [WebsocketNeoGoCompatibleMethod]
        protected virtual object Subscribe(WebSocket webSocket, JArray _params)
        {
            string methodName = _params[0].AsString();
            if (!methodNameToSubscriptions.ContainsKey(methodName))
                throw new NotImplementedException(methodName);
            subscriptionIdSemaphore.Wait();
            uint newId = subscriptionId;
            subscriptionId += 1;
            // subscriptionIdSemaphore.Release();  // after return

            WebSocketSubscriptionNeoGoCompatible subscription = new WebSocketSubscriptionNeoGoCompatible { subscriptionId=newId, webSocket=webSocket, method = methodName, @params = _params.Count > 1 ? (JObject)_params[1] : new JObject() };
            methodNameToSubscriptions[methodName].TryAdd(subscription);
            idToSubscriptions[newId] = subscription;
            return newId;
        }

        [WebsocketNeoGoCompatibleMethod]
        protected virtual object Unsubscribe(WebSocket webSocket, JArray _params)
        {
            foreach (JToken param in _params)
            {
                uint subscriptionId = uint.Parse(param.AsString(), System.Globalization.NumberStyles.HexNumber);
                if (!idToSubscriptions.ContainsKey(subscriptionId))
                    throw new ArgumentException($"{subscriptionId}");
                idToSubscriptions.Remove(subscriptionId, out WebSocketSubscriptionNeoGoCompatible subscription);
                methodNameToSubscriptions[subscription.method].TryRemove(subscription);
            }
            return true;
        }

        [WebsocketControlMethod]
        protected virtual JToken ListSubscriptions(WebSocket webSocket, JArray _params, LinkedList<object> webSocketSubscriptions)
        {
            JArray json = new();
            foreach (object subscription in webSocketSubscriptions)
                if (subscription is WebSocketSubscriptionNeoGoCompatible)
                    json.Add(((WebSocketSubscriptionNeoGoCompatible)subscription).ToJson());
            return json;
        }
    }
}
