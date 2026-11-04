using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>
    /// Loads real Codex history and renders it into UI Toolkit elements without
    /// opening a window. Batchmode:
    /// -executeMethod Minerva.Codex.Editor.Dev.CodexRenderSmoke.Run
    /// </summary>
    internal static class CodexRenderSmoke
    {
        const string Tag = "[CodexRender]";
        const int TimeoutMs = 30000;

        [MenuItem("Tools/Minerva/Codex/Diagnose Rendering")]
        public static void Run()
        {
            bool batch = Application.isBatchMode;
            int exitCode = 1;
            var workspace = new CodexWorkspace();

            try
            {
                workspace.Connect(ProjectRoot, CodexPreferences.CodexHome);
                WaitUntil(() => workspace.Threads.Count > 0 || workspace.Error != null, "thread list");

                if (workspace.Threads.Count == 0)
                {
                    throw new InvalidOperationException(workspace.Error ?? "no threads");
                }

                Debug.Log($"{Tag} loaded {workspace.Threads.Count} threads, selected {workspace.SelectedThreadId}");
                WaitUntil(() => workspace.Turns.Count > 0 || workspace.Error != null, "turn list");

                if (workspace.Turns.Count == 0)
                {
                    throw new InvalidOperationException(workspace.Error ?? "no turns");
                }

                var root = new VisualElement();
                int itemCount = 0;
                for (int turnIndex = workspace.Turns.Count - 1; turnIndex >= 0; turnIndex--)
                {
                    if (workspace.Turns[turnIndex]["items"] is not JArray items)
                    {
                        continue;
                    }

                    foreach (JToken token in items)
                    {
                        if (token is JObject item)
                        {
                            CodexItemRenderer.Render(root, item, true, true);
                            itemCount++;
                        }
                    }
                }

                List<string> labels = Labels(root)
                    .Select(label => label.text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Take(6)
                    .ToList();

                Debug.Log($"{Tag} rendered {itemCount} items into {root.childCount} elements");
                foreach (string label in labels)
                {
                    Debug.Log($"{Tag}   label: {Trim(label, 80)}");
                }

                exitCode = 0;
                Debug.Log($"{Tag} PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Tag} FAIL: {exception.Message}");
                Debug.LogException(exception);
            }
            finally
            {
                workspace.Dispose();
                if (batch)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        static IEnumerable<Label> Labels(VisualElement root)
        {
            foreach (VisualElement child in root.Children())
            {
                if (child is Label label)
                {
                    yield return label;
                }

                foreach (Label nested in Labels(child))
                {
                    yield return nested;
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

        static string Trim(string text, int length)
        {
            string singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
            return singleLine.Length <= length ? singleLine : singleLine.Substring(0, length) + "...";
        }

        static string ProjectRoot => System.IO.Path.GetDirectoryName(Application.dataPath);
    }
}
