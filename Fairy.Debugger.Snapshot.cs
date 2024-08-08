using Neo.Json;

namespace Neo.Plugins
{
    public partial class Fairy
    {
        [RpcMethod]
        protected virtual JToken ListDebugSnapshots(JArray _params)
        {
            JArray session = new JArray();
            foreach (string s in sessionStringToFairySession.Keys)
            {
                if (sessionStringToFairySession[s].debugEngine != null)
                    session.Add(s);
            }
            return session;
        }

        [RpcMethod]
        protected virtual JObject DeleteDebugSnapshots(JArray _params)
        {
            JObject json = new();
            foreach (var s in _params)
            {
                string session = s!.AsString();
                if (sessionStringToFairySession[session].debugEngine != null)
                {
                    json[session] = true;
                    sessionStringToFairySession[session].debugEngine = null;
                }
                else
                    json[session] = false;
            }
            return json;
        }
    }
}
