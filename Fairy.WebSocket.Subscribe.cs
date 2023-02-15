using Neo.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        protected Block committedBlock;
        protected ConcurrentDictionary<SemaphoreSlim, WebSocket> committedBlockSemaphores = new();
        List<SemaphoreSlim> keysToRemove = new();

        protected void RegisterBlockchainEvents()
        {
            Blockchain.Committed += delegate (NeoSystem @system, Block @block)
            {
                committedBlock = block;
                foreach (var item in committedBlockSemaphores)
                    if (item.Value.State == WebSocketState.Open)
                        item.Key.Release();
                    else
                        keysToRemove.Add(item.Key);
                foreach (SemaphoreSlim key in keysToRemove)
                    committedBlockSemaphores.TryRemove(key, out _);
                keysToRemove.Clear();
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
        protected virtual Action SubscribeCommittedBlock(WebSocket webSocket, JArray _params, CancellationToken cancellationToken)
        {
            return async () =>
            {
                SemaphoreSlim semaphore = new(1);
                committedBlockSemaphores[semaphore] = webSocket;
                while (true)
                {
                    try
                    {
                        await semaphore.WaitAsync();
                        switch (webSocket.State)
                        {
                            case WebSocketState.Open:
                                await webSocket.SendAsync(committedBlock.ToJson(system.Settings).ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                                if (cancellationToken.IsCancellationRequested)
                                    return;
                                break;
                            case WebSocketState.Closed:
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, webSocket.State.ToString(), CancellationToken.None);
                                webSocket.Dispose();
                                return;
                            default:
                                break;
                        }
                    }
                    catch
                    {
                        webSocket.Dispose();
                        return;
                    }
                }
            };
        }
    }
}
