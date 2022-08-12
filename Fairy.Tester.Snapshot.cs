using Neo.Json;
using System.Collections.Concurrent;
using System.Numerics;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public readonly ConcurrentDictionary<string, FairyEngine> sessionToEngine = new();
        public struct RuntimeArgs
        {
            public ulong timestamp = 0;
            // public BigInteger? designatedRandom = null;

            public new string ToString()
            {
                return $"timestamp: {timestamp}";//\tdesignatedRandom: {designatedRandom}";
            }
        }
        public readonly ConcurrentDictionary<string, RuntimeArgs> sessionToRuntimeArgs = new();

        private FairyEngine BuildSnapshotWithDummyScript(FairyEngine engine = null)
        {
            return FairyEngine.Run(new byte[] { 0x40 }, engine != null ? engine.Snapshot.CreateSnapshot() : system.StoreView, settings: system.Settings, gas: settings.MaxGasInvoke);
        }

        [RpcMethod]
        protected virtual JToken NewSnapshotsFromCurrentSystem(JArray _params)
        {
            JObject json = new();
            foreach (var param in _params)
            {
                string session = param.AsString();
                if (sessionToEngine.TryGetValue(session, out _))
                    json[session] = true;
                else
                    json[session] = false;
                sessionToEngine[session] = FairyEngine.Run(new byte[] { 0x40 }, system.StoreView, settings: system.Settings, gas: settings.MaxGasInvoke);
                sessionToRuntimeArgs[session] = new();
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
                json[str] = sessionToEngine.Remove(str, out var _) ? sessionToRuntimeArgs.Remove(str, out var _) : false;
            }
            return json;
        }

        [RpcMethod]
        protected virtual JToken ListSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionToEngine.Keys)
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
            sessionToEngine[to] = sessionToEngine[from];
            sessionToEngine.Remove(from, out var _);
            sessionToRuntimeArgs[to] = sessionToRuntimeArgs[from];
            sessionToRuntimeArgs.Remove(from, out var _);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JToken CopySnapshot(JArray _params)
        {
            string from = _params[0].AsString();
            string to = _params[1].AsString();
            sessionToEngine[to] = BuildSnapshotWithDummyScript(sessionToEngine[from]);
            sessionToRuntimeArgs[to] = sessionToRuntimeArgs[from];
            JObject json = new();
            json[to] = from;
            return json;
        }

        [RpcMethod]
        protected virtual JToken SetSnapshotTimestamp(JArray _params)
        {
            string session = _params[0].AsString();
            ulong timestamp = ulong.Parse(_params[1].AsString());
            RuntimeArgs runtimeArgs = sessionToRuntimeArgs[session];
            runtimeArgs.timestamp = timestamp;
            sessionToRuntimeArgs[session] = runtimeArgs;
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
                json[session] = sessionToRuntimeArgs.GetValueOrDefault(session, new RuntimeArgs()).timestamp;
            }
            return json;
        }

        //[RpcMethod]
        //protected virtual JToken SetSnapshotRandom(JArray _params)
        //{
        //    string session = _params[0].AsString();
        //    string? designatedRandomString = _params[1]?.AsString();
        //    if (designatedRandomString == null)
        //    {
        //        RuntimeArgs runtimeArgs = sessionToRuntimeArgs[session];
        //        runtimeArgs.designatedRandom = null;
        //        sessionToRuntimeArgs[session] = runtimeArgs;
        //    }
        //    else
        //    {
        //        BigInteger designatedRandom = BigInteger.Parse(designatedRandomString);
        //        RuntimeArgs runtimeArgs = sessionToRuntimeArgs[session];
        //        runtimeArgs.designatedRandom = designatedRandom;
        //        sessionToRuntimeArgs[session] = runtimeArgs;
        //    }
        //    JObject json = new();
        //    json[session] = designatedRandomString;
        //    return json;
        //}

        //[RpcMethod]
        //protected virtual JToken GetSnapshotRandom(JArray _params)
        //{
        //    JObject json = new();
        //    foreach (var s in _params)
        //    {
        //        string session = s.AsString();
        //        if (sessionToRuntimeArgs.ContainsKey(session))
        //            json[session] = sessionToRuntimeArgs[session].designatedRandom.ToString();
        //        else
        //            json[session] = null;
        //    }
        //    return json;
        //}
    }
}
