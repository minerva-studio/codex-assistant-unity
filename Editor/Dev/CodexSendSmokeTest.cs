using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>
    /// Sends a real message through <see cref="CodexWorkspace"/> and checks that the
    /// turn appears in history. Run from the menu or through script eval:
    /// <c>Minerva.Codex.Editor.Dev.CodexSendSmokeTest.Run()</c>.
    /// </summary>
    public static class CodexSendSmokeTest
    {
        const string Tag = "[CodexSend]";
        const string Prompt = "Reply with exactly the single word: pong. Do not use tools.";
        const double TimeoutSeconds = 180;

        [MenuItem("Tools/Minerva/Codex/Send Smoke Test")]
        public static void Run()
        {
            CodexWorkspace workspace = null;
            try
            {
                workspace = new CodexWorkspace();
                string observedLive = null;
                workspace.Changed += () =>
                {
                    string text = AgentText(workspace.LiveTurn);
                    if (!string.IsNullOrEmpty(text))
                    {
                        observedLive = text;
                    }
                };
                workspace.Connect(Path.GetDirectoryName(Application.dataPath));
                WaitUntil(() => workspace.IsConnected && workspace.ThreadsLoaded && workspace.SettingsLoaded, "thread listing and settings");
                Debug.Log(
                    $"{Tag} connected with {workspace.Threads.Count} thread(s); " +
                    $"model={workspace.SelectedModel} effort={workspace.SelectedEffort} " +
                    $"sandbox={workspace.SelectedSandbox} approval={workspace.ApprovalDisplay} " +
                    $"source={workspace.SettingsSource}");

                if (string.IsNullOrEmpty(workspace.SelectedModel) ||
                    string.IsNullOrEmpty(workspace.SelectedEffort) ||
                    string.IsNullOrEmpty(workspace.SelectedSandbox))
                {
                    throw new InvalidOperationException("the effective config did not provide model/effort/sandbox");
                }

                // Clear the selection so the send exercises the lazy thread/start path.
                ForceNoSelection(workspace);
                observedLive = null;
                Debug.Log($"{Tag} sending first message without a thread");
                if (!workspace.SendMessage(Prompt))
                {
                    throw new InvalidOperationException("SendMessage rejected the first message");
                }

                WaitUntil(() => !workspace.IsRunning, "first turn completion");
                if (workspace.Error != null)
                {
                    throw new InvalidOperationException($"first turn failed: {workspace.Error}");
                }

                // The blocking wait cannot run the throttled transcript updates, so
                // Changed is where the live turn is observed before the reload.
                Debug.Log($"{Tag} first stream: live={Trim(observedLive, 80)}");
                if (string.IsNullOrEmpty(observedLive))
                {
                    throw new InvalidOperationException("the live turn has no streamed agent text");
                }

                WaitUntil(() => workspace.Turns.Count >= 1 && workspace.LiveTurn == null, "first history reload");
                string threadId = workspace.SelectedThreadId;
                if (string.IsNullOrEmpty(threadId))
                {
                    throw new InvalidOperationException("no thread selected after thread/start");
                }

                string firstReply = LastAgentText(workspace);
                Debug.Log($"{Tag} first reply in {threadId}: {Trim(firstReply, 80)}");
                if (string.IsNullOrEmpty(firstReply))
                {
                    throw new InvalidOperationException("the first turn has no agent message");
                }

                observedLive = null;
                Debug.Log($"{Tag} sending second message to the same thread");
                if (!workspace.SendMessage(Prompt))
                {
                    throw new InvalidOperationException("SendMessage rejected the second message");
                }

                WaitUntil(() => !workspace.IsRunning, "second turn completion");
                if (workspace.Error != null)
                {
                    throw new InvalidOperationException($"second turn failed: {workspace.Error}");
                }

                Debug.Log($"{Tag} second stream: live={Trim(observedLive, 80)}");
                if (string.IsNullOrEmpty(observedLive))
                {
                    throw new InvalidOperationException("the second live turn has no streamed agent text");
                }

                WaitUntil(() => workspace.Turns.Count >= 2 && workspace.LiveTurn == null, "second history reload");
                string secondReply = LastAgentText(workspace);
                Debug.Log($"{Tag} second reply: {Trim(secondReply, 80)}");
                if (string.IsNullOrEmpty(secondReply))
                {
                    throw new InvalidOperationException("the second turn has no agent message");
                }

                if (workspace.Turns.Count < 2)
                {
                    throw new InvalidOperationException($"expected 2 turns in history, found {workspace.Turns.Count}");
                }

                Debug.Log($"{Tag} PASS thread={threadId} turns={workspace.Turns.Count}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Tag} FAIL: {exception.Message}");
                Debug.LogException(exception);
            }
            finally
            {
                workspace?.Dispose();
            }
        }

        static void ForceNoSelection(CodexWorkspace workspace)
        {
            PropertyInfo property = typeof(CodexWorkspace).GetProperty(
                "SelectedThreadId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(workspace, null);

            FieldInfo turns = typeof(CodexWorkspace).GetField(
                "m_Turns",
                BindingFlags.Instance | BindingFlags.NonPublic);
            (turns?.GetValue(workspace) as System.Collections.IList)?.Clear();
        }

        static string LastAgentText(CodexWorkspace workspace)
        {
            return AgentText(workspace.Turns.Count > 0 ? workspace.Turns[0] : null);
        }

        static string AgentText(JObject turn)
        {
            JObject message = (turn?["items"] as JArray)?
                .OfType<JObject>()
                .LastOrDefault(item => item["type"]?.Value<string>() == "agentMessage");
            return message?["text"]?.Value<string>();
        }

        static void WaitUntil(Func<bool> condition, string step)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                EditorDispatcher.Pump();
                Thread.Sleep(10);
            }

            EditorDispatcher.Pump();
            if (!condition())
            {
                throw new TimeoutException($"{step} timed out after {TimeoutSeconds:0} s");
            }
        }

        static string Trim(string text, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.Length <= length ? text : text.Substring(0, length) + "...";
        }
    }
}
