using System;
using Newtonsoft.Json.Linq;

namespace Minerva.Codex
{
    /// <summary>
    /// The app-server calls the assistant needs. Parameters stay as JObject so a
    /// protocol addition never breaks deserialization.
    /// </summary>
    internal sealed class CodexClient
    {
        public const string ClientName = "minerva_codex_assistant";
        public const string ClientTitle = "Codex Assistant for Unity";
        public const string ClientVersion = "0.1.0";

        readonly CodexConnection m_Connection;

        public CodexClient(CodexConnection connection)
        {
            m_Connection = connection;
        }

        public CodexConnection Connection => m_Connection;

        public void Initialize(Action<JObject> onReady, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["clientInfo"] = new JObject
                {
                    ["name"] = ClientName,
                    ["title"] = ClientTitle,
                    ["version"] = ClientVersion,
                },
            };

            m_Connection.Call("initialize", parameters, (result, error) =>
            {
                if (error != null)
                {
                    onError?.Invoke(error.ToString());
                    return;
                }

                m_Connection.Notify("initialized", new JObject());
                onReady?.Invoke(result as JObject);
            });
        }

        public void ListThreads(int limit, string[] sourceKinds, Action<JArray> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["limit"] = limit,
                ["sortKey"] = "updated_at",
                ["sortDirection"] = "desc",
            };

            if (sourceKinds != null && sourceKinds.Length > 0)
            {
                var kinds = new JArray();
                foreach (string kind in sourceKinds)
                {
                    kinds.Add(kind);
                }

                parameters["sourceKinds"] = kinds;
            }

            Request("thread/list", parameters, result => onResult?.Invoke(result as JArray ?? result?["data"] as JArray), onError);
        }

        public void ReadThread(string threadId, bool includeTurns, Action<JObject> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["includeTurns"] = includeTurns,
            };

            Request("thread/read", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void ResumeThread(string threadId, Action<JObject> onResult, Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            Request("thread/resume", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void ListTurns(string threadId, int limit, Action<JArray> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["limit"] = limit,
                ["itemsView"] = "summary",
            };

            Request("thread/turns/list", parameters, result => onResult?.Invoke(result?["data"] as JArray), onError);
        }

        public void StartTurn(string threadId, string text, string cwd, Action<JObject> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["input"] = new JArray(new JObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                }),
            };

            if (!string.IsNullOrEmpty(cwd))
            {
                parameters["cwd"] = cwd;
            }

            Request("turn/start", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void InterruptTurn(string threadId, string turnId, Action onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["turnId"] = turnId,
            };

            Request("turn/interrupt", parameters, _ => onResult?.Invoke(), onError);
        }

        void Request(string method, JObject parameters, Action<JToken> onResult, Action<string> onError)
        {
            m_Connection.Call(method, parameters, (result, error) =>
            {
                if (error != null)
                {
                    onError?.Invoke(error.ToString());
                    return;
                }

                onResult?.Invoke(result);
            });
        }
    }
}
