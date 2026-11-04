using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>
    /// Verifies the transport against a real Codex installation without a window.
    /// Batchmode: -executeMethod Minerva.Codex.Editor.Dev.CodexSmokeTest.Run
    /// </summary>
    internal static class CodexSmokeTest
    {
        const string Tag = "[CodexSmoke]";
        const int TimeoutMs = 30000;

        [MenuItem("Tools/Minerva/Codex/Diagnose App Server")]
        public static void Run()
        {
            bool batch = Application.isBatchMode;
            int exitCode = 1;
            var stderr = new StringBuilder();
            CodexConnection connection = null;

            try
            {
                CodexLaunchSpec spec = CodexPaths.Resolve();
                Debug.Log($"{Tag} launching: {spec}");

                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                Process process = CodexAppServerProcess.Start(spec, projectRoot, stderr);
                connection = new CodexConnection(process);
                var client = new CodexClient(connection);

                connection.Notification += message => Debug.Log($"{Tag} event {message["method"]}");
                connection.Disconnected += reason => Debug.LogWarning($"{Tag} disconnected: {reason}");

                JObject ready = null;
                string failure = null;
                client.Initialize(result => ready = result, error => failure = error);
                WaitUntil(() => ready != null || failure != null, "initialize");

                if (failure != null)
                {
                    throw new InvalidOperationException($"initialize failed: {failure}");
                }

                Debug.Log($"{Tag} initialized: userAgent={ready?["userAgent"]} os={ready?["platformOs"]}");

                JArray threads = null;
                failure = null;
                client.ListThreads(
                    10,
                    new[] { "cli", "vscode", "exec", "appServer", "unknown" },
                    result => threads = result,
                    error => failure = error);
                WaitUntil(() => threads != null || failure != null, "thread/list");

                if (failure != null)
                {
                    throw new InvalidOperationException($"thread/list failed: {failure}");
                }

                Debug.Log($"{Tag} thread/list returned {threads.Count} thread(s)");
                foreach (JObject thread in threads.Take(5).OfType<JObject>())
                {
                    Debug.Log($"{Tag}   thread {thread["id"]} cwd={thread["cwd"]} updated={thread["updatedAt"]} status={thread["status"]?["type"]} preview={Trim(thread["preview"] ?? thread["name"], 60)}");
                }

                JObject newest = threads.OfType<JObject>().FirstOrDefault();
                if (newest == null)
                {
                    Debug.Log($"{Tag} no threads available to inspect");
                    exitCode = 0;
                    return;
                }

                string threadId = newest["id"]?.Value<string>();
                JObject read = null;
                failure = null;
                client.ReadThread(threadId, false, result => read = result, error => failure = error);
                WaitUntil(() => read != null || failure != null, "thread/read");

                if (failure != null)
                {
                    throw new InvalidOperationException($"thread/read failed: {failure}");
                }

                Debug.Log($"{Tag} thread/read ok: {read?["thread"]?["id"]}");

                JArray turns = null;
                failure = null;
                client.ListTurns(threadId, 5, result => turns = result, error => failure = error);
                WaitUntil(() => turns != null || failure != null, "thread/turns/list");

                if (failure != null)
                {
                    throw new InvalidOperationException($"thread/turns/list failed: {failure}");
                }

                Debug.Log($"{Tag} thread/turns/list returned {turns.Count} turn(s)");
                foreach (JObject turn in turns.OfType<JObject>())
                {
                    JArray items = turn["items"] as JArray;
                    string firstType = items?.FirstOrDefault()?["type"]?.Value<string>();
                    Debug.Log($"{Tag}   turn {turn["id"]} status={turn["status"]} items={items?.Count ?? 0} first={firstType}");
                }

                exitCode = 0;
                Debug.Log($"{Tag} PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Tag} FAIL: {exception.Message}");
                Debug.LogException(exception);
                string diagnostics = stderr.ToString().Trim();
                if (diagnostics.Length > 0)
                {
                    Debug.LogError($"{Tag} app-server stderr:\n{diagnostics}");
                }
            }
            finally
            {
                connection?.Dispose();
                if (batch)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        static void WaitUntil(Func<bool> condition, string step)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < TimeoutMs)
            {
                EditorDispatcher.Pump();
                Thread.Sleep(10);
            }

            EditorDispatcher.Pump();
            if (!condition())
            {
                throw new TimeoutException($"{step} timed out after {TimeoutMs} ms");
            }
        }

        static string Trim(JToken token, int length)
        {
            string text = token?.Value<string>() ?? string.Empty;
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.Length <= length ? text : text.Substring(0, length) + "...";
        }
    }
}
