using Neo.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        protected Block? committingBlock;
        protected ConcurrentDictionary<NotifyEventArgs, UInt256> notifications = new();
        protected ConcurrentDictionary<SemaphoreSlim, WebSocket> committingBlockSemaphores = new();
        protected ConcurrentDictionary<SemaphoreSlim, WebSocket> notificationSemaphores = new();
        protected DataCache latestSnapshot;

        protected void RegisterBlockchainEvents()
        {
            Blockchain.Committing += delegate (NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
            {
                // block
                committingBlock = block;
                foreach (var item in committingBlockSemaphores)
                    item.Key.Release();

                // notifications
                if (notificationSemaphores.Count > 0)
                {
                    latestSnapshot = snapshot;
                    notifications.Clear();
                    foreach (Blockchain.ApplicationExecuted app in applicationExecutedList)
                        foreach (NotifyEventArgs notification in app.Notifications)
                            if (app.Transaction != null)
                                notifications[notification] = app.Transaction.Hash;
                    foreach (var item in notificationSemaphores)
                        item.Key.Release();
                }
            };
        }

        [WebsocketControlMethod]
        protected virtual JObject UnsubscribeLastAction(WebSocket webSocket, JArray _params, LinkedList<WebSocketSubscription> webSocketSubscriptions)
        {
            WebSocketSubscription webSocketSubscription = webSocketSubscriptions.Last.Value;
            webSocketSubscription.cancellationTokenSource.Cancel();
            webSocketSubscriptions.RemoveLast();
            return webSocketSubscription.ToJson();
        }

        [WebsocketControlMethod]
        protected virtual JToken UnsubscribeMethod(WebSocket webSocket, JArray _params, LinkedList<WebSocketSubscription> webSocketSubscriptions)
        {
            string method = _params[0].AsString();
            JArray returned = new();
            LinkedListNode<WebSocketSubscription>? node = webSocketSubscriptions.Last;
            while (node != null)
            {
                if (node.Value.method == method)
                {
                    webSocketSubscriptions.Remove(node);
                    node.Value.cancellationTokenSource.Cancel();
                    returned.Add(node.Value.ToJson());
                    node = node.Previous;
                }
            }
            return returned;
        }

        [WebsocketMethod]
        protected virtual Action SubscribeCommittingBlock(WebSocket webSocket, JArray _params, CancellationToken cancellationToken)
        {
            return async () =>
            {
                SemaphoreSlim semaphore = new(1);
                committingBlockSemaphores[semaphore] = webSocket;
                while (true)
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        switch (webSocket.State)
                        {
                            case WebSocketState.Open:
                                if (committingBlock != null)
                                    await webSocket.SendAsync(committingBlock.ToJson(system.Settings).ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    committingBlockSemaphores.Remove(semaphore, out _);
                                    return;
                                }
                                break;
                            case WebSocketState.Closed:
                                committingBlockSemaphores.Remove(semaphore, out _);
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, webSocket.State.ToString(), CancellationToken.None);
                                webSocket.Dispose();
                                return;
                            default:
                                committingBlockSemaphores.Remove(semaphore, out _);
                                webSocket.Dispose();
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        committingBlockSemaphores.Remove(semaphore, out _);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, ex.StackTrace, CancellationToken.None);
                        webSocket.Dispose();
                        return;
                    }
                }
            };
        }

        [WebsocketMethod]
        protected virtual Action SubscribeContractEvent(WebSocket webSocket, JArray _params, CancellationToken cancellationToken)
        {
            HashSet<UInt160> contracts =  _params.Count > 0 ? ((JArray)_params[0]).Select(v => UInt160.Parse(v.AsString())).ToHashSet() : new();
            HashSet<string> eventNames =  _params.Count > 1 ? ((JArray)_params[1]).Select(v => v.AsString().ToLower()).ToHashSet() : new();
            return async () =>
            {
                SemaphoreSlim semaphore = new(1);
                notificationSemaphores[semaphore] = webSocket;
                while (true)
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        switch (webSocket.State)
                        {
                            case WebSocketState.Open:
                                JArray json = new();
                                foreach (var item in notifications)
                                {
                                    NotifyEventArgs notification = item.Key;
                                    if (contracts.Count > 0 && !contracts.Contains(notification.ScriptHash))
                                        continue;
                                    if (eventNames.Count > 0 && !eventNames.Contains(notification.EventName.ToLower()))
                                        continue;
                                    JObject notificationJson = new();
                                    notificationJson["tx"] = item.Value.ToString();
                                    notificationJson["scripthash"] = notification.ScriptHash.ToString();
                                    notificationJson["contractname"] = NativeContract.ContractManagement.GetContract(latestSnapshot, notification.ScriptHash)?.Manifest.Name;
                                    notificationJson["eventname"] = notification.EventName;
                                    notificationJson["eventargs"] = notification.State.ToJson();
                                    json.Add(notificationJson);
                                }
                                if (json.Count > 0)
                                    await webSocket.SendAsync(json.ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    notificationSemaphores.Remove(semaphore, out _);
                                    return;
                                }
                                break;
                            case WebSocketState.Closed:
                                notificationSemaphores.Remove(semaphore, out _);
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, webSocket.State.ToString(), CancellationToken.None);
                                webSocket.Dispose();
                                return;
                            default:
                                notificationSemaphores.Remove(semaphore, out _);
                                webSocket.Dispose();
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        notificationSemaphores.Remove(semaphore, out _);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, ex.StackTrace, CancellationToken.None);
                        webSocket.Dispose();
                        return;
                    }
                }
            };
        }
    }
}
