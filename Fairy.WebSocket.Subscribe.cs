using Akka.Configuration.Hocon;
using Akka.Util.Internal;
using Neo.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System.Net.WebSockets;
using System.Security.Policy;
using static Neo.Plugins.Fairy;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        protected TaskCompletionSource<Block> committedBlock = new();

        protected void RegisterBlockchainEvents()
        {
            Blockchain.Committed += delegate (NeoSystem @system, Block @block) { committedBlock.SetResult(block); };
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
                while (true)
                {
                    Block block = await committedBlock.Task;
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(block.ToJson(system.Settings).ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                        if (cancellationToken.IsCancellationRequested)
                            return;
                    }
                    else
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, webSocket.State.ToString(), CancellationToken.None);
                        webSocket.Dispose();
                        return;
                    }
                }
            };
        }
    }
}
