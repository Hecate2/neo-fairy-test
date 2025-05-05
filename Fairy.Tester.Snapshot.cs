// Copyright (C) 2015-2025 The Neo Project.
//
// Fairy.Tester.Snapshot.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Json;
using System.Collections.Concurrent;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        public readonly ConcurrentDictionary<string, FairySession> sessionStringToFairySession = new();

        public FairySession GetOrCreateFairySession(string session)
        {
            FairySession? fairySession;
            if (!sessionStringToFairySession.TryGetValue(session, out fairySession))
            {  // we allow initializing a new session when executing
                fairySession = NewFairySession(system, this);
                sessionStringToFairySession[session] = fairySession;
            }
            return fairySession;
        }

        private FairyEngine BuildSnapshotWithDummyScript(FairyEngine? engine = null)
        {
            return FairyEngine.Run(new byte[] { 0x40 }, engine != null ? engine.SnapshotCache.CloneCache() : system.StoreView, this, settings: system.Settings, gas: settings.MaxGasInvoke, oldEngine: engine, copyRuntimeArgs: true);
        }

        [FairyRpcMethod]
        protected virtual JToken NewSnapshotsFromCurrentSystem(JArray _params)
        {
            JObject json = new();
            foreach (var param in _params)
            {
                string session = param!.AsString();
                if (sessionStringToFairySession.TryGetValue(session, out _))
                    json[session] = true;
                else
                    json[session] = false;
                sessionStringToFairySession[session] = NewFairySession(system, this);
            }
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken DeleteSnapshots(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string str = s!.AsString();
                json[str] = sessionStringToFairySession.Remove(str, out var _);
            }
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken ListSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionStringToFairySession.Keys)
            {
                session.Add(s);
            }
            return session;
        }

        [FairyRpcMethod]
        protected virtual JToken RenameSnapshot(JArray _params)
        {
            string from = _params[0]!.AsString();
            string to = _params[1]!.AsString();
            sessionStringToFairySession[to] = sessionStringToFairySession[from];
            sessionStringToFairySession.Remove(from, out var _);
            JObject json = new();
            json[to] = from;
            return json;
        }

        [FairyRpcMethod]
        protected virtual JToken CopySnapshot(JArray _params)
        {
            string from = _params[0]!.AsString();
            string to = _params[1]!.AsString();
            FairySession testSessionTo = NewFairySession(system, this);
            testSessionTo.engine = BuildSnapshotWithDummyScript(sessionStringToFairySession[from].engine);
            testSessionTo.debugEngine = null;
            sessionStringToFairySession[to] = testSessionTo;
            JObject json = new();
            json[to] = from;
            return json;
        }
    }
}
