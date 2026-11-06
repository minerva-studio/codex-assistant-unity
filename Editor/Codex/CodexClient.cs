using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>
        /// Reads one page of threads and returns the whole response so the caller
        /// can read <c>nextCursor</c>. An empty cursor starts from the newest page.
        /// </summary>
        public void ListThreads(
            CodexThreadQuery query,
            string cursor,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = BuildThreadParameters(query);
            if (!string.IsNullOrEmpty(query.Cwd))
            {
                parameters["cwd"] = query.Cwd;
            }

            if (!string.IsNullOrEmpty(cursor))
            {
                parameters["cursor"] = cursor;
            }

            Request("thread/list", parameters, result => onResult?.Invoke(result as JObject), onError);
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

        public void WriteCommandInput(
            string processId,
            string text,
            Action onResult,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(processId))
            {
                onError?.Invoke("processId is required for command/exec/write");
                return;
            }

            var parameters = new JObject
            {
                ["processId"] = processId,
                ["deltaBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty)),
            };
            Request("command/exec/write", parameters, _ => onResult?.Invoke(), onError);
        }

        /// <summary>
        /// Runs a persistent PTY shell in the server sandbox. The response is
        /// deferred until the shell exits; output arrives through
        /// <c>command/exec/outputDelta</c> notifications.
        /// </summary>
        public void StartSandboxedShell(
            JArray command,
            string cwd,
            string processId,
            JObject environment,
            int columns,
            int rows,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["command"] = command,
                ["processId"] = processId,
                ["tty"] = true,
                ["disableTimeout"] = true,
                ["disableOutputCap"] = true,
                ["env"] = environment,
                ["size"] = new JObject { ["cols"] = columns, ["rows"] = rows },
            };
            if (!string.IsNullOrEmpty(cwd))
            {
                parameters["cwd"] = cwd;
            }

            Request("command/exec", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        /// <summary>
        /// Spawns an unsandboxed PTY shell on the app-server host. Output and exit
        /// are delivered through <c>process/outputDelta</c> and
        /// <c>process/exited</c>.
        /// </summary>
        public void StartLocalShell(
            JArray command,
            string cwd,
            string processHandle,
            JObject environment,
            int columns,
            int rows,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["command"] = command,
                ["cwd"] = cwd ?? string.Empty,
                ["processHandle"] = processHandle,
                ["tty"] = true,
                ["timeoutMs"] = JValue.CreateNull(),
                ["outputBytesCap"] = JValue.CreateNull(),
                ["env"] = environment,
                ["size"] = new JObject { ["cols"] = columns, ["rows"] = rows },
            };
            Request("process/spawn", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        /// <summary>Writes literal UTF-8 input to a client-owned local PTY shell.</summary>
        public void WriteProcessInput(string processHandle, string text, Action onResult, Action<string> onError)
        {
            Request("process/writeStdin", new JObject
            {
                ["processHandle"] = processHandle,
                ["deltaBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            }, _ => onResult?.Invoke(), onError);
        }

        /// <summary>Updates the PTY viewport in character cells for a client-owned shell.</summary>
        public void ResizeShell(string id, bool local, int columns, int rows, Action<string> onError)
        {
            Request(local ? "process/resizePty" : "command/exec/resize", new JObject
            {
                [local ? "processHandle" : "processId"] = id,
                ["size"] = new JObject { ["cols"] = columns, ["rows"] = rows },
            }, null, onError);
        }

        public void TerminateCommand(string processId, Action onResult, Action<string> onError)
        {
            Request(
                "command/exec/terminate",
                new JObject { ["processId"] = processId },
                _ => onResult?.Invoke(),
                onError);
        }

        public void KillProcess(string processHandle, Action onResult, Action<string> onError)
        {
            Request(
                "process/kill",
                new JObject { ["processHandle"] = processHandle },
                _ => onResult?.Invoke(),
                onError);
        }

        public void ArchiveThread(string threadId, Action onResult, Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            Request("thread/archive", parameters, _ => onResult?.Invoke(), onError);
        }

        public void UnarchiveThread(string threadId, Action<JObject> onResult, Action<string> onError)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            Request(
                "thread/unarchive",
                parameters,
                result => onResult?.Invoke(result as JObject),
                onError);
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
            Action<string> onError,
            string lastTurnId = null)
        {
            var parameters = new JObject { ["threadId"] = threadId };
            if (excludeTurns)
            {
                parameters["excludeTurns"] = true;
            }

            if (!string.IsNullOrEmpty(lastTurnId))
            {
                parameters["lastTurnId"] = lastTurnId;
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

        /// <summary>
        /// Reads one page of turns, newest first. <paramref name="cursor"/> is the
        /// previous response's <c>nextCursor</c>; the response carries the next one.
        /// </summary>
        public void ListTurns(
            string threadId,
            int limit,
            string cursor,
            Action<JObject> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["limit"] = limit,
                // Tool and reasoning records are omitted by the summary view.
                ["itemsView"] = "full",
            };

            if (!string.IsNullOrEmpty(cursor))
            {
                parameters["cursor"] = cursor;
            }

            Request("thread/turns/list", parameters, result => onResult?.Invoke(result as JObject), onError);
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
            Action<string> onError,
            IReadOnlyList<string> imagePaths = null)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["input"] = Input(text, imagePaths),
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
            Action<string> onError,
            IReadOnlyList<string> imagePaths = null)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["expectedTurnId"] = expectedTurnId,
                ["input"] = Input(text, imagePaths),
            };

            Request("turn/steer", parameters, result => onResult?.Invoke(result as JObject), onError);
        }

        public void AddQueuedTurn(
            string threadId,
            string clientUserMessageId,
            string text,
            Action<JObject> onResult,
            Action<string> onError,
            IReadOnlyList<string> imagePaths = null)
        {
            var parameters = new JObject
            {
                ["threadId"] = threadId,
                ["clientUserMessageId"] = clientUserMessageId,
                ["input"] = Input(text, imagePaths),
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

        /// <summary>
        /// Lists skills discoverable by the app-server for <paramref name="cwd"/>.
        /// The response is the raw <c>data</c> array; callers own flattening and
        /// caching because the list is UI state, not connection state.
        /// </summary>
        public void ListSkills(
            string cwd,
            Action<JArray> onResult,
            Action<string> onError)
        {
            var parameters = new JObject
            {
                ["cwds"] = new JArray(string.IsNullOrEmpty(cwd) ? string.Empty : cwd),
                ["forceReload"] = false,
            };

            Request(
                "skills/list",
                parameters,
                result => onResult?.Invoke(result?["data"] as JArray),
                onError);
        }

        static JArray Input(string text, IReadOnlyList<string> imagePaths)
        {
            var input = new JArray(new JObject
            {
                ["type"] = "text",
                ["text"] = text ?? string.Empty,
            });

            if (imagePaths == null)
            {
                return input;
            }

            foreach (string path in imagePaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                input.Add(new JObject
                {
                    ["type"] = "localImage",
                    ["path"] = path,
                });
            }

            return input;
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
