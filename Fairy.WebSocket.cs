using Neo.ConsoleService;
using Neo.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
//using System.IO.Compression;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Net.WebSockets;
using Akka.Configuration.Hocon;

namespace Neo.Plugins
{
    public partial class Fairy : RpcServer
    {
        public IWebHost websocketHost;
        public readonly Dictionary<string, WebSocket> sessionToWebSocket = new();
        protected readonly Dictionary<string, Func<WebSocket, JArray, CancellationToken, object>> websocketMethods = new();
        protected readonly Dictionary<string, Func<WebSocket, JArray, LinkedList<WebSocketSubscription>, object>> websocketControlMethods = new();

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        public class WebsocketMethodAttribute : Attribute
        {
            public string Name { get; set; }
        }

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        public class WebsocketControlMethodAttribute : Attribute
        {
            public string Name { get; set; }
        }

        public void RegisterWebsocketMethods(object handler)
        {
            foreach (MethodInfo method in handler.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                WebsocketMethodAttribute attribute = method.GetCustomAttribute<WebsocketMethodAttribute>();
                if (attribute is null) continue;
                string name = string.IsNullOrEmpty(attribute.Name) ? method.Name.ToLowerInvariant() : attribute.Name;
                websocketMethods[name] = method.CreateDelegate<Func<WebSocket, JArray, CancellationToken, object>>(handler);
            }
            foreach (MethodInfo method in handler.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                WebsocketControlMethodAttribute attribute = method.GetCustomAttribute<WebsocketControlMethodAttribute>();
                if (attribute is null) continue;
                string name = string.IsNullOrEmpty(attribute.Name) ? method.Name.ToLowerInvariant() : attribute.Name;
                websocketControlMethods[name] = method.CreateDelegate<Func<WebSocket, JArray, LinkedList<WebSocketSubscription>, object>>(handler);
            }
        }

        public void StartWebsocketServer()
        {
            websocketHost = new WebHostBuilder().UseKestrel(options => options.Listen(settings.BindAddress, settings.Port + 1, listenOptions =>
            {
                options.Limits.MaxConcurrentConnections = settings.MaxConcurrentConnections;
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);

                if (string.IsNullOrEmpty(settings.SslCert)) return;
                listenOptions.UseHttps(settings.SslCert, settings.SslCertPassword, httpsConnectionAdapterOptions =>
                {
                    if (settings.TrustedAuthorities is null || settings.TrustedAuthorities.Length == 0)
                        return;
                    httpsConnectionAdapterOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsConnectionAdapterOptions.ClientCertificateValidation = (cert, chain, err) =>
                    {
                        if (err != SslPolicyErrors.None)
                            return false;
                        X509Certificate2 authority = chain.ChainElements[^1].Certificate;
                        return settings.TrustedAuthorities.Contains(authority.Thumbprint);
                    };
                });
            }))
            .Configure(app =>
            {
                app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromMinutes(5) });
                app.Run(ProcessWebsocketAsync);
            })
            .ConfigureServices(services =>
            {
                // Do not use compression?
                // Vulnerable to CRIME/BREACH attacks?
            })
            .Build();

            websocketHost.Start();
            ConsoleHelper.Info($"Fairy websocket server running at {settings.BindAddress}:{settings.Port+1}\n");
        }

        public struct WebSocketSubscription
        {
            public string method;
            public JToken @params;
            public CancellationTokenSource cancellationTokenSource;
            public JObject ToJson()
            {
                JObject json = new();
                json["method"] = method;
                json["params"] = @params;
                json["cancelled"] = cancellationTokenSource.IsCancellationRequested;
                return json;
            }
        }

        public async Task ProcessWebsocketAsync(HttpContext context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Max-Age"] = "31536000";
            using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            byte[] buffer = new byte[4*1024];

            LinkedList<WebSocketSubscription> webSocketSubscriptions = new();

            while (true)
            {
                WebSocketReceiveResult wsResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);//ToDo built in CancellationToken
                if (wsResult.MessageType == WebSocketMessageType.Close)
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

                JToken request;
                using (MemoryStream stream = new MemoryStream())  // MemoryStream has an internal capacity limit 2147483591 < 2**31
                {
                    stream.Write(buffer, 0, wsResult.Count);
                    while (!wsResult.EndOfMessage)
                    {
                        wsResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);  //TODO: CancellationToken
                        stream.Write(buffer, 0, wsResult.Count);
                    }
                    stream.Seek(0, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        request = JObject.Parse(await reader.ReadToEndAsync());
                    }
                }
                string method = request["method"].AsString();
                if (/*!CheckAuth(context) || */settings.DisabledMethods.Contains(method))
                    throw new RpcException(-400, "Access denied");
                JToken @params = request["params"] ?? new JArray();
                if (websocketMethods.TryGetValue(method, out var func))
                {
                    CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                    CancellationToken cancellationToken = cancellationTokenSource.Token;
                    switch (func(webSocket, (JArray)@params, cancellationToken))
                    {
                        case Action action:
                            Task.Run(action);
                            webSocketSubscriptions.AddLast(new WebSocketSubscription { method = method, @params = @params, cancellationTokenSource=cancellationTokenSource });
                            if (request["needresponse"]?.AsBoolean() is true)
                            {
                                JObject response = new();
                                response["jsonrpc"] = "2.0";
                                response["id"] = (string)context.Request.Query["id"];
                                response["result"] = method;
                                await webSocket.SendAsync(response.ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            break;
                        case JObject result:
                            await webSocket.SendAsync(result.ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                            break;
                        default:
                            throw new NotSupportedException();
                    };
                }
                else if (websocketControlMethods.TryGetValue(method, out var controlFunc))
                {
                    switch (controlFunc(webSocket, (JArray)@params, webSocketSubscriptions))
                    {
                        case JObject result:
                            await webSocket.SendAsync(result.ToByteArray(false), WebSocketMessageType.Text, true, CancellationToken.None);
                            break;
                        default:
                            throw new NotSupportedException();
                    };
                }
                else
                    throw new RpcException(-32601, "Method not found");
            }
        }
    }
}
