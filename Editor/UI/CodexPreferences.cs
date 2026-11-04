using System;
using UnityEditor;

namespace Minerva.Codex
{
    /// <summary>User preferences shared by the assistant window and the settings panel.</summary>
    internal static class CodexPreferences
    {
        const string Prefix = "Minerva.Codex.";
        const string ContextIndicatorKey = Prefix + "ShowContextIndicator";
        const string ReasoningKey = Prefix + "ShowReasoning";
        const string ToolCallsKey = Prefix + "ShowToolCalls";
        const string CodexHomeKey = Prefix + "CodexHome";
        const string AutoTitleKey = Prefix + "AutoTitle";
        const string AutoTitleModelKey = Prefix + "AutoTitleModel";

        public static event Action Changed;
        public static event Action ReloadRequested;

        public static bool ShowContextIndicator
        {
            get => EditorPrefs.GetBool(ContextIndicatorKey, false);
            set => SetBool(ContextIndicatorKey, false, value);
        }

        public static bool ShowReasoning
        {
            get => EditorPrefs.GetBool(ReasoningKey, true);
            set => SetBool(ReasoningKey, true, value);
        }

        public static bool ShowToolCalls
        {
            get => EditorPrefs.GetBool(ToolCallsKey, true);
            set => SetBool(ToolCallsKey, true, value);
        }

        /// <summary>Empty uses the process default (~/.codex).</summary>
        public static string CodexHome
        {
            get => EditorPrefs.GetString(CodexHomeKey, string.Empty);
            set => SetString(CodexHomeKey, value);
        }

        /// <summary>Generate a short title after the first turn of a new conversation.</summary>
        public static bool AutoTitle
        {
            get => EditorPrefs.GetBool(AutoTitleKey, true);
            set => SetBool(AutoTitleKey, true, value);
        }

        /// <summary>Empty uses the conversation's model.</summary>
        public static string AutoTitleModel
        {
            get => EditorPrefs.GetString(AutoTitleModelKey, string.Empty);
            set => SetString(AutoTitleModelKey, value);
        }

        /// <summary>Restart app-server when config files changed without a home change.</summary>
        public static void RequestReload()
        {
            ReloadRequested?.Invoke();
        }

        public static void ResetToDefaults()
        {
            EditorPrefs.SetBool(ContextIndicatorKey, false);
            EditorPrefs.SetBool(ReasoningKey, true);
            EditorPrefs.SetBool(ToolCallsKey, true);
            EditorPrefs.DeleteKey(CodexHomeKey);
            EditorPrefs.SetBool(AutoTitleKey, true);
            EditorPrefs.DeleteKey(AutoTitleModelKey);
            Changed?.Invoke();
        }

        static void SetString(string key, string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (EditorPrefs.GetString(key, string.Empty) == normalized)
            {
                return;
            }

            if (normalized.Length == 0)
            {
                EditorPrefs.DeleteKey(key);
            }
            else
            {
                EditorPrefs.SetString(key, normalized);
            }

            Changed?.Invoke();
        }

        static void SetBool(string key, bool defaultValue, bool value)
        {
            if (EditorPrefs.GetBool(key, defaultValue) == value)
            {
                return;
            }

            EditorPrefs.SetBool(key, value);
            Changed?.Invoke();
        }
    }
}
