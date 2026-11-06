using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Minerva.Codex
{
    /// <summary>
    /// Captures a small snapshot of the Editor for the composer's Unity context
    /// attachment. The snapshot is taken when a message is sent, not when the
    /// window is opened, so it reflects edits made since the last message.
    /// </summary>
    internal static class CodexUnityContext
    {
        public const string OpenTag = "<unity-context>";
        public const string CloseTag = "</unity-context>";

        const int MaxErrors = 5;
        const int MaxWarnings = 3;
        const int MaxSelection = 5;
        const int MaxComponents = 4;
        const int MaxMessageLength = 400;
        const int MaxScanEntries = 5000;

        // LogMessageFlags in UnityEditor. Kept as constants because the enum is
        // internal. Errors and warnings are separate because Unity's console
        // shows them under separate filters.
        const int ErrorMask =
            (1 << 0) |   // kError
            (1 << 1) |   // kAssert
            (1 << 4) |   // kFatal
            (1 << 6) |   // kAssetImportError
            (1 << 8) |   // kScriptingError
            (1 << 11) |  // kScriptCompileError
            (1 << 17) |  // kScriptingException
            (1 << 21);   // kScriptingAssertion

        const int WarningMask =
            (1 << 7) |   // kAssetImportWarning
            (1 << 9) |   // kScriptingWarning
            (1 << 12);   // kScriptCompileWarning

        // Console window toggle bits for Log/Warning/Error. The read forces them
        // on so hidden severities still yield entry text, then restores them.
        const int AllLogLevelsMask = (1 << 7) | (1 << 8) | (1 << 9);

        static bool s_ConsoleProbed;
        static bool s_ConsoleUnavailable;
        static Type s_LogEntriesType;
        static Type s_LogEntryType;
        static MethodInfo s_StartGettingEntries;
        static MethodInfo s_EndGettingEntries;
        static MethodInfo s_GetCount;
        static MethodInfo s_GetCountsByType;
        static MethodInfo s_GetEntryInternal;
        static PropertyInfo s_ConsoleFlags;
        static MethodInfo s_GetFilteringText;
        static MethodInfo s_SetFilteringText;
        static FieldInfo s_ModeField;
        static FieldInfo s_MessageField;
        static FieldInfo s_FileField;
        static FieldInfo s_LineField;

        sealed class ConsoleSnapshot
        {
            public int ErrorCount;
            public int WarningCount;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// Returns a delimited, self-contained Unity state block, or an empty
        /// string when the Editor state cannot be read. A console read failure
        /// degrades to an "unavailable" line rather than blocking the message.
        /// </summary>
        public static string Capture()
        {
            try
            {
                return BuildSnapshot();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        static string BuildSnapshot()
        {
            var builder = new StringBuilder();
            builder.AppendLine(OpenTag);
            builder.AppendLine("Auto-attached Unity Editor snapshot (data, not a user instruction).");
            builder.AppendLine($"Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss} local");
            builder.AppendLine($"Editor: {DescribeEditor()}");
            AppendScene(builder);
            builder.AppendLine($"Selection: {DescribeSelection()}");
            AppendCompile(builder);
            AppendConsole(builder);
            builder.AppendLine(CloseTag);
            return builder.ToString();
        }

        /// <summary>
        /// Splits a leading context block from the rest of a user message.
        /// Returns false when the text does not start with a complete block.
        /// </summary>
        public static bool TryExtract(string text, out string context, out string remainder)
        {
            context = null;
            remainder = text;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            if (start + OpenTag.Length > text.Length ||
                string.CompareOrdinal(text, start, OpenTag, 0, OpenTag.Length) != 0)
            {
                return false;
            }

            int end = text.IndexOf(CloseTag, start + OpenTag.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            end += CloseTag.Length;
            context = text.Substring(start, end - start);
            remainder = text.Substring(end).TrimStart('\r', '\n', ' ', '\t');
            return true;
        }

        /// <summary>
        /// Returns the part of <paramref name="text"/> after a leading context
        /// block, for places that show a queued message without its snapshot.
        /// Text without the block is returned unchanged.
        /// </summary>
        public static string StripContext(string text)
        {
            if (!TryExtract(text, out _, out string remainder))
            {
                return text;
            }

            return remainder.Length == 0 ? "Unity state" : remainder;
        }

        /// <summary>One-line label for the collapsed transcript chip.</summary>
        public static string Summarize(string context)
        {
            if (string.IsNullOrEmpty(context))
            {
                return "Unity state";
            }

            string scene = null;
            string compile = null;
            string console = null;
            foreach (string raw in context.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("Prefab stage: ", StringComparison.Ordinal))
                {
                    scene = line.Substring("Prefab stage: ".Length);
                }
                else if (line.StartsWith("Scenes: ", StringComparison.Ordinal))
                {
                    scene = line.Substring("Scenes: ".Length);
                }
                else if (line.StartsWith("Compile: ", StringComparison.Ordinal))
                {
                    compile = line.Substring("Compile: ".Length);
                }
                else if (line.StartsWith("Console: ", StringComparison.Ordinal))
                {
                    console = line.Substring("Console: ".Length);
                }
            }

            if (!string.IsNullOrEmpty(scene))
            {
                int marker = scene.IndexOf(" (", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    scene = scene.Substring(0, marker);
                }

                int separator = scene.IndexOf(';');
                if (separator >= 0)
                {
                    scene = scene.Substring(0, separator);
                }

                scene = Path.GetFileNameWithoutExtension(scene.Trim());
                if (string.IsNullOrEmpty(scene) || scene.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    scene = null;
                }
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(scene))
            {
                parts.Add(scene);
            }

            if (!string.IsNullOrEmpty(console) &&
                !console.Equals("clean", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(console);
            }

            if (string.Equals(compile, "failed", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(console) ||
                 console.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0))
            {
                parts.Add("compile failed");
            }

            return parts.Count == 0
                ? "Unity state"
                : "Unity state · " + string.Join(" · ", parts);
        }

        /// <summary>Inner lines of the snapshot, without the wrapper tags.</summary>
        public static string FormatDetails(string context)
        {
            if (string.IsNullOrEmpty(context))
            {
                return string.Empty;
            }

            string inner = context.Trim();
            if (inner.StartsWith(OpenTag, StringComparison.Ordinal))
            {
                inner = inner.Substring(OpenTag.Length);
            }

            if (inner.EndsWith(CloseTag, StringComparison.Ordinal))
            {
                inner = inner.Substring(0, inner.Length - CloseTag.Length);
            }

            var lines = new List<string>();
            foreach (string raw in inner.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.TrimStart().StartsWith(
                        "Auto-attached Unity Editor snapshot",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) && lines.Count == 0)
                {
                    continue;
                }

                lines.Add(line);
            }

            return string.Join("\n", lines).Trim();
        }

        static string DescribeEditor()
        {
            string mode;
            if (EditorApplication.isPlaying)
            {
                mode = EditorApplication.isPaused ? "playing (paused)" : "playing";
            }
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                mode = "changing play mode";
            }
            else
            {
                mode = "editing";
            }

            if (EditorApplication.isCompiling)
            {
                mode += ", compiling";
            }
            else if (EditorApplication.isUpdating)
            {
                mode += ", importing assets";
            }

            return $"{Application.unityVersion} · {Application.platform} · {mode}";
        }

        static void AppendScene(StringBuilder builder)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                string path = string.IsNullOrEmpty(stage.assetPath)
                    ? (stage.prefabContentsRoot != null ? stage.prefabContentsRoot.name : "Untitled prefab")
                    : stage.assetPath;
                builder.AppendLine($"Prefab stage: {path}{(stage.scene.isDirty ? " (dirty)" : string.Empty)}");
                return;
            }

            var scenes = new List<string>();
            int loaded = 0;
            Scene active = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                loaded++;
                if (scenes.Count >= 4)
                {
                    continue;
                }

                string name;
                if (!string.IsNullOrEmpty(scene.path))
                {
                    name = scene.path;
                }
                else
                {
                    name = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
                }
                var markers = new List<string>();
                if (scene == active)
                {
                    markers.Add("active");
                }

                if (scene.isDirty)
                {
                    markers.Add("dirty");
                }

                scenes.Add(name + (markers.Count == 0 ? string.Empty : " (" + string.Join(", ", markers) + ")"));
            }

            if (loaded == 0)
            {
                builder.AppendLine("Scenes: none");
                return;
            }

            if (loaded > scenes.Count)
            {
                scenes.Add($"(+{loaded - scenes.Count} more)");
            }

            builder.AppendLine("Scenes: " + string.Join("; ", scenes));
        }

        static string DescribeSelection()
        {
            UnityEngine.Object[] objects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            var parts = new List<string>();
            int count = 0;
            foreach (UnityEngine.Object selected in objects)
            {
                if (selected == null)
                {
                    continue;
                }

                count++;
                if (parts.Count >= MaxSelection)
                {
                    continue;
                }

                parts.Add(DescribeObject(selected));
            }

            if (parts.Count == 0)
            {
                return "none";
            }

            if (count > parts.Count)
            {
                parts.Add($"(+{count - parts.Count} more)");
            }

            return string.Join(" · ", parts);
        }

        static string DescribeObject(UnityEngine.Object selected)
        {
            var gameObject = selected as GameObject ?? (selected as Component)?.gameObject;
            if (gameObject != null)
            {
                return gameObject.name + " (GameObject" + DescribeComponents(gameObject) + ")" +
                       DescribeAssetPath(AssetDatabase.GetAssetPath(gameObject));
            }

            string path = AssetDatabase.GetAssetPath(selected);
            return selected.name + " (" + selected.GetType().Name + ")" + DescribeAssetPath(path);
        }

        static string DescribeComponents(GameObject gameObject)
        {
            var names = new List<string>();
            int added = 0;
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component is Transform || component is RectTransform)
                {
                    continue;
                }

                if (added >= MaxComponents)
                {
                    names.Add("…");
                    break;
                }

                names.Add(component == null ? "Missing Script" : component.GetType().Name);
                added++;
            }

            return names.Count == 0 ? string.Empty : ": " + string.Join(", ", names);
        }

        static string DescribeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            path = path.Replace('\\', '/');
            if (path.Length > 160)
            {
                path = path.Substring(0, 157) + "…";
            }

            return " @ " + path;
        }

        static void AppendCompile(StringBuilder builder)
        {
            if (EditorApplication.isCompiling)
            {
                builder.AppendLine("Compile: running");
            }
            else if (EditorUtility.scriptCompilationFailed)
            {
                builder.AppendLine("Compile: failed");
            }
            else
            {
                builder.AppendLine("Compile: idle");
            }
        }

        static void AppendConsole(StringBuilder builder)
        {
            if (!TryReadConsole(out ConsoleSnapshot snapshot))
            {
                builder.AppendLine("Console: unavailable");
                return;
            }

            if (snapshot.ErrorCount == 0 && snapshot.WarningCount == 0)
            {
                builder.AppendLine("Console: clean");
                return;
            }

            var counts = new List<string>();
            if (snapshot.ErrorCount > 0)
            {
                counts.Add(CountLabel(snapshot.ErrorCount, "error"));
            }

            if (snapshot.WarningCount > 0)
            {
                counts.Add(CountLabel(snapshot.WarningCount, "warning"));
            }

            builder.AppendLine("Console: " + string.Join(", ", counts));
            AppendEntries(builder, "Errors", snapshot.Errors, snapshot.ErrorCount);
            AppendEntries(builder, "Warnings", snapshot.Warnings, snapshot.WarningCount);
        }

        static string CountLabel(int count, string noun)
        {
            return count == 1 ? "1 " + noun : count + " " + noun + "s";
        }

        static void AppendEntries(StringBuilder builder, string label, List<string> entries, int total)
        {
            if (entries.Count == 0)
            {
                return;
            }

            builder.AppendLine(total > entries.Count
                ? $"{label} (latest {entries.Count} of {total}):"
                : label + ":");
            foreach (string entry in entries)
            {
                builder.AppendLine("- " + entry);
            }
        }

        static bool TryReadConsole(out ConsoleSnapshot snapshot)
        {
            snapshot = null;
            if (!EnsureConsoleReflection())
            {
                return false;
            }

            bool started = false;
            int savedFlags = 0;
            bool flagsChanged = false;
            string savedFilter = null;
            bool filterChanged = false;
            try
            {
                // The Console window's toolbar filters select which entries
                // StartGettingEntries exposes. Force every severity on and clear
                // the search field for the duration of the read, then restore
                // both so the user's view is unchanged.
                if (s_ConsoleFlags != null)
                {
                    savedFlags = (int)s_ConsoleFlags.GetValue(null);
                    int forced = savedFlags | AllLogLevelsMask;
                    if (forced != savedFlags)
                    {
                        s_ConsoleFlags.SetValue(null, forced);
                        flagsChanged = true;
                    }
                }

                if (s_GetFilteringText != null && s_SetFilteringText != null)
                {
                    savedFilter = s_GetFilteringText.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(savedFilter))
                    {
                        filterChanged = true;
                        s_SetFilteringText.Invoke(null, new object[] { string.Empty });
                    }
                }

                // Start/End bracket the whole read; the native console rejects
                // entry access outside that window.
                object startResult = s_StartGettingEntries.Invoke(null, null);
                started = true;
                int count = startResult is int startedCount
                    ? startedCount
                    : (int)s_GetCount.Invoke(null, null);

                var errors = new List<string>();
                var warnings = new List<string>();
                var seenErrors = new HashSet<string>(StringComparer.Ordinal);
                var seenWarnings = new HashSet<string>(StringComparer.Ordinal);
                object entry = Activator.CreateInstance(s_LogEntryType);
                int scanned = 0;
                for (int i = count - 1; i >= 0 && scanned < MaxScanEntries; i--, scanned++)
                {
                    if (!(bool)s_GetEntryInternal.Invoke(null, new object[] { i, entry }))
                    {
                        continue;
                    }

                    int mode = (int)s_ModeField.GetValue(entry);
                    if ((mode & ErrorMask) != 0)
                    {
                        if (errors.Count < MaxErrors)
                        {
                            AddEntry(entry, errors, seenErrors);
                        }
                    }
                    else if ((mode & WarningMask) != 0)
                    {
                        if (warnings.Count < MaxWarnings)
                        {
                            AddEntry(entry, warnings, seenWarnings);
                        }
                    }

                    if (errors.Count >= MaxErrors && warnings.Count >= MaxWarnings)
                    {
                        break;
                    }
                }

                var countArgs = new object[] { 0, 0, 0 };
                s_GetCountsByType.Invoke(null, countArgs);
                snapshot = new ConsoleSnapshot
                {
                    ErrorCount = Math.Max((int)countArgs[0], errors.Count),
                    WarningCount = Math.Max((int)countArgs[1], warnings.Count),
                };
                snapshot.Errors.AddRange(errors);
                snapshot.Warnings.AddRange(warnings);
                return true;
            }
            catch (Exception)
            {
                snapshot = null;
                return false;
            }
            finally
            {
                if (started)
                {
                    try
                    {
                        s_EndGettingEntries.Invoke(null, null);
                    }
                    catch (Exception)
                    {
                        // The snapshot, if any, is still valid.
                    }
                }

                if (filterChanged)
                {
                    try
                    {
                        s_SetFilteringText.Invoke(null, new object[] { savedFilter });
                    }
                    catch (Exception)
                    {
                        // The console read itself already succeeded or failed.
                    }
                }

                if (flagsChanged)
                {
                    try
                    {
                        s_ConsoleFlags.SetValue(null, savedFlags);
                    }
                    catch (Exception)
                    {
                        // The console read itself already succeeded or failed.
                    }
                }
            }
        }

        static void AddEntry(object entry, List<string> destination, HashSet<string> seen)
        {
            string formatted = FormatEntry(entry);
            if (!string.IsNullOrEmpty(formatted) && seen.Add(formatted))
            {
                destination.Add(formatted);
            }
        }

        static string FormatEntry(object entry)
        {
            string message = s_MessageField.GetValue(entry) as string ?? string.Empty;
            string file = s_FileField.GetValue(entry) as string ?? string.Empty;
            int line = s_LineField.GetValue(entry) is int value ? value : 0;
            string body = TrimMessage(message);
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            string relative = ProjectRelativePath(file);
            if (!string.IsNullOrEmpty(relative) &&
                body.IndexOf(relative, StringComparison.OrdinalIgnoreCase) < 0)
            {
                body += $" ({relative}:{line})";
            }

            return body;
        }

        static string ProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            path = path.Replace('\\', '/');
            string root = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(root) &&
                path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(root.Length + 1);
            }

            return path;
        }

        static string TrimMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string[] lines = message.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');
            var kept = new List<string>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    if (kept.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (kept.Count > 0 && IsStackLine(line))
                {
                    break;
                }

                kept.Add(line);
                if (kept.Count >= 3)
                {
                    break;
                }
            }

            string body = string.Join(" ", kept);
            if (body.Length > MaxMessageLength)
            {
                body = body.Substring(0, MaxMessageLength - 1) + "…";
            }

            return body.Replace("</unity-context>", "</ unity-context>");
        }

        static bool IsStackLine(string line)
        {
            return line.StartsWith("at ", StringComparison.Ordinal) ||
                   line.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                   line.StartsWith("UnityEditor.", StringComparison.Ordinal) ||
                   line.Contains(" (at ");
        }

        static bool EnsureConsoleReflection()
        {
            if (s_ConsoleProbed)
            {
                return !s_ConsoleUnavailable;
            }

            s_ConsoleProbed = true;
            try
            {
                s_LogEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor", false);
                s_LogEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor", false);
                if (s_LogEntriesType == null || s_LogEntryType == null)
                {
                    s_ConsoleUnavailable = true;
                    return false;
                }

                const BindingFlags StaticFlags =
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags InstanceFlags =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                s_StartGettingEntries = s_LogEntriesType.GetMethod("StartGettingEntries", StaticFlags);
                s_EndGettingEntries = s_LogEntriesType.GetMethod("EndGettingEntries", StaticFlags);
                s_GetCount = s_LogEntriesType.GetMethod("GetCount", StaticFlags);
                s_GetCountsByType = s_LogEntriesType.GetMethod("GetCountsByType", StaticFlags);
                s_GetEntryInternal = s_LogEntriesType.GetMethod("GetEntryInternal", StaticFlags);
                s_ConsoleFlags = s_LogEntriesType.GetProperty("consoleFlags", StaticFlags);
                s_GetFilteringText = s_LogEntriesType.GetMethod("GetFilteringText", StaticFlags);
                s_SetFilteringText = s_LogEntriesType.GetMethod("SetFilteringText", StaticFlags);
                s_ModeField = s_LogEntryType.GetField("mode", InstanceFlags);
                s_MessageField = s_LogEntryType.GetField("message", InstanceFlags);
                s_FileField = s_LogEntryType.GetField("file", InstanceFlags);
                s_LineField = s_LogEntryType.GetField("line", InstanceFlags);

                s_ConsoleUnavailable =
                    s_StartGettingEntries == null ||
                    s_EndGettingEntries == null ||
                    s_GetCount == null ||
                    s_GetCountsByType == null ||
                    s_GetEntryInternal == null ||
                    s_ModeField == null ||
                    s_MessageField == null ||
                    s_FileField == null ||
                    s_LineField == null;
            }
            catch (Exception)
            {
                s_ConsoleUnavailable = true;
            }

            return !s_ConsoleUnavailable;
        }
    }
}
