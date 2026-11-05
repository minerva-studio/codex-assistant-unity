using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Small rename prompt opened from the conversation context menu.</summary>
    internal sealed class CodexRenameWindow : EditorWindow
    {
        string m_ThreadId;
        string m_CurrentTitle;
        Action<string, string> m_OnSubmit;
        TextField m_Field;

        public static void Open(string threadId, string currentTitle, Action<string, string> onSubmit)
        {
            var window = CreateInstance<CodexRenameWindow>();
            window.titleContent = new GUIContent("Rename conversation");
            window.m_ThreadId = threadId;
            window.m_CurrentTitle = currentTitle;
            window.m_OnSubmit = onSubmit;
            window.minSize = new Vector2(340, 132);
            window.maxSize = new Vector2(520, 180);
            window.ShowUtility();
            window.BuildUi();
            window.Focus();
        }

        void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.paddingLeft = 16;
            rootVisualElement.style.paddingRight = 16;
            rootVisualElement.style.paddingTop = 14;
            rootVisualElement.style.paddingBottom = 14;
            rootVisualElement.style.backgroundColor = CodexPalette.Window;

            var title = new Label("Rename conversation");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = CodexPalette.Text;
            rootVisualElement.Add(title);

            var hint = new Label("A local title for this conversation.");
            hint.style.fontSize = 10;
            hint.style.color = CodexPalette.Muted;
            hint.style.marginTop = 3;
            hint.style.marginBottom = 9;
            rootVisualElement.Add(hint);

            m_Field = new TextField { value = m_CurrentTitle ?? string.Empty };
            m_Field.style.height = 28;
            m_Field.style.backgroundColor = CodexPalette.Pill;
            m_Field.style.borderTopLeftRadius = 7;
            m_Field.style.borderTopRightRadius = 7;
            m_Field.style.borderBottomLeftRadius = 7;
            m_Field.style.borderBottomRightRadius = 7;
            m_Field.textEdition.placeholder = "Conversation title";
            rootVisualElement.Add(m_Field);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 10;
            rootVisualElement.Add(actions);

            var cancel = new Button(Close) { text = "Cancel" };
            StyleSecondaryButton(cancel);
            actions.Add(cancel);

            var save = new Button(Submit) { text = "Save" };
            StylePrimaryButton(save);
            actions.Add(save);

            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            m_Field.schedule.Execute(() => m_Field.Focus());
        }

        void Submit()
        {
            string value = m_Field?.value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            m_OnSubmit?.Invoke(m_ThreadId, value);
            Close();
        }

        static void StylePrimaryButton(Button button)
        {
            button.style.height = 28;
            button.style.minWidth = 70;
            button.style.marginLeft = 6;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.backgroundColor = CodexPalette.SendAction;
            button.style.color = Color.white;
            button.style.fontSize = 10;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        static void StyleSecondaryButton(Button button)
        {
            button.style.height = 28;
            button.style.minWidth = 70;
            button.style.marginLeft = 6;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            button.style.backgroundColor = CodexPalette.Pill;
            button.style.color = CodexPalette.Text;
            button.style.fontSize = 10;
        }
    }
}
