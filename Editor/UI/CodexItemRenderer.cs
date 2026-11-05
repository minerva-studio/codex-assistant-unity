using System;
using System.Collections.Generic;
using System.Linq;
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
        public static void Render(
            VisualElement parent,
            JObject item,
            bool showReasoning,
            bool showTools,
            Action<string> openThread = null,
            Action<string, string> sendTerminalInput = null,
            Action<string> copyMessage = null,
            Action<string> forkFromTurn = null,
            string turnId = null,
            long turnTimestampMs = 0,
            bool canFork = false,
            string linkRoot = null)
        {
            string type = item["type"]?.Value<string>() ?? "unknown";
            switch (type)
            {
                case "userMessage":
                    RenderUserMessage(
                        parent,
                        item,
                        turnId,
                        copyMessage,
                        forkFromTurn,
                        turnTimestampMs,
                        canFork,
                        linkRoot);
                    break;
                case "agentMessage":
                    RenderAgentMessage(
                        parent,
                        item,
                        turnId,
                        copyMessage,
                        forkFromTurn,
                        turnTimestampMs,
                        canFork,
                        linkRoot);
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
                case "turnPlan":
                    if (showTools)
                    {
                        RenderTurnPlan(parent, item);
                    }
                    break;
                case "turnDiff":
                    if (showTools)
                    {
                        RenderTurnDiff(parent, item);
                    }
                    break;
                case "commandExecution":
                    if (showTools)
                    {
                        RenderCommand(parent, item, sendTerminalInput);
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
                case "imageView":
                    if (showTools)
                    {
                        RenderImageView(parent, item);
                    }

                    break;
                case "collabAgentToolCall":
                    if (showTools)
                    {
                        RenderCollabAgent(parent, item, openThread);
                    }

                    break;
                case "subAgentActivity":
                    if (showTools)
                    {
                        RenderSubAgentActivity(parent, item, openThread);
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

        static void RenderUserMessage(
            VisualElement parent,
            JObject item,
            string turnId,
            Action<string> copyMessage,
            Action<string> forkFromTurn,
            long turnTimestampMs,
            bool canFork,
            string linkRoot)
        {
            long timestamp = item["startedAtMs"]?.Value<long?>() ??
                             item["completedAtMs"]?.Value<long?>() ??
                             turnTimestampMs;
            string raw = JoinUserContent(item["content"]);
            if (CodexQuestionReply.TryParse(raw, out List<CodexQuestionReply> replies, out string remainder))
            {
                foreach (CodexQuestionReply reply in replies)
                {
                    RenderQuestionReply(parent, reply, linkRoot);
                }

                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    RenderUserBubble(
                        parent,
                        CodexAnnotations.Clean(remainder),
                        turnId,
                        copyMessage,
                        forkFromTurn,
                        timestamp,
                        canFork,
                        linkRoot);
                }

                return;
            }

            RenderUserBubble(
                parent,
                CodexAnnotations.Clean(raw),
                turnId,
                copyMessage,
                forkFromTurn,
                timestamp,
                canFork,
                linkRoot);
        }

        static void RenderUserBubble(
            VisualElement parent,
            string text,
            string turnId,
            Action<string> copyMessage,
            Action<string> forkFromTurn,
            long timestampMs,
            bool canFork,
            string linkRoot)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;
            VisualElement bubble = Bubble(CodexPalette.UserBubble);
            VisualElement content = CodexMarkdown.Render(CodexMedia.Tokenize(text), linkRoot);
            bubble.Add(content);
            wrapper.Add(bubble);
            AddMessageFooter(
                wrapper,
                bubble,
                content,
                text,
                turnId,
                timestampMs,
                copyMessage,
                forkFromTurn,
                true,
                canFork);
            parent.Add(wrapper);
        }

        static void RenderQuestionReply(VisualElement parent, CodexQuestionReply reply, string linkRoot)
        {
            var card = new VisualElement();
            card.style.backgroundColor = CodexPalette.AgentBubble;
            card.style.borderLeftWidth = 2;
            card.style.borderLeftColor = CodexPalette.Warning;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.paddingTop = 7;
            card.style.paddingBottom = 8;
            card.style.marginLeft = 12;
            card.style.marginRight = 12;
            card.style.marginTop = 2;
            card.style.marginBottom = 8;

            var questionCaption = new Label("CODEX ASKED");
            questionCaption.style.fontSize = 9;
            questionCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            questionCaption.style.color = CodexPalette.Warning;
            questionCaption.style.marginBottom = 3;
            card.Add(questionCaption);
            card.Add(CodexMarkdown.Render(CodexMedia.Tokenize(reply.Question), linkRoot));

            var answerCaption = new Label("YOU ANSWERED");
            answerCaption.style.fontSize = 9;
            answerCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            answerCaption.style.color = CodexPalette.Muted;
            answerCaption.style.marginTop = 7;
            answerCaption.style.marginBottom = 3;
            card.Add(answerCaption);
            card.Add(CodexMarkdown.Render(CodexMedia.Tokenize(reply.Answer), linkRoot));

            parent.Add(card);
        }

        static void RenderAgentMessage(
            VisualElement parent,
            JObject item,
            string turnId,
            Action<string> copyMessage,
            Action<string> forkFromTurn,
            long turnTimestampMs,
            bool canFork,
            string linkRoot)
        {
            string text = item["text"]?.Value<string>();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            long timestamp = item["completedAtMs"]?.Value<long?>() ??
                             item["startedAtMs"]?.Value<long?>() ??
                             turnTimestampMs;

            // Codex output is plain document text; only user input uses a bubble.
            var block = new VisualElement();
            block.style.marginLeft = 18;
            block.style.marginRight = 18;
            block.style.marginTop = 4;
            block.style.marginBottom = 12;
            block.style.paddingLeft = 2;
            block.style.paddingRight = 2;

            var caption = new Label("CODEX");
            caption.style.fontSize = 8;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = CodexPalette.TextDim;
            caption.style.marginBottom = 5;
            block.Add(caption);
            VisualElement content = CodexMarkdown.Render(CodexMedia.Tokenize(text), linkRoot);
            block.Add(content);
            AddMessageFooter(
                block,
                block,
                content,
                text,
                turnId,
                timestamp,
                copyMessage,
                forkFromTurn,
                false,
                canFork);
            parent.Add(block);
        }

        static void AddMessageFooter(
            VisualElement container,
            VisualElement host,
            VisualElement richContent,
            string text,
            string turnId,
            long timestampMs,
            Action<string> copyMessage,
            Action<string> forkFromTurn,
            bool userMessage,
            bool canFork)
        {
            if (copyMessage == null && forkFromTurn == null && string.IsNullOrEmpty(text))
            {
                return;
            }

            var footer = new VisualElement { name = "codex-message-footer" };
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.marginTop = 6;
            if (userMessage)
            {
                footer.style.alignSelf = Align.FlexEnd;
                footer.style.marginRight = 18;
            }
            else
            {
                footer.style.alignSelf = Align.FlexStart;
            }

            container.Add(footer);

            TextField selectable = null;
            bool selecting = false;
            Button selectButton = null;

            void StyleSelectable()
            {
                if (selectable == null)
                {
                    return;
                }

                selectable.style.backgroundColor = Color.clear;
                selectable.style.borderTopWidth = 0;
                selectable.style.borderBottomWidth = 0;
                selectable.style.borderLeftWidth = 0;
                selectable.style.borderRightWidth = 0;
                selectable.style.color = CodexPalette.Text;
                selectable.style.fontSize = 11;
                VisualElement input = selectable.Q(className: "unity-base-text-field__input")
                                      ?? selectable.Q(className: "unity-text-input");
                if (input != null)
                {
                    input.style.backgroundColor = Color.clear;
                    input.style.color = CodexPalette.Text;
                    input.style.borderTopWidth = 0;
                    input.style.borderBottomWidth = 0;
                    input.style.borderLeftWidth = 0;
                    input.style.borderRightWidth = 0;
                }
            }

            void ToggleSelect()
            {
                selecting = !selecting;
                if (selecting)
                {
                    if (selectable == null)
                    {
                        selectable = new TextField
                        {
                            name = "codex-message-select",
                            multiline = true,
                            isReadOnly = true,
                            value = text,
                        };
                        selectable.style.minHeight = 36;
                        selectable.style.maxHeight = 360;
                        selectable.style.marginTop = 4;
                        selectable.RegisterCallback<AttachToPanelEvent>(_ => StyleSelectable());
                        selectable.schedule.Execute(StyleSelectable);
                        host.Insert(host.IndexOf(richContent) + 1, selectable);
                    }

                    richContent.style.display = DisplayStyle.None;
                    selectable.style.display = DisplayStyle.Flex;
                    selectButton.text = "Done";
                    selectButton.tooltip = "Return to formatted text";
                    selectable.schedule.Execute(() => selectable.Focus());
                }
                else
                {
                    if (selectable != null)
                    {
                        selectable.style.display = DisplayStyle.None;
                    }

                    richContent.style.display = DisplayStyle.Flex;
                    selectButton.text = "Select";
                    selectButton.tooltip = "Show selectable message text";
                }
            }

            selectButton = FooterButton("Select", ToggleSelect);
            selectButton.name = "codex-select-message-button";
            selectButton.tooltip = "Show selectable message text";
            footer.Add(selectButton);

            if (copyMessage != null)
            {
                var copy = FooterButton("Copy", () => copyMessage(text));
                copy.tooltip = "Copy this message";
                footer.Add(copy);
            }

            if (canFork && forkFromTurn != null && !string.IsNullOrEmpty(turnId))
            {
                var fork = FooterButton("Fork", () => forkFromTurn(turnId));
                fork.tooltip = "Fork after this completed turn";
                footer.Add(fork);
            }

            string time = FormatMessageTime(timestampMs);
            if (!string.IsNullOrEmpty(time))
            {
                var timestamp = new Label(time);
                timestamp.style.fontSize = 9;
                timestamp.style.color = CodexPalette.TextDim;
                timestamp.style.marginLeft = 6;
                footer.Add(timestamp);
            }
        }

        static Button FooterButton(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 20;
            button.style.minWidth = 0;
            button.style.marginLeft = 0;
            button.style.marginRight = 1;
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopLeftRadius = 4;
            button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4;
            button.style.borderBottomRightRadius = 4;
            button.style.backgroundColor = Color.clear;
            button.style.color = CodexPalette.TextDim;
            button.style.fontSize = 9;
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                button.style.backgroundColor = CodexPalette.Hover;
                button.style.color = CodexPalette.Text;
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = Color.clear;
                button.style.color = CodexPalette.TextDim;
            });
            return button;
        }

        static string FormatMessageTime(long timestampMs)
        {
            if (timestampMs <= 0)
            {
                return string.Empty;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestampMs)
                .ToLocalTime()
                .ToString("h:mm tt");
        }

        internal static bool HasReasoningText(JObject item)
        {
            ReadReasoning(item, out string summary, out string content);
            return !string.IsNullOrEmpty(summary) || !string.IsNullOrEmpty(content);
        }

        static void ReadReasoning(JObject item, out string summary, out string content)
        {
            summary = JoinText(item["summary"]).Trim();
            content = JoinText(item["content"]).Trim();

            // Some providers put the only reasoning text in `text` (or leave the
            // summary/content arrays empty when the chain is not exposed).
            if (summary.Length == 0 && content.Length == 0)
            {
                content = JoinText(item["text"]).Trim();
            }
        }

        static void RenderReasoning(VisualElement parent, JObject item)
        {
            ReadReasoning(item, out string summary, out string content);
            if (string.IsNullOrEmpty(summary) && string.IsNullOrEmpty(content))
            {
                return;
            }

            if (string.Equals(summary, content, StringComparison.Ordinal))
            {
                content = string.Empty;
            }

            string primary = !string.IsNullOrEmpty(summary) ? summary : content;
            string details = !string.IsNullOrEmpty(summary) && !string.IsNullOrEmpty(content)
                ? content
                : string.Empty;

            // A long chain of thought must not be sprayed into the transcript.
            // Keep a short summary visible; fold the detail under the same block.
            if (IsLongReasoning(primary))
            {
                details = string.IsNullOrEmpty(details) ? primary : $"{primary}\n\n{details}";
                primary = null;
            }

            // The surrounding Process group already owns collapsing. This block
            // gives the remaining thinking the same inset as the tool sections.
            var block = new VisualElement();
            block.style.marginLeft = 12;
            block.style.marginRight = 12;
            block.style.marginTop = 2;
            block.style.marginBottom = 6;
            block.style.paddingLeft = 10;
            block.style.paddingRight = 8;
            block.style.paddingTop = 4;
            block.style.paddingBottom = 4;
            block.style.borderLeftWidth = 2;
            block.style.borderLeftColor = CodexPalette.Border;
            parent.Add(block);

            var caption = new Label("Thinking");
            caption.style.fontSize = 9;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = CodexPalette.Muted;
            caption.style.marginBottom = 2;
            block.Add(caption);

            if (!string.IsNullOrEmpty(primary))
            {
                block.Add(CodexMarkdown.Render(primary));
            }

            if (string.IsNullOrEmpty(details))
            {
                return;
            }

            if (!IsLongReasoning(details))
            {
                var detailsLabel = new Label("Full reasoning");
                detailsLabel.style.fontSize = 9;
                detailsLabel.style.color = CodexPalette.Muted;
                detailsLabel.style.marginTop = 4;
                detailsLabel.style.marginBottom = 2;
                block.Add(detailsLabel);
                block.Add(CodexMarkdown.Render(details));
                return;
            }

            var foldout = new Foldout
            {
                text = "Full reasoning",
                value = false,
            };

            foldout.style.marginLeft = 0;
            foldout.style.marginRight = 0;
            foldout.style.marginTop = 2;
            foldout.style.marginBottom = 0;
            foldout.style.color = CodexPalette.Muted;
            foldout.Add(CodexMarkdown.Render(details));
            block.Add(foldout);
        }

        static bool IsLongReasoning(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int lines = 1;
            foreach (char character in text)
            {
                if (character == '\n')
                {
                    lines++;
                }
            }

            return text.Length > 280 || lines > 4;
        }

        static void RenderImageView(VisualElement parent, JObject item)
        {
            string path = item["path"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            VisualElement image = CodexMedia.RenderImage(path);
            if (image == null)
            {
                return;
            }

            image.tooltip = path;
            image.style.marginLeft = 4;
            image.style.marginRight = 4;
            image.style.marginTop = 6;
            image.style.marginBottom = 6;
            parent.Add(image);
        }

        static void RenderCollabAgent(VisualElement parent, JObject item, Action<string> openThread)
        {
            string tool = item["tool"]?.Value<string>() ?? "subagent";
            Foldout foldout = Section(parent, $"Subagent · {tool}");
            AddStatus(foldout, item);

            string prompt = item["prompt"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                if (prompt.Length > 320)
                {
                    Foldout promptFoldout = Section(foldout, "Prompt");
                    AddMuted(promptFoldout, prompt);
                }
                else
                {
                    AddMuted(foldout, prompt);
                }
            }

            string model = item["model"]?.Value<string>();
            string effort = item["reasoningEffort"]?.Value<string>();
            string modelLine = string.Join(" · ", new[] { model, effort }.Where(value => !string.IsNullOrEmpty(value)));
            if (!string.IsNullOrEmpty(modelLine))
            {
                AddMuted(foldout, modelLine);
            }

            var seen = new HashSet<string>();
            if (item["agentsStates"] is JObject states)
            {
                foreach (JProperty state in states.Properties())
                {
                    seen.Add(state.Name);
                    AddSubagentRow(
                        foldout,
                        state.Name,
                        state.Value?["status"]?.Value<string>(),
                        state.Value?["message"]?.Value<string>(),
                        openThread);
                }
            }

            if (item["receiverThreadIds"] is JArray receivers)
            {
                foreach (JToken receiver in receivers)
                {
                    string id = receiver.Value<string>();
                    if (!string.IsNullOrEmpty(id) && seen.Add(id))
                    {
                        AddSubagentRow(foldout, id, null, null, openThread);
                    }
                }
            }
        }

        static void RenderSubAgentActivity(VisualElement parent, JObject item, Action<string> openThread)
        {
            string kind = item["kind"]?.Value<string>() ?? "activity";
            string threadId = item["agentThreadId"]?.Value<string>();
            string path = item["agentPath"]?.Value<string>();
            string model = item["model"]?.Value<string>();
            string effort = item["reasoningEffort"]?.Value<string>();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginLeft = 12;
            row.style.marginRight = 12;
            row.style.marginTop = 2;
            row.style.marginBottom = 4;

            var label = new Label(
                $"Subagent {kind} · {(string.IsNullOrEmpty(path) ? ShortThreadId(threadId) : path)}");
            label.style.flexGrow = 1;
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = StatusColor(kind);
            string details = string.Join(" · ", new[] { model, effort }.Where(value => !string.IsNullOrEmpty(value)));
            if (!string.IsNullOrEmpty(details))
            {
                label.tooltip = details;
            }

            row.Add(label);
            AddOpenButton(row, threadId, openThread);
            parent.Add(row);
        }

        static void AddSubagentRow(
            VisualElement parent,
            string threadId,
            string status,
            string message,
            Action<string> openThread)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            row.style.marginLeft = 12;
            row.style.marginRight = 8;

            var label = new Label(
                $"Subagent {ShortThreadId(threadId)}" +
                (string.IsNullOrEmpty(status) ? string.Empty : $" · {status}"));
            label.style.flexGrow = 1;
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = string.IsNullOrEmpty(status) ? CodexPalette.Muted : StatusColor(status);
            label.tooltip = string.IsNullOrEmpty(message) ? threadId : $"{threadId}\n{message}";
            row.Add(label);
            AddOpenButton(row, threadId, openThread);
            parent.Add(row);
        }

        static void AddOpenButton(VisualElement row, string threadId, Action<string> openThread)
        {
            if (openThread == null || string.IsNullOrEmpty(threadId))
            {
                return;
            }

            var open = new Button(() => openThread(threadId)) { text = "Open" };
            open.style.height = 18;
            open.style.minWidth = 40;
            open.style.marginLeft = 6;
            open.style.paddingLeft = 6;
            open.style.paddingRight = 6;
            open.style.fontSize = 9;
            row.Add(open);
        }

        static string ShortThreadId(string threadId)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                return "unknown";
            }

            return threadId.Length <= 12 ? threadId : $"{threadId.Substring(0, 8)}…";
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

        static void RenderCommand(
            VisualElement parent,
            JObject item,
            Action<string, string> sendTerminalInput)
        {
            string command = item["command"]?.Value<string>() ?? string.Empty;
            string title = string.IsNullOrEmpty(command) ? "Command" : $"$ {OneLine(command, 80)}";
            title = AppendDuration(title, item);
            Foldout foldout = Section(parent, title);
            AddStatus(foldout, item);
            string status = item["status"]?.Value<string>() ?? string.Empty;
            bool running = status.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                           status.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                           status.Equals("started", StringComparison.OrdinalIgnoreCase);
            if (running)
            {
                foldout.value = true;
            }

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

            string terminalInput = item["terminalInput"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(terminalInput))
            {
                Foldout inputSection = Section(foldout, "Terminal input");
                AddCode(inputSection, terminalInput);
            }

            string processId = item["processId"]?.Value<string>();
            if (sendTerminalInput == null || string.IsNullOrEmpty(processId) || !running)
            {
                return;
            }

            var inputRow = new VisualElement();
            inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.alignItems = Align.Center;
            inputRow.style.marginTop = 5;
            foldout.Add(inputRow);

            var input = new TextField { name = "codex-terminal-input" };
            input.style.flexGrow = 1;
            input.textEdition.placeholder = "Send terminal input...";
            inputRow.Add(input);

            var send = new Button(() =>
            {
                if (!string.IsNullOrWhiteSpace(input.value))
                {
                    sendTerminalInput(processId, input.value);
                    input.value = string.Empty;
                }
            })
            {
                text = "Send",
            };
            send.style.marginLeft = 5;
            inputRow.Add(send);

            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    if (!string.IsNullOrWhiteSpace(input.value))
                    {
                        sendTerminalInput(processId, input.value);
                        input.value = string.Empty;
                    }

                    evt.StopPropagation();
                }
            });
        }

        static void RenderTurnPlan(VisualElement parent, JObject item)
        {
            JArray plan = item["plan"] as JArray;
            if (plan == null || plan.Count == 0)
            {
                return;
            }

            Foldout foldout = Section(parent, "Plan");
            foldout.value = true;
            string explanation = item["explanation"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(explanation))
            {
                AddMuted(foldout, explanation);
            }

            foreach (JToken token in plan)
            {
                if (token is not JObject step)
                {
                    continue;
                }

                string text = step["step"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string status = step["status"]?.Value<string>() ?? "pending";
                var label = new Label($"{PlanStatusMark(status)} {text}");
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.fontSize = 10;
                label.style.color = PlanStatusColor(status);
                label.style.marginTop = 2;
                foldout.Add(label);
            }
        }

        static void RenderTurnDiff(VisualElement parent, JObject item)
        {
            string diff = item["diff"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(diff))
            {
                return;
            }

            Foldout foldout = Section(parent, "Turn diff");
            AddCode(foldout, diff);
        }

        static string AppendDuration(string title, JObject item)
        {
            long start = item["startedAtMs"]?.Value<long?>() ?? 0;
            long end = item["completedAtMs"]?.Value<long?>() ?? 0;
            if (end <= start && start > 0)
            {
                string status = item["status"]?.Value<string>() ?? string.Empty;
                if (status.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("running", StringComparison.OrdinalIgnoreCase))
                {
                    end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

            string duration = FormatDuration(start, end);
            return string.IsNullOrEmpty(duration) ? title : $"{title}  ·  {duration}";
        }

        internal static string FormatDuration(long startMs, long endMs)
        {
            if (startMs <= 0 || endMs <= startMs)
            {
                return string.Empty;
            }

            long elapsed = endMs - startMs;
            if (elapsed < 1000)
            {
                return $"{elapsed}ms";
            }

            long seconds = (elapsed + 999) / 1000;
            if (seconds < 60)
            {
                return $"{seconds}s";
            }

            long minutes = seconds / 60;
            long remainingSeconds = seconds % 60;
            if (minutes < 60)
            {
                return remainingSeconds == 0
                    ? $"{minutes}m"
                    : $"{minutes}m {remainingSeconds}s";
            }

            long hours = minutes / 60;
            long remainingMinutes = minutes % 60;
            return remainingMinutes == 0
                ? $"{hours}h"
                : $"{hours}h {remainingMinutes}m";
        }

        static string PlanStatusMark(string status)
        {
            return status switch
            {
                "completed" => "✓",
                "inProgress" or "in_progress" => "▶",
                "failed" => "✕",
                _ => "○",
            };
        }

        static Color PlanStatusColor(string status)
        {
            return status switch
            {
                "completed" => CodexPalette.Success,
                "inProgress" or "in_progress" => CodexPalette.Warning,
                "failed" => CodexPalette.Error,
                _ => CodexPalette.Muted,
            };
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

            title = AppendDuration(title, item);
            Foldout foldout = Section(parent, title);
            AddStatus(foldout, item);
            string status = item["status"]?.Value<string>() ?? string.Empty;
            if (status.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                foldout.value = true;
            }

            if (changes == null)
            {
                return;
            }

            string output = item["output"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(output))
            {
                Foldout outputSection = Section(foldout, "Output");
                AddMuted(outputSection, output.TrimEnd());
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
            string title = AppendDuration($"MCP · {server}/{tool}", item);
            Foldout foldout = Section(parent, title);
            AddStatus(foldout, item);
            string status = item["status"]?.Value<string>() ?? string.Empty;
            if (status.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                foldout.value = true;
            }

            string progress = item["progress"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(progress))
            {
                AddMuted(foldout, progress);
            }

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
                "failed" or "errored" => CodexPalette.Error,
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

        static VisualElement Bubble(Color background)
        {
            var bubble = new VisualElement();
            bubble.style.alignSelf = Align.FlexEnd;
            bubble.style.maxWidth = Length.Percent(68);
            bubble.style.flexShrink = 1;
            bubble.style.backgroundColor = background;
            bubble.style.borderTopWidth = 1;
            bubble.style.borderBottomWidth = 1;
            bubble.style.borderLeftWidth = 1;
            bubble.style.borderRightWidth = 1;
            bubble.style.borderTopColor = CodexPalette.PillBorder;
            bubble.style.borderBottomColor = CodexPalette.PillBorder;
            bubble.style.borderLeftColor = CodexPalette.PillBorder;
            bubble.style.borderRightColor = CodexPalette.PillBorder;
            bubble.style.borderTopLeftRadius = 14;
            bubble.style.borderTopRightRadius = 14;
            bubble.style.borderBottomLeftRadius = 14;
            bubble.style.borderBottomRightRadius = 14;
            bubble.style.paddingLeft = 13;
            bubble.style.paddingRight = 13;
            bubble.style.paddingTop = 9;
            bubble.style.paddingBottom = 9;
            bubble.style.marginLeft = 64;
            bubble.style.marginRight = 18;
            bubble.style.marginTop = 4;
            bubble.style.marginBottom = 12;
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
                    case "skill":
                        builder.AppendLine("$" + part["name"]?.Value<string>());
                        break;
                    case "mention":
                        builder.AppendLine("@" + (part["path"] ?? part["name"]));
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
