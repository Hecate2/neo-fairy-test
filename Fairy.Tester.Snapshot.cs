using Neo.Json;
using System.Collections.Concurrent;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public readonly ConcurrentDictionary<string, FairySession> sessionStringToFairySession = new();

        public FairySession NewTestSession()
        {
            return new FairySession(this);
        }

        private FairyEngine BuildSnapshotWithDummyScript(FairyEngine engine = null)
        {
            return FairyEngine.Run(new byte[] { 0x40 }, engine != null ? engine.Snapshot.CreateSnapshot() : system.StoreView, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: engine);
        }

        [RpcMethod]
        protected virtual JToken NewSnapshotsFromCurrentSystem(JArray _params)
        {
            JObject json = new();
            foreach (var param in _params)
            {
                string session = param.AsString();
                if (sessionStringToFairySession.TryGetValue(session, out _))
                    json[session] = true;
                else
                    json[session] = false;
                sessionStringToFairySession[session] = NewTestSession();
            }
            return json;
        }

        [RpcMethod]
        protected virtual JToken DeleteSnapshots(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s.AsString();
                json[str] = sessionStringToFairySession.Remove(str, out var _);
            }
            return json;
        }

        [RpcMethod]
        protected virtual JToken ListSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionStringToFairySession.Keys)
            {
                session.Add(s);
            }
            return session;
        }

        [RpcMethod]
        protected virtual JToken RenameSnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionStringToFairySession[to] = sessionStringToFairySession[from];
            sessionStringToFairySession.Remove(from, out var _);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JToken CopySnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionStringToFairySession[to] = sessionStringToFairySession[from];
            var testSessionTo = sessionStringToFairySession[to];
            testSessionTo.engine = BuildSnapshotWithDummyScript(sessionStringToFairySession[from].engine);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JToken SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0].AsString();
            ulong timestamp = ulong.Parse(_params[1].AsString());
            sessionStringToFairySession[session].timestamp = timestamp;
            JObject json = new();
            json[session] = timestamp;
            return json;
        }

        [RpcMethod]
        protected virtual JToken GetSnapshotTimeStamp(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s.AsString();
                json[session] = sessionStringToFairySession[session].timestamp;
            }
            return json;
        }
    }
}
