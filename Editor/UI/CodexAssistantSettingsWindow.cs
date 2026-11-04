using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Preferences for the assistant transcript and composer.</summary>
    internal sealed class CodexAssistantSettingsWindow : EditorWindow
    {
        Toggle m_ContextToggle;
        Toggle m_ReasoningToggle;
        Toggle m_ToolsToggle;
        Toggle m_AutoTitleToggle;
        TextField m_AutoTitleModelField;
        TextField m_CodexHomeField;
        Label m_CodexHomeStatus;

        [MenuItem("Window/Minerva/Codex Assistant Settings", priority = 2)]
        public static void Open()
        {
            var window = GetWindow<CodexAssistantSettingsWindow>();
            window.titleContent = new GUIContent("Codex Settings");
            window.minSize = new Vector2(380, 580);
            if (window.position.width < 380 || window.position.height < 580)
            {
                window.position = new Rect(window.position.x, window.position.y, 480, 700);
            }

            window.Show();
        }

        void OnEnable()
        {
            BuildUi();
        }

        void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 16;
            rootVisualElement.style.paddingRight = 16;
            rootVisualElement.style.paddingTop = 14;
            rootVisualElement.style.paddingBottom = 14;
            rootVisualElement.style.backgroundColor = CodexPalette.Window;

            var title = new Label("Codex Assistant");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            rootVisualElement.Add(title);

            var subtitle = new Label("Conversation display and composer options.");
            subtitle.style.fontSize = 10;
            subtitle.style.color = CodexPalette.Muted;
            subtitle.style.marginTop = 2;
            subtitle.style.marginBottom = 10;
            rootVisualElement.Add(subtitle);

            var card = new VisualElement();
            card.style.backgroundColor = CodexPalette.ComposerCard;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = CodexPalette.PillBorder;
            card.style.borderBottomColor = CodexPalette.PillBorder;
            card.style.borderLeftColor = CodexPalette.PillBorder;
            card.style.borderRightColor = CodexPalette.PillBorder;
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            rootVisualElement.Add(card);

            var transcript = SectionLabel("Transcript");
            card.Add(transcript);

            m_ReasoningToggle = AddToggle(
                card,
                "Show reasoning",
                "Show thinking summaries and reasoning details in the transcript.",
                CodexPreferences.ShowReasoning,
                value => CodexPreferences.ShowReasoning = value);

            m_ToolsToggle = AddToggle(
                card,
                "Show tool calls",
                "Show commands, file edits, MCP calls and web searches in the transcript.",
                CodexPreferences.ShowToolCalls,
                value => CodexPreferences.ShowToolCalls = value);

            var composer = SectionLabel("Composer");
            composer.style.marginTop = 14;
            card.Add(composer);

            m_ContextToggle = AddToggle(
                card,
                "Context window indicator",
                "Show context usage next to the model. The indicator fills in after a turn reports token usage.",
                CodexPreferences.ShowContextIndicator,
                value => CodexPreferences.ShowContextIndicator = value);

            var titles = SectionLabel("Conversation titles");
            titles.style.marginTop = 14;
            card.Add(titles);

            m_AutoTitleToggle = AddToggle(
                card,
                "Generate titles automatically",
                "After the first turn of a new conversation, run a small title turn and set the thread name.",
                CodexPreferences.AutoTitle,
                value => CodexPreferences.AutoTitle = value);

            m_AutoTitleModelField = new TextField { name = "auto-title-model" };
            m_AutoTitleModelField.style.alignSelf = Align.Stretch;
            m_AutoTitleModelField.style.height = 24;
            m_AutoTitleModelField.SetValueWithoutNotify(CodexPreferences.AutoTitleModel);
            m_AutoTitleModelField.textEdition.placeholder = "Conversation model (optional)";
            m_AutoTitleModelField.textEdition.hidePlaceholderOnFocus = false;
            m_AutoTitleModelField.RegisterValueChangedCallback(
                evt => CodexPreferences.AutoTitleModel = evt.newValue);
            card.Add(m_AutoTitleModelField);

            var titleHelp = new Label(
                "Optional model id for the title turn. Leave empty to use the conversation's model. " +
                "Title generation sends the first user/assistant exchange to that model.");
            titleHelp.style.fontSize = 10;
            titleHelp.style.color = CodexPalette.Muted;
            titleHelp.style.whiteSpace = WhiteSpace.Normal;
            titleHelp.style.marginTop = 4;
            titleHelp.style.marginBottom = 8;
            card.Add(titleHelp);

            var backend = SectionLabel("Backend");
            backend.style.marginTop = 14;
            card.Add(backend);

            m_CodexHomeField = new TextField { name = "codex-home" };
            m_CodexHomeField.style.alignSelf = Align.Stretch;
            m_CodexHomeField.style.height = 24;
            m_CodexHomeField.SetValueWithoutNotify(CodexPreferences.CodexHome);
            m_CodexHomeField.textEdition.placeholder = "Default (~/.codex)";
            m_CodexHomeField.textEdition.hidePlaceholderOnFocus = false;
            card.Add(m_CodexHomeField);

            var homeActions = new VisualElement();
            homeActions.style.flexDirection = FlexDirection.Row;
            homeActions.style.justifyContent = Justify.FlexEnd;
            homeActions.style.marginTop = 6;
            card.Add(homeActions);

            var browse = new Button(BrowseCodexHome) { text = "Browse..." };
            browse.style.flexShrink = 0;
            browse.style.marginRight = 6;
            homeActions.Add(browse);

            var reconnect = new Button(ReconnectBackend) { text = "Reconnect" };
            reconnect.style.flexShrink = 0;
            reconnect.style.marginRight = 6;
            reconnect.tooltip = "Restart app-server and reload config, providers and models";
            homeActions.Add(reconnect);

            var apply = new Button(ApplyCodexHome) { text = "Apply" };
            apply.style.flexShrink = 0;
            homeActions.Add(apply);

            m_CodexHomeStatus = new Label(
                $"CODEX_HOME used by app-server. Leave empty for the default ({DefaultCodexHome()}). " +
                "Applying restarts the connection; a missing folder can be created.");
            m_CodexHomeStatus.style.fontSize = 10;
            m_CodexHomeStatus.style.color = CodexPalette.Muted;
            m_CodexHomeStatus.style.whiteSpace = WhiteSpace.Normal;
            m_CodexHomeStatus.style.marginTop = 4;
            m_CodexHomeStatus.style.marginBottom = 4;
            card.Add(m_CodexHomeStatus);

            m_CodexHomeField.RegisterValueChangedCallback(_ =>
            {
                m_CodexHomeStatus.text = "Click Apply to use this Codex home.";
                m_CodexHomeStatus.style.color = CodexPalette.Muted;
            });

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 12;
            rootVisualElement.Add(buttons);

            var reset = new Button(ResetToDefaults) { text = "Reset to defaults" };
            reset.style.marginRight = 8;
            buttons.Add(reset);

            var done = new Button(Close) { text = "Done" };
            buttons.Add(done);
        }

        static Label SectionLabel(string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.style.fontSize = 9;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = CodexPalette.Muted;
            label.style.marginBottom = 6;
            return label;
        }

        static Toggle AddToggle(
            VisualElement parent,
            string label,
            string help,
            bool value,
            Action<bool> setter)
        {
            var toggle = new Toggle(label);
            toggle.value = value;
            toggle.style.color = CodexPalette.Text;
            toggle.style.marginBottom = 2;
            toggle.RegisterValueChangedCallback(evt => setter(evt.newValue));
            parent.Add(toggle);

            var description = new Label(help);
            description.style.fontSize = 10;
            description.style.color = CodexPalette.Muted;
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginLeft = 16;
            description.style.marginBottom = 10;
            parent.Add(description);

            return toggle;
        }

        void ResetToDefaults()
        {
            CodexPreferences.ResetToDefaults();
            m_ContextToggle.SetValueWithoutNotify(CodexPreferences.ShowContextIndicator);
            m_ReasoningToggle.SetValueWithoutNotify(CodexPreferences.ShowReasoning);
            m_ToolsToggle.SetValueWithoutNotify(CodexPreferences.ShowToolCalls);
            m_AutoTitleToggle.SetValueWithoutNotify(CodexPreferences.AutoTitle);
            m_AutoTitleModelField.SetValueWithoutNotify(CodexPreferences.AutoTitleModel);
            m_CodexHomeField.SetValueWithoutNotify(CodexPreferences.CodexHome);
            m_CodexHomeStatus.text = string.IsNullOrEmpty(CodexPreferences.CodexHome)
                ? "Using the default Codex home."
                : "Using a custom Codex home.";
            m_CodexHomeStatus.style.color = CodexPalette.Muted;
        }

        void BrowseCodexHome()
        {
            string current = ExpandCodexHome(m_CodexHomeField.value);
            if (string.IsNullOrEmpty(current))
            {
                current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string picked = EditorUtility.OpenFolderPanel("Select CODEX_HOME", current, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            m_CodexHomeField.SetValueWithoutNotify(picked);
            ApplyCodexHome();
        }

        void ApplyCodexHome()
        {
            string value = ExpandCodexHome(m_CodexHomeField.value);
            if (!string.IsNullOrEmpty(value) && !Directory.Exists(value))
            {
                if (Application.isBatchMode ||
                    !EditorUtility.DisplayDialog(
                        "Create CODEX_HOME?",
                        $"The folder\n{value}\ndoes not exist. Create it?",
                        "Create",
                        "Cancel"))
                {
                    m_CodexHomeStatus.text = "That folder does not exist. Pick or create a CODEX_HOME.";
                    m_CodexHomeStatus.style.color = CodexPalette.Error;
                    return;
                }

                try
                {
                    Directory.CreateDirectory(value);
                }
                catch (Exception exception)
                {
                    m_CodexHomeStatus.text = $"Could not create the folder: {exception.Message}";
                    m_CodexHomeStatus.style.color = CodexPalette.Error;
                    return;
                }
            }

            bool changed = CodexPreferences.CodexHome != value;
            CodexPreferences.CodexHome = value;
            if (!changed)
            {
                // The path is the same, but config.toml/models.json may have changed.
                CodexPreferences.RequestReload();
            }

            m_CodexHomeField.SetValueWithoutNotify(value);
            m_CodexHomeStatus.text = string.IsNullOrEmpty(value)
                ? "Using the default Codex home."
                : "Applied. The assistant reconnects with this Codex home.";
            m_CodexHomeStatus.style.color = CodexPalette.Success;
        }

        void ReconnectBackend()
        {
            CodexPreferences.RequestReload();
            m_CodexHomeStatus.text = "Reconnect requested. Config, providers and models reload.";
            m_CodexHomeStatus.style.color = CodexPalette.Success;
        }

        static string DefaultCodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(configured) ? "~/.codex" : configured;
        }

        static string ExpandCodexHome(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string value = Environment.ExpandEnvironmentVariables(raw.Trim());
            if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal) ||
                value.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                value = value.Length <= 2 ? home : Path.Combine(home, value.Substring(2));
            }

            if (!Path.IsPathRooted(value))
            {
                value = Path.GetFullPath(value);
            }

            return value;
        }
    }
}
