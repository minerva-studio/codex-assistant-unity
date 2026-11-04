using System;
using Newtonsoft.Json.Linq;

namespace Minerva.Codex
{
    /// <summary>Filters shared by thread listing and thread search.</summary>
    internal sealed class CodexThreadQuery
    {
        public int Limit = 50;
        public string[] SourceKinds;
        public string Cwd;
        public string SearchTerm;
        public bool IncludeArchived;
    }

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
                ["capabilities"] = new JObject
                {
                    // thread/search is gated behind the experimental API.
                    ["experimentalApi"] = true,
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

        public void ListThreads(CodexThreadQuery query, Action<JArray> onResult, Action<string> onError)
        {
            var parameters = BuildThreadParameters(query);
            if (!string.IsNullOrEmpty(query.Cwd))
            {
                parameters["cwd"] = query.Cwd;
            }

            Request(
                "thread/list",
                parameters,
                result => onResult?.Invoke(result as JArray ?? result?["data"] as JArray),
                onError);
        }

        public void SearchThreads(CodexThreadQuery query, Action<JArray> onResult, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                onError?.Invoke("searchTerm is required for thread/search");
                return;
            }

            var parameters = BuildThreadParameters(query);
            parameters["searchTerm"] = query.SearchTerm;
            Request("thread/search", parameters, result => onResult?.Invoke(result?["data"] as JArray), onError);
        }

        public void ArchiveThread(string threadId, Action onResult, Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            Request("thread/archive", parameters, _ => onResult?.Invoke(), onError);
        }

        public void UnsubscribeThread(string threadId, Action onResult, Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            Request("thread/unsubscribe", parameters, _ => onResult?.Invoke(), onError);
        }

        static JObject BuildThreadParameters(CodexThreadQuery query)
        {
            var parameters = new JObject
            {
                ["limit"] = query.Limit,
                ["archived"] = query.IncludeArchived,
                ["sortKey"] = "updated_at",
                ["sortDirection"] = "desc",
            };

            if (query.SourceKinds != null && query.SourceKinds.Length > 0)
            {
                var kinds = new JArray();
                foreach (string kind in query.SourceKinds)
                {
                    kinds.Add(kind);
                }

                parameters["sourceKinds"] = kinds;
            }

            return parameters;
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

        public void ResumeThread(
            string threadId,
            bool excludeTurns,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            if (excludeTurns)
            {
                parameters["excludeTurns"] = true;
            }

            Request("thread/resume", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void StartThread(
            string cwd,
            string model,
            string sandbox,
            JToken approvalPolicy,
            Action<JObject> onResult,
            Action<string> onError,
            bool ephemeral = false)
        {
            var parameters = new JObject();
            if (!string.IsNullOrEmpty(cwd))
            {
                parameters["cwd"] = cwd;
            }

            if (!string.IsNullOrEmpty(model))
            {
                parameters["model"] = model;
            }

            if (!string.IsNullOrEmpty(sandbox))
            {
                parameters["sandbox"] = sandbox;
            }

            AddIfPresent(parameters, "approvalPolicy", approvalPolicy);
            if (ephemeral)
            {
                parameters["ephemeral"] = true;
            }

            Request("thread/start", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void ForkThread(
            string threadId,
            bool excludeTurns,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            if (excludeTurns)
            {
                parameters["excludeTurns"] = true;
            }

            Request("thread/fork", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void SetThreadName(
            string threadId,
            string name,
            Action onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["name"] = name,
            };

            Request("thread/name/set", parameters, _ => onResult?.Invoke(), onError);
        }

        public void ReadConfig(string cwd, Action<JObject> onResult, Action<string> onError)
        {
            var parameters = new JObject { ["includeLayers"] = false };
            if (!string.IsNullOrEmpty(cwd))
            {
                parameters["cwd"] = cwd;
            }

            Request("config/read", parameters, result => onResult?.Invoke(result?["config"] as JObject), onError);
        }

        public void ListModels(int limit, Action<JArray> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["limit"] = limit,
                ["includeHidden"] = true,
            };

            Request("model/list", parameters, result => onResult?.Invoke(result?["data"] as JArray), onError);
        }

        public void ListTurns(string threadId, int limit, Action<JArray> onResult, Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["limit"] = limit,
                // Tool and reasoning records are omitted by the summary view.
                ["itemsView"] = "full",
            };

            Request("thread/turns/list", parameters, result => onResult?.Invoke(result?["data"] as JArray), onError);
        }

        /// <summary>
        /// Updates the thread's sticky settings. This is the API path for changing
        /// reasoning effort between turns; it avoids restarting the thread.
        /// </summary>
        public void UpdateThreadSettings(
            string threadId,
            string effort,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["effort"] = effort,
            };

            Request("thread/settings/update", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void StartTurn(
            string threadId,
            string text,
            string cwd,
            string model,
            string effort,
            JObject sandboxPolicy,
            JToken approvalPolicy,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["input"] = TextInput(text),
            };

            if (!string.IsNullOrEmpty(cwd))
            {
                parameters["cwd"] = cwd;
            }

            if (!string.IsNullOrEmpty(model))
            {
                parameters["model"] = model;
            }

            if (!string.IsNullOrEmpty(effort))
            {
                parameters["effort"] = effort;
            }

            // Without this, GPT-family models can run with encrypted reasoning
            // and no plaintext summary in the persisted turn.
            parameters["summary"] = "auto";

            if (sandboxPolicy != null)
            {
                parameters["sandboxPolicy"] = sandboxPolicy;
            }

            AddIfPresent(parameters, "approvalPolicy", approvalPolicy);

            Request("turn/start", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        /// <summary>Adds input to the active turn without interrupting its tools.</summary>
        public void SteerTurn(
            string threadId,
            string expectedTurnId,
            string text,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["expectedTurnId"] = expectedTurnId,
                ["input"] = TextInput(text),
            };

            Request("turn/steer", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void AddQueuedTurn(
            string threadId,
            string clientUserMessageId,
            string text,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["clientUserMessageId"] = clientUserMessageId,
                ["input"] = TextInput(text),
            };

            Request("thread/queue/add", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void ListQueuedTurns(
            string threadId,
            int limit,
            Action<JArray> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["limit"] = limit,
            };

            Request(
                "thread/queue/list",
                parameters,
                result => onResult?.Invoke(result?["data"] as JArray),
                onError);
        }

        public void DeleteQueuedTurn(
            string threadId,
            string queuedSubmissionId,
            Action onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["queuedSubmissionId"] = queuedSubmissionId,
            };

            Request("thread/queue/delete", parameters, _ => onResult?.Invoke(), onError);
        }

        static JArray TextInput(string text)
        {
            return new JArray(new JObject
            {
                ["type"] = "text",
                ["text"] = text,
            });
        }

        static void AddIfPresent(JObject target, string name, JToken value)
        {
            if (value != null && value.Type != JTokenType.Null)
            {
                target[name] = value.DeepClone();
            }
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
