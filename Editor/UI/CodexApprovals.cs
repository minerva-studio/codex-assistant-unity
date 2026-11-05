using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Minerva.Codex
{
    internal enum CodexApprovalKind
    {
        Command,
        FileChange,
        Permissions,
        McpElicitation,
    }

    /// <summary>A server-initiated approval or MCP elicitation shown above the composer.</summary>
    internal sealed class CodexApprovalRequest
    {
        public JToken Id;
        public CodexApprovalKind Kind;
        public string ThreadId;
        public string TurnId;
        public string ItemId;
        public string Reason;
        public string Command;
        public string Cwd;
        public string CommandKind;
        public JArray AvailableDecisions;
        public JObject Permissions;
        public readonly List<string> Files = new List<string>();
        public string ServerName;
        public string McpMode;
        public string McpMessage;
        public string McpTitle;
        public string McpDescription;
        public string McpUrl;
        public string McpChallenge;
        public JObject McpSchema;

        public bool IsMcpUserVerification => McpMode == "openai/userVerification";
    }
}
