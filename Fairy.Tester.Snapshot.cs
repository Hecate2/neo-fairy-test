using Neo.IO.Json;
using Neo.SmartContract;
using System.Collections.Concurrent;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public readonly ConcurrentDictionary<string, ApplicationEngine> sessionToEngine = new();
        public readonly ConcurrentDictionary<string, ulong> sessionToTimestamp = new();

        [RpcMethod]
        protected virtual JObject NewSnapshotsFromCurrentSystem(JArray _params)
        {
            JObject json = new();
            foreach (var param in _params)
            {
                string session = param.AsString();
                if (sessionToEngine.TryGetValue(session, out _))
                    json[session] = true;
                else
                    json[session] = false;
                sessionToEngine[session] = ApplicationEngine.Run(new byte[] { 0x40 }, system.StoreView, settings: system.Settings, gas: settings.MaxGasInvoke);
                sessionToTimestamp[session] = 0;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JObject DeleteSnapshots(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s.AsString();
                json[str] = sessionToEngine.Remove(str, out var _) ? sessionToTimestamp.Remove(str, out var _) : false;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JObject ListSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionToEngine.Keys)
            {
                session.Add(s);
            }
            return session;
        }

        [RpcMethod]
        protected virtual JObject RenameSnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionToEngine[to] = sessionToEngine[from];
            sessionToEngine.Remove(from, out var _);
            sessionToTimestamp[to] = sessionToTimestamp[from];
            sessionToTimestamp.Remove(from, out var _);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JObject CopySnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionToEngine[to] = BuildSnapshotWithDummyScript(sessionToEngine[from]);
            sessionToTimestamp[to] = sessionToTimestamp[from];
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JObject SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0].AsString();
            ulong timestamp = ulong.Parse(_params[1].AsString());
            sessionToTimestamp[session] = timestamp;
            JObject json = new();
            json[session] = timestamp;
            return json;
        }

        [RpcMethod]
        protected virtual JObject GetSnapshotTimeStamp(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                json[session] = sessionToTimestamp.GetValueOrDefault(session, (ulong)0);
            }
            return json;
        }
    }
}
