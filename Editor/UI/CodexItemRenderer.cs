using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Turns app-server items into UI Toolkit elements.</summary>
    internal static class CodexItemRenderer
    {
        public static void Render(VisualElement parent, JObject item, bool showReasoning, bool showTools)
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
                    if (showReasoning)
                    {
                        RenderReasoning(parent, item);
                    }

                    break;
                case "plan":
                    if (showTools)
                    {
                        RenderPlan(parent, item);
                    }

                    break;
                case "commandExecution":
                    if (showTools)
                    {
                        RenderCommand(parent, item);
                    }

                    break;
                case "fileChange":
                    if (showTools)
                    {
                        RenderFileChange(parent, item);
                    }

                    break;
                case "mcpToolCall":
                    if (showTools)
                    {
                        RenderMcpTool(parent, item);
                    }

                    break;
                case "webSearch":
                    if (showTools)
                    {
                        RenderWebSearch(parent, item);
                    }

                    break;
                case "enteredReviewMode":
                case "exitedReviewMode":
                    if (showTools)
                    {
                        RenderReview(parent, item, type);
                    }

                    break;
                case "contextCompaction":
                    RenderMutedLine(parent, "Context compacted");
                    break;
                default:
                    if (showTools)
                    {
                        RenderUnknown(parent, item, type);
                    }

                    break;
            }
        }

        static void RenderUserMessage(VisualElement parent, JObject item)
        {
            string raw = JoinUserContent(item["content"]);
            VisualElement bubble = Bubble("YOU", CodexPalette.UserBubble);
            bubble.Add(CodexMarkdown.Render(CodexMedia.Tokenize(raw)));
            parent.Add(bubble);
        }

        static void RenderAgentMessage(VisualElement parent, JObject item)
        {
            string text = item["text"]?.Value<string>();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            VisualElement bubble = Bubble("CODEX", CodexPalette.AgentBubble);
            bubble.Add(CodexMarkdown.Render(CodexMedia.Tokenize(text)));
            parent.Add(bubble);
        }

        static void RenderReasoning(VisualElement parent, JObject item)
        {
            string summary = JoinText(item["summary"]);
            string content = JoinText(item["content"]);
            if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            Foldout foldout = Section(parent, "Thinking");
            if (!string.IsNullOrWhiteSpace(summary))
            {
                AddMuted(foldout, summary);
            }

            if (!string.IsNullOrWhiteSpace(content) &&
                !string.Equals(content.Trim(), summary.Trim(), StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    Foldout details = Section(foldout, "Full reasoning");
                    AddMuted(details, content);
                }
                else
                {
                    AddMuted(foldout, content);
                }
            }
        }

        static void RenderPlan(VisualElement parent, JObject item)
        {
            string text = item["text"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Foldout foldout = Section(parent, "Plan");
            AddText(foldout, text);
        }

        static void RenderCommand(VisualElement parent, JObject item)
        {
            string command = item["command"]?.Value<string>() ?? string.Empty;
            string title = string.IsNullOrEmpty(command) ? "Command" : $"$ {OneLine(command, 80)}";
            Foldout foldout = Section(parent, title);
            AddStatus(foldout, item);

            if (!string.IsNullOrWhiteSpace(command))
            {
                AddCode(foldout, command);
            }

            string output = item["aggregatedOutput"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(output))
            {
                Foldout outputSection = Section(foldout, "Output");
                AddMuted(outputSection, output.TrimEnd());
            }
        }

        static void RenderFileChange(VisualElement parent, JObject item)
        {
            JArray changes = item["changes"] as JArray;
            int count = changes?.Count ?? 0;
            string title = count switch
            {
                0 => "File changes",
                1 => "Edited 1 file",
                _ => $"Edited {count} files",
            };

            Foldout foldout = Section(parent, title);
            AddStatus(foldout, item);
            if (changes == null)
            {
                return;
            }

            foreach (JToken change in changes)
            {
                string path = change["path"]?.Value<string>() ?? "(unknown file)";
                var pathLabel = new Label(path);
                pathLabel.style.whiteSpace = WhiteSpace.Normal;
                pathLabel.style.color = CodexPalette.Text;
                pathLabel.style.fontSize = 10;
                pathLabel.style.marginTop = 2;
                foldout.Add(pathLabel);

                string diff = change["diff"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(diff))
                {
                    Foldout diffSection = Section(foldout, "Diff");
                    AddCode(diffSection, diff);
                }
            }
        }

        static void RenderMcpTool(VisualElement parent, JObject item)
        {
            string server = item["server"]?.Value<string>() ?? "mcp";
            string tool = item["tool"]?.Value<string>() ?? "tool";
            Foldout foldout = Section(parent, $"MCP · {server}/{tool}");
            AddStatus(foldout, item);

            JToken arguments = item["arguments"];
            if (arguments != null && arguments.Type != JTokenType.Null)
            {
                Foldout argumentsSection = Section(foldout, "Arguments");
                AddCode(argumentsSection, arguments.ToString(Formatting.Indented));
            }

            string error = item["error"] is JObject errorObject
                ? errorObject["message"]?.Value<string>()
                : item["error"]?.Value<string>();
            if (!string.IsNullOrEmpty(error))
            {
                var errorLabel = AddMuted(foldout, error);
                errorLabel.style.color = CodexPalette.Error;
            }

            JToken result = item["result"];
            if (result != null && result.Type != JTokenType.Null)
            {
                Foldout resultSection = Section(foldout, "Result");
                AddCode(resultSection, result.ToString(Formatting.Indented));
            }
        }

        static void RenderWebSearch(VisualElement parent, JObject item)
        {
            string query = item["query"]?.Value<string>() ?? string.Empty;
            string title = string.IsNullOrEmpty(query) ? "Web search" : $"Web search · {OneLine(query, 60)}";
            Foldout foldout = Section(parent, title);

            if (!string.IsNullOrWhiteSpace(query))
            {
                AddMuted(foldout, query);
            }

            JToken action = item["action"];
            if (action != null && action.Type != JTokenType.Null)
            {
                Foldout actionSection = Section(foldout, "Action");
                AddCode(actionSection, action.ToString(Formatting.Indented));
            }

            if (item["results"] is JArray results && results.Count > 0)
            {
                Foldout resultsSection = Section(foldout, "Results");
                AddCode(resultsSection, results.ToString(Formatting.Indented));
            }
        }

        static void RenderReview(VisualElement parent, JObject item, string type)
        {
            string review = item["review"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(review))
            {
                return;
            }

            Foldout foldout = Section(parent, type == "exitedReviewMode" ? "Review" : "Reviewing");
            AddText(foldout, review);
        }

        static void RenderUnknown(VisualElement parent, JObject item, string type)
        {
            Foldout foldout = Section(parent, type);
            AddCode(foldout, item.ToString(Formatting.Indented));
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

        static Foldout Section(VisualElement parent, string title)
        {
            var foldout = new Foldout
            {
                text = string.IsNullOrEmpty(title) ? "Details" : title,
                value = false,
            };

            foldout.style.marginLeft = 12;
            foldout.style.marginRight = 12;
            foldout.style.marginTop = 2;
            foldout.style.marginBottom = 6;
            foldout.style.color = CodexPalette.Text;
            parent.Add(foldout);
            return foldout;
        }

        static void AddStatus(VisualElement parent, JObject item)
        {
            string status = item["status"]?.Value<string>();
            string text = status;
            if (item["exitCode"] is JValue exitCode && exitCode.Type == JTokenType.Integer)
            {
                text = AppendPart(text, $"exit {exitCode.Value<int>()}");
            }

            if (item["durationMs"] is JValue duration && duration.Type == JTokenType.Integer)
            {
                text = AppendPart(text, $"{duration.Value<long>()} ms");
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.color = StatusColor(status);
            label.style.marginBottom = 4;
            parent.Add(label);
        }

        static string AppendPart(string text, string part)
        {
            return string.IsNullOrEmpty(text) ? part : $"{text} · {part}";
        }

        static Color StatusColor(string status)
        {
            return status switch
            {
                "completed" => CodexPalette.Success,
                "failed" => CodexPalette.Error,
                "declined" or "interrupted" => CodexPalette.Warning,
                _ => CodexPalette.Muted,
            };
        }

        static Label AddMuted(VisualElement parent, string text)
        {
            var label = new Label(text.TrimEnd());
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = CodexPalette.Muted;
            label.style.marginTop = 2;
            label.style.marginBottom = 2;
            parent.Add(label);
            return label;
        }

        static void AddCode(VisualElement parent, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var label = new Label(text.TrimEnd());
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.backgroundColor = CodexPalette.CodeBackground;
            label.style.color = CodexPalette.Text;
            label.style.fontSize = 10;
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.paddingTop = 6;
            label.style.paddingBottom = 6;
            label.style.marginTop = 4;
            label.style.marginBottom = 4;
            parent.Add(label);
        }

        static string OneLine(string text, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
            return singleLine.Length <= length ? singleLine : singleLine.Substring(0, length) + "...";
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
