using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Exercises composer requests against an inert stream; never starts an inference turn.</summary>
    internal static class CodexAcceptanceSmokeTest
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        sealed class Fixture : IDisposable
        {
            public readonly CodexAssistantWindow Window;
            public readonly CodexWorkspace Workspace = new CodexWorkspace();
            public readonly MemoryStream Output = new MemoryStream();
            public readonly List<string> Errors = new List<string>();
            readonly Application.LogCallback m_Log;
            public CodexConnection Connection;
            public TextField Composer => Window.rootVisualElement.Q<TextField>("codex-composer");

            public Fixture()
            {
                m_Log = (message, _, level) =>
                {
                    if (level == LogType.Error || level == LogType.Exception || level == LogType.Assert) Errors.Add(message);
                };
                Application.logMessageReceived += m_Log;
                Window = ScriptableObject.CreateInstance<CodexAssistantWindow>();
                var initial = (CodexWorkspace)Get(Window, "m_Workspace");
                var initialConnection = (CodexConnection)Get(initial, "m_Connection");
                if (initialConnection != null)
                    Invoke(initialConnection, "Route", new JObject { ["id"] = (long)Get(initialConnection, "m_LastId"), ["result"] = new JObject() });
                // OnEnable performs the normal handshake; detach before substituting the inert sink.
                Invoke(Window, "OnDisable");
                Window.titleContent = new GUIContent("Daily acceptance fixture");
                BindConnection();
                Set(Workspace, "m_ConfigLoaded", true);
                Property(Workspace, "ThreadsLoaded", true);
                Property(Workspace, "SelectedThreadId", "acceptance-thread");
                Set(Window, "m_Workspace", Workspace);
                Set(Window, "m_AttachUnityContext", false);
                Invoke(Window, "BuildUi");
                Window.ShowUtility();
                Window.position = new Rect(100, 100, 1000, 720);
            }

            public void BindConnection()
            {
                Connection = Sink(Output);
                Connection.Disconnected += reason => Invoke(Workspace, "OnDisconnected", reason);
                Set(Workspace, "m_Client", new CodexClient(Connection));
                Set(Workspace, "m_Connection", Connection);
                Set(Workspace, "m_Ready", true);
            }

            public JObject Request(string method) => Encoding.UTF8.GetString(Output.ToArray()).Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).Last(request => request["method"]?.Value<string>() == method);

            public void Reply(JObject request, JObject result = null, string error = null, CodexConnection connection = null)
            {
                var response = new JObject { ["id"] = request["id"].DeepClone() };
                if (error != null) response["error"] = new JObject { ["code"] = -32000, ["message"] = error };
                else response["result"] = result ?? new JObject();
                Invoke(connection ?? Connection, "Route", response);
                EditorDispatcher.Pump();
            }

            public void Dispose()
            {
                Window.Close();
                Output.Dispose();
                Application.logMessageReceived -= m_Log;
            }
        }

        [MenuItem("Tools/Minerva/Codex/Check Daily Workflow")]
        public static string Run()
        {
            using var fixture = new Fixture();
            var window = fixture.Window;
            var workspace = fixture.Workspace;
            fixture.Composer.value = "Keep this unsent draft";
            Invoke(window, "SendComposer", false);
            Require(fixture.Composer.value == "Keep this unsent draft" && !fixture.Composer.enabledSelf,
                "an in-flight request retains and locks the draft until acknowledgment");
            long length = fixture.Output.Length;
            Invoke(window, "SendComposer", false);
            Require(fixture.Output.Length == length, "repeated submission cannot send the same in-flight draft twice");
            fixture.Reply(fixture.Request("turn/start"), error: "fixture rejection");
            Require(fixture.Composer.value == "Keep this unsent draft" && fixture.Composer.enabledSelf && !workspace.IsRunning,
                "asynchronous rejection preserves an editable draft");
            Invoke(window, "SendComposer", false);
            fixture.Reply(fixture.Request("turn/start"), TurnResult());
            Require(fixture.Composer.value == "" && workspace.IsRunning, "successful acknowledgment clears the submitted draft");

            fixture.Composer.value = "Queue this later";
            Invoke(window, "SendComposer", false);
            fixture.Reply(fixture.Request("thread/queue/add"), error: "fixture queue rejection");
            Require(fixture.Composer.value == "Queue this later" && fixture.Composer.enabledSelf, "queue rejection does not discard input");
            Invoke(window, "SendComposer", false);
            fixture.Reply(fixture.Request("thread/queue/add"));
            Require(fixture.Composer.value == "", "queue acknowledgment clears only the confirmed input");
            var queue = new JArray(new JObject { ["id"] = "queued-1", ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = "Queue this later" }) });
            fixture.Reply(fixture.Request("thread/queue/list"), new JObject { ["data"] = queue });
            Require(workspace.QueuedMessages.Count == 1, "confirmed queue state hydrates from the server");

            fixture.Composer.value = "Steer or queue";
            Invoke(window, "SendComposer", true);
            fixture.Reply(fixture.Request("turn/steer"), error: "fixture steer rejection");
            Require(fixture.Composer.value == "Steer or queue" && !fixture.Composer.enabledSelf, "steer fallback retains the pending draft");
            fixture.Reply(fixture.Request("thread/queue/add"));
            Require(fixture.Composer.value == "", "steer fallback clears only after queue acknowledgment");

            fixture.Composer.value = "Preserve across reconnect";
            Invoke(window, "SendComposer", false);
            CodexConnection oldConnection = fixture.Connection;
            JObject oldRequest = fixture.Request("thread/queue/add");
            Set(workspace, "m_Ready", false);
            Invoke(window, "UpdateComposer");
            oldConnection.Dispose();
            fixture.BindConnection();
            Property(workspace, "IsRunning", false);
            Invoke(window, "UpdateComposer");
            fixture.Composer.value = "New draft after reconnect";
            fixture.Reply(oldRequest, connection: oldConnection);
            Require(fixture.Composer.value == "New draft after reconnect" && fixture.Composer.enabledSelf,
                "late old acknowledgment cannot clear a newer draft after connection replacement");
            Invoke(workspace, "LoadQueue", "acceptance-thread");
            fixture.Reply(fixture.Request("thread/queue/list"), new JObject { ["data"] = queue });
            Require(workspace.QueuedMessages.Count == 1, "reconnect reads the server queue instead of replaying local messages");

            Property(workspace, "SelectedThreadId", null);
            fixture.Composer.value = "First message in a new thread";
            Invoke(window, "SendComposer", false);
            fixture.Reply(fixture.Request("thread/start"), new JObject { ["thread"] = new JObject { ["id"] = "new-thread" } });
            Require(fixture.Composer.value.Length > 0 && !fixture.Composer.enabledSelf, "thread creation alone is not a message acknowledgment");
            fixture.Reply(fixture.Request("turn/start"), error: "fixture first-turn rejection");
            Require(fixture.Composer.value == "First message in a new thread", "first-turn rejection preserves a new conversation's input");

            string image = Path.Combine(Path.GetTempPath(), "codex-draft-" + Guid.NewGuid().ToString("N") + ".png");
            var texture = new Texture2D(2, 2);
            try
            {
                File.WriteAllBytes(image, texture.EncodeToPNG());
                fixture.Composer.value = "Reload-safe draft\nSecond line";
                Invoke(window, "AddAttachment", image);
                string serialized = EditorJsonUtility.ToJson(window);
                Invoke(window, "ClearAttachments");
                fixture.Composer.value = "";
                EditorJsonUtility.FromJsonOverwrite(serialized, window);
                Invoke(window, "BuildUi");
                var attachments = (List<CodexImageAttachment>)Get(window, "m_Attachments");
                Require(fixture.Composer.value == "Reload-safe draft\nSecond line" && attachments.Count == 1
                    && attachments[0].Path == image && attachments[0].Thumbnail != null,
                    "Unity serialization restores draft text, image path and a recreated preview");
                Invoke(window, "SendComposer", false);
                fixture.Reply(fixture.Request("turn/start"), error: "fixture image rejection");
                Require(attachments.Count == 1 && fixture.Composer.value.Length > 0, "failed image submission retains the draft and attachment");
                Invoke(window, "SendComposer", false);
                fixture.Reply(fixture.Request("turn/start"), TurnResult());
                Require(attachments.Count == 0 && fixture.Composer.value == "", "confirmed image submission releases its draft preview");
                Property(workspace, "IsRunning", false);
                fixture.Composer.value = "Retain on write failure";
                fixture.Output.Close();
                Invoke(window, "SendComposer", false);
                EditorDispatcher.Pump();
                Invoke(window, "UpdateComposer");
                Require(!fixture.Connection.IsOpen && !workspace.IsConnected
                    && fixture.Composer.value == "Retain on write failure" && !(bool)Get(window, "m_Submitting"),
                    "write failure recovery: closed=" + !fixture.Connection.IsOpen + ", ready=" + workspace.IsConnected
                    + ", retained=" + (fixture.Composer.value == "Retain on write failure") + ", pending=" + Get(window, "m_Submitting"));
                int closedCallbacks = 0;
                byte[] written = fixture.Output.ToArray();
                fixture.Connection.Call("turn/start", new JObject(), (_, error) =>
                {
                    Require(error != null, "a closed transport rejects subsequent requests");
                    closedCallbacks++;
                });
                EditorDispatcher.Pump();
                Require(closedCallbacks == 1 && written.SequenceEqual(fixture.Output.ToArray()),
                    "closed transport reports failure once without writing another request");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                File.Delete(image);
            }
            Require(fixture.Errors.Count == 0, "no asynchronous Editor errors during the fixture: " + string.Join("; ", fixture.Errors));
            return "PASS: send/queue/steer acknowledgment, rejection recovery, duplicate guard, late callbacks, server queue hydration, new-thread failure, serialized image draft, transport write failure and disposed initialization";
        }

        /// <summary>Measures bounded synthetic workloads; construction timings do not include every later Editor layout frame.</summary>
        public static JObject Measure()
        {
            using var fixture = new Fixture();
            var turns = (List<JObject>)Get(fixture.Workspace, "m_Turns");
            for (int index = 0; index < 200; index++)
                turns.Add(new JObject
                {
                    ["id"] = "history-" + index, ["status"] = "completed", ["items"] = new JArray(
                        new JObject { ["type"] = "userMessage", ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = "Review scene change " + index }) },
                        new JObject { ["type"] = "agentMessage", ["text"] = "Updated **scene placement**, checked the local coordinates and preserved the existing object references.\n\n- Camera bounds\n- Roof alignment\n- Preview configuration" })
                });
            Set(fixture.Workspace, "m_LiveThreadId", "acceptance-thread");
            Set(fixture.Workspace, "m_LiveTurn", new JObject { ["id"] = "stream-turn", ["items"] = new JArray() });
            Property(fixture.Workspace, "IsRunning", true);
            var watch = Stopwatch.StartNew();
            Invoke(fixture.Window, "RebuildTranscript");
            long historyMs = watch.ElapsedMilliseconds;
            var transcript = (ScrollView)Get(fixture.Window, "m_Transcript");
            VisualElement historical = transcript[0];
            double slowest = 0;
            watch.Restart();
            for (int index = 0; index < 200; index++)
            {
                Invoke(fixture.Workspace, "OnAgentMessageDelta", new JObject
                {
                    ["threadId"] = "acceptance-thread", ["itemId"] = "stream-item",
                    ["delta"] = "Streaming update with **source context** and ordinary text.\n",
                });
                if (index % 10 != 9) continue;
                var render = Stopwatch.StartNew();
                Invoke(fixture.Window, "OnTranscriptChanged");
                slowest = Math.Max(slowest, render.Elapsed.TotalMilliseconds);
            }
            long streamMs = watch.ElapsedMilliseconds;
            Require(ReferenceEquals(historical, transcript[0]), "stream updates retain historical visual elements");
            var patch = new CodexPatchView(wrap: true);
            string source = string.Join("\n", Enumerable.Range(0, 2000).Select(index => "public int Field" + index + " = " + index + ";"));
            watch.Restart();
            patch.SetPatch(source, "add", "Large.cs");
            long patchMs = watch.ElapsedMilliseconds;
            Require(patch.Additions == 2000 && patch.Patch == source, "large patch retains all source and counts");
            var terminal = new CodexTerminalBuffer();
            watch.Restart();
            for (int index = 0; index < 4000; index++)
            {
                terminal.Append("Output " + index + ": " + new string('x', 70) + "\r\n");
                if (index % 50 == 49) { string snapshot = terminal.Text; Require(snapshot.Length <= 200000, "streamed terminal output remains capped"); }
            }
            Require(fixture.Errors.Count == 0, "no asynchronous Editor errors during workload: " + string.Join("; ", fixture.Errors));
            return new JObject
            {
                ["historyTurns"] = 200, ["historyBuildMs"] = historyMs,
                ["streamDeltas"] = 200, ["streamRenderPasses"] = 20, ["streamTotalMs"] = streamMs, ["slowestStreamRenderMs"] = slowest,
                ["patchLines"] = 2000, ["patchBuildMs"] = patchMs,
                ["terminalChunks"] = 4000, ["terminalMs"] = watch.ElapsedMilliseconds, ["terminalRetainedChars"] = terminal.Text.Length,
                ["historyReused"] = true,
            };
        }

        static JObject TurnResult() => new JObject { ["turn"] = new JObject { ["id"] = "active-turn", ["status"] = "inProgress", ["items"] = new JArray() } };
        static CodexConnection Sink(Stream output)
        {
            var connection = (CodexConnection)FormatterServices.GetUninitializedObject(typeof(CodexConnection));
            Set(connection, "m_Stdin", new StreamWriter(output, new UTF8Encoding(false), 1024, true) { AutoFlush = true });
            Set(connection, "m_SendLock", new object());
            Set(connection, "m_Pending", new ConcurrentDictionary<long, Action<JToken, CodexRpcError>>());
            return connection;
        }

        static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Daily acceptance: " + message);
        }
    }
}
