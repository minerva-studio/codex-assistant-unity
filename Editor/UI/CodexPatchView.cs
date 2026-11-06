using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Displays unified patches or whole-file additions/deletions, retaining the original text for copying.</summary>
    internal sealed class CodexPatchView : VisualElement
    {
        /// <summary>The original patch, including its line endings. A null input is normalized to an empty string.</summary>
        public string Patch { get; private set; } = "";
        string m_Operation;

        public CodexPatchView()
        {
            AddToClassList("codex-patch");
            style.alignSelf = Align.Stretch;
            style.flexShrink = 0;
            style.paddingTop = 8;
            style.paddingBottom = 8;
            style.unityFontDefinition = FontDefinition.FromFont(EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font);
            style.fontSize = 10;
            style.color = CodexPalette.Text;
        }

        /// <summary>Replaces the display from the supplied patch without interpreting markup or changing the original text.</summary>
        /// <param name="patch">Unified patch or whole-file content; null is treated as an empty string.</param>
        /// <param name="operation">For add/delete, every content line is an addition/deletion. Other values use unified-diff syntax.</param>
        public void SetPatch(string patch, string operation = null)
        {
            patch ??= "";
            if (Patch == patch && m_Operation == operation) return;
            Patch = patch;
            m_Operation = operation;
            Clear();
            if (patch.Length == 0) return;

            string[] lines = patch.Split('\n');
            // Hunk ranges distinguish header-looking source text from the next file's headers.
            int oldLines = 0;
            int newLines = 0;
            bool fullFile = operation == "add" || operation == "delete";
            for (int index = 0; index < lines.Length; index++)
            {
                string text = lines[index];
                // A final newline terminates the last content line; it is not another added/deleted line.
                if (fullFile && index == lines.Length - 1 && text.Length == 0) break;
                if (index < lines.Length - 1 && text.EndsWith("\r", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 1);

                var line = new Label(text) { enableRichText = false };
                line.AddToClassList("codex-patch-line");
                line.style.whiteSpace = WhiteSpace.Pre;
                line.style.alignSelf = Align.Stretch;
                line.style.flexShrink = 0;
                line.style.minHeight = 15;
                line.style.marginLeft = line.style.marginRight = 0;
                line.style.marginTop = line.style.marginBottom = 0;
                line.style.paddingLeft = line.style.paddingRight = 8;
                line.style.paddingTop = line.style.paddingBottom = 0;
                line.style.backgroundColor = Color.clear;
                bool added = operation == "add";
                bool removed = operation == "delete";
                if (fullFile)
                {
                    // Whole-file content may itself start with diff-looking characters.
                    line.text = (added ? "+ " : "- ") + text;
                }
                else if (text.StartsWith("@@", StringComparison.Ordinal))
                {
                    line.AddToClassList("codex-patch-hunk");
                    line.style.color = CodexPalette.Accent;
                    line.style.backgroundColor = CodexPalette.AccentSoft;
                    Match range = Regex.Match(text, @"^@@ -\d+(?:,(\d+))? \+\d+(?:,(\d+))? @@");
                    oldLines = newLines = 0;
                    if (range.Success)
                    {
                        int.TryParse(range.Groups[1].Value, out oldLines);
                        int.TryParse(range.Groups[2].Value, out newLines);
                        if (!range.Groups[1].Success) oldLines = 1;
                        if (!range.Groups[2].Success) newLines = 1;
                    }
                }
                else if (text.StartsWith("+", StringComparison.Ordinal)
                    && (oldLines > 0 || newLines > 0 || !text.StartsWith("+++ ", StringComparison.Ordinal)))
                {
                    added = true;
                    if (newLines > 0) newLines--;
                }
                else if (text.StartsWith("-", StringComparison.Ordinal)
                    && (oldLines > 0 || newLines > 0 || !text.StartsWith("--- ", StringComparison.Ordinal)))
                {
                    removed = true;
                    if (oldLines > 0) oldLines--;
                }
                else if (text.StartsWith(" ", StringComparison.Ordinal))
                {
                    if (oldLines > 0) oldLines--;
                    if (newLines > 0) newLines--;
                }

                if (added || removed)
                {
                    line.AddToClassList(added ? "codex-patch-addition" : "codex-patch-deletion");
                    Color color = added ? CodexPalette.Success : CodexPalette.Error;
                    line.style.color = color;
                    color.a = CodexPalette.Dark ? 0.18f : 0.12f;
                    line.style.backgroundColor = color;
                }
                Add(line);
            }
        }
    }
}
