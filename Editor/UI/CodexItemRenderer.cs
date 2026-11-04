using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Turns app-server items into UI Toolkit elements.</summary>
    internal static class CodexItemRenderer
    {
        public static void Render(VisualElement parent, JObject item)
        {
            string type = item["type"]?.Value<string>() ?? "unknown";
            switch (type)
            {
                case "userMessage":
                    RenderUserMessage(parent, item);
                    break;
                case "agentMessage":
                    RenderAgentMessage(parent, item);
                    break;
                case "reasoning":
                    RenderReasoning(parent, item);
                    break;
                case "plan":
                    RenderPlan(parent, item);
                    break;
                case "commandExecution":
                    RenderCommand(parent, item);
                    break;
                case "fileChange":
                    RenderFileChange(parent, item);
                    break;
                case "mcpToolCall":
                    RenderMcpTool(parent, item);
                    break;
                case "webSearch":
                    RenderWebSearch(parent, item);
                    break;
                case "enteredReviewMode":
                case "exitedReviewMode":
                    RenderReview(parent, item, type);
                    break;
                case "contextCompaction":
                    RenderMutedLine(parent, "Context compacted");
                    break;
                default:
                    RenderUnknown(parent, item, type);
                    break;
            }
        }

        static void RenderUserMessage(VisualElement parent, JObject item)
        {
            string raw = JoinUserContent(item["content"]);
            string cleaned = CodexAttachments.Extract(raw, out var images, out var audioPaths);

            VisualElement bubble = Bubble("YOU", CodexPalette.UserBubble);
            AddText(bubble, cleaned);
            CodexAttachments.Render(bubble, images, audioPaths);
            parent.Add(bubble);
        }

        static void RenderAgentMessage(VisualElement parent, JObject item)
        {
            string text = item["text"]?.Value<string>();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string cleaned = CodexAttachments.Extract(text, out var images, out var audioPaths);

            VisualElement bubble = Bubble("CODEX", CodexPalette.AgentBubble);
            bubble.Add(CodexMarkdown.Render(cleaned));
            CodexAttachments.Render(bubble, images, audioPaths);
            parent.Add(bubble);
        }

        static void RenderReasoning(VisualElement parent, JObject item)
        {
            string text = JoinText(item["summary"]);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = JoinText(item["content"]);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var foldout = new Foldout { text = "Reasoning", value = false };
            foldout.style.marginLeft = 12;
            foldout.style.marginRight = 12;
            foldout.style.marginBottom = 6;

            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = CodexPalette.Muted;
            foldout.Add(label);
            parent.Add(foldout);
        }

        static void RenderPlan(VisualElement parent, JObject item)
        {
            VisualElement bubble = Bubble("PLAN", CodexPalette.ToolBubble);
            AddText(bubble, item["text"]?.Value<string>());
            parent.Add(bubble);
        }

        static void RenderCommand(VisualElement parent, JObject item)
        {
            string status = item["status"]?.Value<string>() ?? "";
            VisualElement bubble = Bubble($"COMMAND  {status}".Trim(), CodexPalette.ToolBubble);

            string command = item["command"]?.Value<string>() ?? "";
            var commandLabel = new Label(command);
            commandLabel.style.whiteSpace = WhiteSpace.Normal;
            commandLabel.style.color = CodexPalette.Text;
            bubble.Add(commandLabel);

            string output = item["aggregatedOutput"]?.Value<string>();
            if (!string.IsNullOrEmpty(output))
            {
                var foldout = new Foldout { text = "Output", value = false };
                foldout.style.marginTop = 4;
                var outputLabel = new Label(output.TrimEnd());
                outputLabel.style.whiteSpace = WhiteSpace.Normal;
                outputLabel.style.fontSize = 11;
                outputLabel.style.color = CodexPalette.Muted;
                foldout.Add(outputLabel);
                bubble.Add(foldout);
            }

            parent.Add(bubble);
        }

        static void RenderFileChange(VisualElement parent, JObject item)
        {
            VisualElement bubble = Bubble("EDITED", CodexPalette.ToolBubble);
            if (item["changes"] is JArray changes)
            {
                foreach (JToken change in changes)
                {
                    string path = change["path"]?.Value<string>() ?? "(unknown file)";
                    string diff = change["diff"]?.Value<string>();

                    var pathLabel = new Label(path);
                    pathLabel.style.whiteSpace = WhiteSpace.Normal;
                    pathLabel.style.color = CodexPalette.Text;
                    pathLabel.style.marginTop = 2;
                    bubble.Add(pathLabel);

                    if (!string.IsNullOrEmpty(diff))
                    {
                        var foldout = new Foldout { text = "Diff", value = false };
                        var diffLabel = new Label(diff);
                        diffLabel.style.whiteSpace = WhiteSpace.Normal;
                        diffLabel.style.fontSize = 11;
                        diffLabel.style.color = CodexPalette.Muted;
                        foldout.Add(diffLabel);
                        bubble.Add(foldout);
                    }
                }
            }

            parent.Add(bubble);
        }

        static void RenderMcpTool(VisualElement parent, JObject item)
        {
            string server = item["server"]?.Value<string>() ?? "mcp";
            string tool = item["tool"]?.Value<string>() ?? "tool";
            string status = item["status"]?.Value<string>() ?? "";
            VisualElement bubble = Bubble($"MCP  {server}/{tool}  {status}".Trim(), CodexPalette.ToolBubble);

            string error = item["error"]?.Value<string>();
            if (!string.IsNullOrEmpty(error))
            {
                AddText(bubble, error);
            }

            parent.Add(bubble);
        }

        static void RenderWebSearch(VisualElement parent, JObject item)
        {
            string query = item["query"]?.Value<string>() ?? "";
            VisualElement bubble = Bubble("WEB SEARCH", CodexPalette.ToolBubble);
            AddText(bubble, query);
            parent.Add(bubble);
        }

        static void RenderReview(VisualElement parent, JObject item, string type)
        {
            string review = item["review"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(review))
            {
                return;
            }

            VisualElement bubble = Bubble(type == "exitedReviewMode" ? "REVIEW" : "REVIEWING", CodexPalette.ToolBubble);
            AddText(bubble, review);
            parent.Add(bubble);
        }

        static void RenderUnknown(VisualElement parent, JObject item, string type)
        {
            var foldout = new Foldout { text = type, value = false };
            foldout.style.marginLeft = 12;
            foldout.style.marginRight = 12;
            foldout.style.marginBottom = 4;

            var raw = new Label(item.ToString());
            raw.style.whiteSpace = WhiteSpace.Normal;
            raw.style.fontSize = 10;
            raw.style.color = CodexPalette.Muted;
            foldout.Add(raw);
            parent.Add(foldout);
        }

        static void RenderMutedLine(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.color = CodexPalette.Muted;
            label.style.fontSize = 10;
            label.style.marginLeft = 12;
            label.style.marginBottom = 6;
            parent.Add(label);
        }

        static VisualElement Bubble(string role, Color background)
        {
            var bubble = new VisualElement();
            bubble.style.backgroundColor = background;
            bubble.style.borderTopLeftRadius = 6;
            bubble.style.borderTopRightRadius = 6;
            bubble.style.borderBottomLeftRadius = 6;
            bubble.style.borderBottomRightRadius = 6;
            bubble.style.paddingLeft = 10;
            bubble.style.paddingRight = 10;
            bubble.style.paddingTop = 6;
            bubble.style.paddingBottom = 8;
            bubble.style.marginLeft = 12;
            bubble.style.marginRight = 12;
            bubble.style.marginTop = 2;
            bubble.style.marginBottom = 8;

            var caption = new Label(role);
            caption.style.fontSize = 9;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = CodexPalette.Muted;
            caption.style.marginBottom = 3;
            bubble.Add(caption);

            return bubble;
        }

        static void AddText(VisualElement parent, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string normalized = text.Replace("\r\n", "\n");
            string[] segments = normalized.Split(new[] { "```" }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0)
                {
                    continue;
                }

                if (i % 2 == 1)
                {
                    parent.Add(CodeBlock(segment));
                }
                else
                {
                    var label = new Label(segment.TrimEnd());
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.color = CodexPalette.Text;
                    label.style.marginTop = 2;
                    label.style.marginBottom = 2;
                    parent.Add(label);
                }
            }
        }

        static VisualElement CodeBlock(string segment)
        {
            int firstNewline = segment.IndexOf('\n');
            if (firstNewline > 0 && firstNewline < 40)
            {
                string language = segment.Substring(0, firstNewline).Trim();
                if (language.Length > 0 && !language.Contains(" "))
                {
                    segment = segment.Substring(firstNewline + 1);
                }
            }

            var box = new Label(segment.TrimEnd());
            box.style.whiteSpace = WhiteSpace.Normal;
            box.style.backgroundColor = CodexPalette.CodeBackground;
            box.style.color = CodexPalette.Text;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.marginTop = 4;
            box.style.marginBottom = 4;
            return box;
        }

        static string JoinUserContent(JToken content)
        {
            if (content is not JArray parts)
            {
                return content?.Value<string>() ?? string.Empty;
            }

            var builder = new StringBuilder();
            foreach (JToken part in parts)
            {
                string kind = part["type"]?.Value<string>();
                switch (kind)
                {
                    case "text":
                        builder.AppendLine(part["text"]?.Value<string>());
                        break;
                    case "image":
                    case "localImage":
                        builder.AppendLine($"[Attached image \"image\" is saved at: {part["path"] ?? part["url"]}]");
                        break;
                    default:
                        builder.AppendLine($"[{kind}]");
                        break;
                }
            }

            return builder.ToString().TrimEnd();
        }

        static string JoinText(JToken token)
        {
            if (token == null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token is not JArray parts)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (JToken part in parts)
            {
                if (part.Type == JTokenType.String)
                {
                    builder.AppendLine(part.Value<string>());
                }
                else
                {
                    builder.AppendLine(part["text"]?.Value<string>());
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
