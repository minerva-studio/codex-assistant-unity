using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Renders sample messages that contain an image attachment and audio files.</summary>
    public sealed class CodexAttachmentPreviewWindow : EditorWindow
    {
        [MenuItem("Tools/Minerva/Codex/Attachment Preview")]
        public static void Open()
        {
            var window = GetWindow<CodexAttachmentPreviewWindow>();
            window.titleContent = new GUIContent("Codex Attachments");
            window.minSize = new Vector2(640, 420);
            if (window.position.width < 640 || window.position.height < 420)
            {
                window.position = new Rect(window.position.x, window.position.y, 760, 560);
            }

            window.Show();
        }

        void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 8;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            rootVisualElement.Add(scroll);

            string audio = Path.Combine(Application.dataPath, "Prototype/Samples/beep.wav").Replace('/', '\\');
            string image = Path.Combine(Application.dataPath, "Prototype/Samples/dot.png").Replace('/', '\\');

            string userText =
                $"这是三段试听里的第一段：{audio}\n\n" +
                $"[Attached image \"image.png\" is saved at: {image}]";

            scroll.Add(Render(UserMessage(userText)));

            string agentText =
                "有，订书机那版淘汰。这里换成一段机械录音：\n\n" +
                $"`{audio}`\n\n" +
                "文件检查通过，置信度 99%。";
            scroll.Add(Render(AgentMessage(agentText)));
        }

        static VisualElement Render(JObject item)
        {
            var root = new VisualElement();
            CodexItemRenderer.Render(root, item);
            return root;
        }

        static JObject UserMessage(string text)
        {
            return new JObject
            {
                ["type"] = "userMessage",
                ["content"] = new JArray(new JObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                }),
            };
        }

        static JObject AgentMessage(string text)
        {
            return new JObject
            {
                ["type"] = "agentMessage",
                ["text"] = text,
            };
        }

        public static void DebugSample()
        {
            string audio = Path.Combine(Application.dataPath, "Prototype/Samples/beep.wav").Replace('/', '\\');
            string image = Path.Combine(Application.dataPath, "Prototype/Samples/dot.png").Replace('/', '\\');
            string text = $"这是三段试听里的第一段：{audio}\n\n[Attached image \"image.png\" is saved at: {image}]";

            Debug.Log($"[AttachDebug] raw=<<{text}>>");
            string cleaned = CodexAttachments.Extract(text, out var images, out var audioPaths);
            Debug.Log($"[AttachDebug] cleaned=<<{cleaned}>> images={images.Count} audio={audioPaths.Count}");
        }
    }
}
