using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Reads recorded patches with source coordinates, syntax colors and collapsible supplied context.</summary>
    internal sealed class CodexPatchView : VisualElement
    {
        sealed class SourceRow
        {
            public string Text;
            public string Path;
            public string Colored;
            // Diff prefix; '@' is a hunk heading and '\0' is metadata. Zero coordinates mean unavailable.
            public char Marker;
            public int Before;
            public int After;
            public int Segment;
        }

        // ColorCode supplies the language grammars/palette already used by Markdown. Format each token
        // once and close colors on each line so multiline syntax cannot bleed into neighboring diff rows.
        sealed class Colorizer : CodeColorizerBase
        {
            readonly List<StringBuilder> m_Lines = new List<StringBuilder> { new StringBuilder() };

            public Colorizer() : base(CodexPalette.Dark ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight, null) { }

            public string[] Format(string source, ILanguage language)
            {
                languageParser.Parse(source, language, Write);
                return m_Lines.Select(line => line.ToString()).ToArray();
            }

            protected override void Write(string source, IList<Scope> scopes)
            {
                AppendScopes(source, 0, source.Length, scopes, null);
            }

            void AppendScopes(string source, int start, int end, IList<Scope> scopes, string color)
            {
                int cursor = start;
                foreach (Scope scope in scopes.OrderBy(scope => scope.Index))
                {
                    int from = Math.Max(cursor, scope.Index);
                    int to = Math.Min(end, scope.Index + scope.Length);
                    if (to <= from) continue;
                    Append(source.Substring(cursor, from - cursor), color);
                    string foreground = color;
                    if (Styles.TryGetValue(scope.Name, out var style) && !string.IsNullOrEmpty(style.Foreground))
                    {
                        string hex = style.Foreground.TrimStart('#');
                        foreground = "#" + hex.Substring(2) + hex.Substring(0, 2);
                    }
                    AppendScopes(source, from, to, scope.Children, foreground);
                    cursor = to;
                }
                Append(source.Substring(cursor, end - cursor), color);
            }

            void Append(string text, string color)
            {
                string[] parts = text.Split('\n');
                for (int index = 0; index < parts.Length; index++)
                {
                    if (index > 0) m_Lines.Add(new StringBuilder());
                    if (parts[index].Length == 0) continue;
                    StringBuilder line = m_Lines[m_Lines.Count - 1];
                    if (color != null) line.Append("<color=").Append(color).Append('>');
                    // Isolate every literal '<', including a source string containing '</noparse>'.
                    line.Append(parts[index].Replace("<", "<noparse><</noparse>"));
                    if (color != null) line.Append("</color>");
                }
            }
        }

        static readonly Regex Hunk = new Regex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);
        const int ContextLines = 3;
        readonly bool m_Wrap;
        readonly HashSet<string> m_ExpandedContext = new HashSet<string>();
        string m_Operation;
        string m_Path;
        bool m_Dark;

        /// <summary>The untouched supplied patch, including line endings. Null input is normalized to empty.</summary>
        public string Patch { get; private set; } = "";
        /// <summary>Added source lines; file/hunk headers and the final terminating newline are excluded.</summary>
        public int Additions { get; private set; }
        /// <summary>Removed source lines; file/hunk headers and the final terminating newline are excluded.</summary>
        public int Deletions { get; private set; }

        /// <param name="wrap">Wraps source text inside its row while the line-number gutters stay aligned.</param>
        public CodexPatchView(bool wrap = false)
        {
            m_Wrap = wrap;
            AddToClassList("codex-patch");
            style.alignSelf = Align.Stretch;
            style.flexShrink = 0;
            style.paddingTop = style.paddingBottom = 8;
            style.unityFontDefinition = FontDefinition.FromFont(EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font);
            style.fontSize = 10;
            style.color = CodexPalette.Text;
        }

        /// <summary>Replaces the reader without rewriting the copy source or reading current files.</summary>
        /// <param name="patch">Unified patch or whole-file content; null is empty.</param>
        /// <param name="operation">Add/delete means whole-file content; other values use unified syntax.</param>
        /// <param name="path">Source filename for syntax detection; aggregate patches infer filenames from their headers.</param>
        public void SetPatch(string patch, string operation = null, string path = null)
        {
            patch ??= "";
            if (Patch == patch && m_Operation == operation && m_Path == path && m_Dark == CodexPalette.Dark) return;
            if (m_Operation != operation || m_Path != path) m_ExpandedContext.Clear();
            Patch = patch;
            m_Operation = operation;
            m_Path = path;
            m_Dark = CodexPalette.Dark;
            Clear();
            List<SourceRow> rows = Parse(patch, operation, path);
            Additions = rows.Count(row => row.Marker == '+');
            Deletions = rows.Count(row => row.Marker == '-');
            Highlight(rows);
            float numberWidth = Math.Max(22, rows.Select(row => Math.Max(row.Before, row.After)).DefaultIfEmpty(0).Max().ToString().Length * 6 + 6);
            var contextKeys = new HashSet<string>();
            for (int index = 0; index < rows.Count;)
            {
                int end = index;
                while (end < rows.Count && rows[end].Marker == ' ') end++;
                if (end - index > ContextLines * 2 + 2)
                {
                    for (int line = index; line < index + ContextLines; line++) AddRow(this, rows[line], numberWidth);
                    int start = index + ContextLines;
                    int count = end - index - ContextLines * 2;
                    string position = rows[start].Before > 0 || rows[start].After > 0
                        ? rows[start].Before + ":" + rows[start].After : "row:" + start;
                    string key = rows[start].Path + ":" + rows[start].Segment + ":" + position + ":" + count;
                    contextKeys.Add(key);
                    AddContext(rows, start, count, key, numberWidth);
                    for (int line = end - ContextLines; line < end; line++) AddRow(this, rows[line], numberWidth);
                    index = end;
                }
                else AddRow(this, rows[index++], numberWidth);
            }
            m_ExpandedContext.IntersectWith(contextKeys);
        }

        static List<SourceRow> Parse(string patch, string operation, string path)
        {
            var rows = new List<SourceRow>();
            string[] lines = patch.Split('\n');
            bool fullFile = operation == "add" || operation == "delete";
            int before = 0, after = 0, oldRemaining = 0, newRemaining = 0, segment = 0;
            string file = path;
            for (int index = 0; index < lines.Length; index++)
            {
                string text = lines[index];
                if (index == lines.Length - 1 && text.Length == 0) break;
                if (index < lines.Length - 1 && text.EndsWith("\r", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
                var row = new SourceRow { Text = text };
                Match hunk = fullFile ? Match.Empty : Hunk.Match(text);
                bool inHunk = oldRemaining > 0 || newRemaining > 0;
                if (fullFile)
                {
                    row.Marker = operation == "add" ? '+' : '-';
                    if (row.Marker == '+') row.After = index + 1;
                    else row.Before = index + 1;
                }
                else if (hunk.Success)
                {
                    row.Marker = '@';
                    int.TryParse(hunk.Groups[1].Value, out before);
                    int.TryParse(hunk.Groups[3].Value, out after);
                    oldRemaining = hunk.Groups[2].Success && int.TryParse(hunk.Groups[2].Value, out int oldCount) ? oldCount : 1;
                    newRemaining = hunk.Groups[4].Success && int.TryParse(hunk.Groups[4].Value, out int newCount) ? newCount : 1;
                    segment++;
                }
                else if (text.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    oldRemaining = newRemaining = 0;
                    file = path;
                    segment++;
                }
                else if (!inHunk && (text.StartsWith("--- ", StringComparison.Ordinal) || text.StartsWith("+++ ", StringComparison.Ordinal)))
                {
                    string headerPath = text.Substring(4).Split('\t')[0].Trim('"');
                    if (headerPath != "/dev/null") file = headerPath;
                    segment++;
                }
                else if (text.Length > 0 && (text[0] == '+' || text[0] == '-' || text[0] == ' '))
                {
                    row.Marker = text[0];
                    row.Text = text.Substring(1);
                    if (row.Marker != '+' && oldRemaining > 0) { row.Before = before++; oldRemaining--; }
                    if (row.Marker != '-' && newRemaining > 0) { row.After = after++; newRemaining--; }
                }
                row.Path = file;
                row.Segment = segment;
                rows.Add(row);
            }
            return rows;
        }

        static void Highlight(List<SourceRow> rows)
        {
            foreach (var segment in rows.Where(row => row.Marker == '+' || row.Marker == '-' || row.Marker == ' ').GroupBy(row => row.Segment))
            {
                string path = segment.First().Path;
                if (string.IsNullOrEmpty(path)) continue;
                int dot = path.LastIndexOf('.');
                if (dot < 0 || dot < Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'))) continue;
                string extension = path.Substring(dot + 1).ToLowerInvariant();
                ILanguage language = extension switch
                {
                    "shader" or "hlsl" or "compute" or "cginc" or "c" or "h" or "cpp" or "hpp" => Languages.Cpp,
                    "json" or "asmdef" or "asmref" => Languages.JavaScript,
                    _ => Languages.FindById(extension) ?? Languages.All.FirstOrDefault(candidate => candidate.HasAlias(extension)),
                };
                if (language == null) continue;
                // Reconstruct each side separately: removed text must not affect the new side's lexer state.
                foreach (char excluded in new[] { '+', '-' })
                {
                    var side = segment.Where(row => row.Marker != excluded).ToList();
                    string[] colored = new Colorizer().Format(string.Join("\n", side.Select(row => row.Text)) + "\n", language);
                    for (int index = 0; index < side.Count; index++) side[index].Colored = colored[index];
                }
            }
        }

        void AddContext(List<SourceRow> rows, int start, int count, string key, float numberWidth)
        {
            var button = new Button { name = "codex-patch-context-toggle" };
            button.style.height = 22;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginTop = button.style.marginBottom = 0;
            button.style.fontSize = 10;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.backgroundColor = CodexPalette.SurfaceRaised;
            button.style.color = CodexPalette.Muted;
            var content = new VisualElement { name = "codex-patch-context" };
            bool loaded = false;
            void Expand(bool expanded)
            {
                if (expanded)
                {
                    if (!loaded)
                    {
                        for (int index = start; index < start + count; index++) AddRow(content, rows[index], numberWidth);
                        loaded = true;
                    }
                    m_ExpandedContext.Add(key);
                }
                else m_ExpandedContext.Remove(key);
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                button.text = expanded ? "▾  Collapse " + count + " unchanged lines" : "▸  " + count + " unchanged lines · Expand";
            }
            button.clicked += () => Expand(content.style.display.value == DisplayStyle.None);
            Expand(m_ExpandedContext.Contains(key));
            Add(button);
            Add(content);
        }

        void AddRow(VisualElement parent, SourceRow source, float numberWidth)
        {
            var row = new VisualElement { name = "codex-patch-line" };
            row.AddToClassList("codex-patch-line");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.alignSelf = Align.Stretch;
            row.style.flexShrink = 0;
            row.style.minWidth = 0;
            row.style.minHeight = 15;
            bool addition = source.Marker == '+';
            bool deletion = source.Marker == '-';
            Color markerColor = addition ? CodexPalette.Success : deletion ? CodexPalette.Error : CodexPalette.Muted;
            if (addition || deletion || source.Marker == '@')
            {
                row.AddToClassList(addition ? "codex-patch-addition" : deletion ? "codex-patch-deletion" : "codex-patch-hunk");
                Color background = source.Marker == '@' ? CodexPalette.AccentSoft : markerColor;
                if (source.Marker != '@') background.a = CodexPalette.Dark ? 0.18f : 0.12f;
                row.style.backgroundColor = background;
            }
            foreach (bool old in new[] { true, false })
            {
                int number = old ? source.Before : source.After;
                var gutter = new Label(number > 0 ? number.ToString() : "")
                    { name = old ? "codex-patch-old-number" : "codex-patch-new-number", enableRichText = false };
                gutter.style.width = numberWidth;
                gutter.style.flexShrink = 0;
                gutter.style.fontSize = 9;
                gutter.style.color = CodexPalette.Muted;
                gutter.style.unityTextAlign = TextAnchor.UpperRight;
                gutter.style.marginLeft = gutter.style.marginRight = 0;
                gutter.style.marginTop = gutter.style.marginBottom = 0;
                gutter.style.paddingRight = 4;
                row.Add(gutter);
            }
            var marker = new Label(addition ? "+" : deletion ? "−" : "") { name = "codex-patch-marker", enableRichText = false };
            marker.style.width = 12;
            marker.style.flexShrink = 0;
            marker.style.color = markerColor;
            marker.style.marginLeft = marker.style.marginRight = 0;
            marker.style.marginTop = marker.style.marginBottom = 0;
            row.Add(marker);
            var code = new Label(source.Colored ?? source.Text)
                { name = "codex-patch-code", enableRichText = source.Colored != null };
            code.style.whiteSpace = m_Wrap ? WhiteSpace.PreWrap : WhiteSpace.Pre;
            code.style.flexGrow = 1;
            code.style.flexShrink = m_Wrap ? 1 : 0;
            code.style.minWidth = 0;
            code.style.minHeight = 15;
            code.style.marginLeft = code.style.marginRight = 0;
            code.style.marginTop = code.style.marginBottom = 0;
            code.style.paddingRight = 8;
            code.style.color = source.Marker == '@' ? CodexPalette.Accent : source.Marker == '\0' ? CodexPalette.Muted : CodexPalette.Text;
            row.Add(code);
            parent.Add(row);
        }
    }
}
