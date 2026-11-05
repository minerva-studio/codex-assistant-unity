using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Minerva.Codex
{
    internal sealed class CodexQuestionOption
    {
        public string Label;
        public string Description;
    }

    internal sealed class CodexQuestion
    {
        public string Id;
        public string Header;
        public string Prompt;
        public bool IsOther;
        public bool IsSecret;
        public readonly List<CodexQuestionOption> Options = new List<CodexQuestionOption>();
        public string Answer;
    }

    internal sealed class CodexQuestionRequest
    {
        public JToken Id;
        public string ThreadId;
        public string TurnId;
        public string ItemId;
        public bool IsBlocking;
        public readonly List<CodexQuestion> Questions = new List<CodexQuestion>();

        public CodexQuestion CurrentQuestion
        {
            get
            {
                foreach (CodexQuestion question in Questions)
                {
                    if (string.IsNullOrEmpty(question.Answer))
                    {
                        return question;
                    }
                }

                return null;
            }
        }
    }

    /// <summary>A stored "question / answer" user message written by Codex.</summary>
    internal sealed class CodexQuestionReply
    {
        public string Question;
        public string Answer;

        static readonly Regex ReplyPattern = new Regex(
            "<send_user_message_question_reply>(.*?)</send_user_message_question_reply>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Parses Codex's question-reply wrapper. The payload is a JSON array of
        /// objects with question/answer strings; multiple entries are preserved.
        /// </summary>
        public static bool TryParse(
            string text,
            out List<CodexQuestionReply> replies,
            out string remainder)
        {
            replies = null;
            remainder = text;
            if (string.IsNullOrEmpty(text) || !text.Contains("send_user_message_question_reply"))
            {
                return false;
            }

            MatchCollection matches = ReplyPattern.Matches(text);
            if (matches.Count == 0)
            {
                return false;
            }

            var parsed = new List<CodexQuestionReply>();
            foreach (Match match in matches)
            {
                string payload = match.Groups[1].Value.Trim();
                if (payload.Length == 0)
                {
                    continue;
                }

                JToken root;
                try
                {
                    root = JToken.Parse(payload);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (root is JArray array)
                {
                    foreach (JToken entry in array)
                    {
                        AddReply(entry, parsed);
                    }
                }
                else
                {
                    AddReply(root, parsed);
                }
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            replies = parsed;
            remainder = ReplyPattern.Replace(text, string.Empty).Trim();
            return true;
        }

        static void AddReply(JToken entry, List<CodexQuestionReply> replies)
        {
            if (entry is not JObject obj)
            {
                return;
            }

            string question = obj["question"]?.Value<string>();
            string answer = AnswerText(obj["answer"]);
            if (string.IsNullOrWhiteSpace(answer))
            {
                return;
            }

            replies.Add(new CodexQuestionReply
            {
                Question = string.IsNullOrWhiteSpace(question) ? "Question" : question.Trim(),
                Answer = answer.Trim(),
            });
        }

        static string AnswerText(JToken answer)
        {
            if (answer == null)
            {
                return string.Empty;
            }

            if (answer.Type == JTokenType.String)
            {
                return answer.Value<string>();
            }

            if (answer is JArray array)
            {
                var parts = new List<string>();
                foreach (JToken part in array)
                {
                    string value = part.Type == JTokenType.String
                        ? part.Value<string>()
                        : part.ToString(Formatting.None);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parts.Add(value.Trim());
                    }
                }

                return string.Join("\n", parts);
            }

            return answer.ToString(Formatting.None);
        }
    }
}
