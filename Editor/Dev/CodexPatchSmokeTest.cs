using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks source coordinates, literal syntax display, context folding and raw patch retention.</summary>
    internal static class CodexPatchSmokeTest
    {
        [MenuItem("Tools/Minerva/Codex/Check Patch Rendering")]
        public static string Run()
        {
            const string patch = "--- a/Example.cs\r\n+++ b/Example.cs\r\n@@ -1,3 +1,3 @@\r\n     unchanged\r\n-\told value\r\n+\t<color=red>literal markup</color>\r\n+++ counter;\r\n--- counter;\r\n--- a/Second.cs\r\n+++ b/Second.cs\r\n";
            var view = new CodexPatchView();
            view.SetPatch(patch);
            var rows = view.Query<VisualElement>("codex-patch-line").ToList();
            Require(rows.Count == 10 && view.Patch == patch, "CRLF and final newline stay in the copy source without a phantom row");
            Require(Text(rows[0]) == "--- a/Example.cs" && Text(rows[1]) == "+++ b/Example.cs", "file headers retain their prefixes");
            Require(rows[0].style.backgroundColor.value.a == 0 && rows[1].style.backgroundColor.value.a == 0, "file headers remain neutral");
            Require(rows[2].ClassListContains("codex-patch-hunk"), "hunk heading stays distinct");
            Require(Number(rows[3], true) == "1" && Number(rows[3], false) == "1", "context has old and new source coordinates");
            Require(Number(rows[4], true) == "2" && Number(rows[4], false) == "", "removed line only has an old coordinate");
            Require(Number(rows[5], true) == "" && Number(rows[5], false) == "2", "added line only has a new coordinate");
            Require(Text(rows[3]) == "    unchanged" && Text(rows[4]) == "\told value"
                && Text(rows[5]) == "\t<color=red>literal markup</color>", "diff markers are separated without changing source indentation or markup");
            Require(rows[5].Q<Label>("codex-patch-marker").text == "+", "addition marker remains visible beside source text");
            Color green = rows[5].style.backgroundColor.value;
            Color red = rows[4].style.backgroundColor.value;
            Require(green.a > 0 && green.g > green.r && red.a > 0 && red.r > red.g, "syntax colors retain green/red row backgrounds");
            Require(rows[6].ClassListContains("codex-patch-addition") && rows[7].ClassListContains("codex-patch-deletion"), "header-looking source inside a hunk remains a change");
            Require(!rows[8].ClassListContains("codex-patch-deletion") && !rows[9].ClassListContains("codex-patch-addition"), "next file headers remain metadata");
            Require(view.Additions == 2 && view.Deletions == 2, "counts exclude file/hunk headings");

            view.SetPatch("@@ -0,0 +10,2 @@\n+one\n+two\n@@ -25 +30 @@\n-old\n+new\n\\ No newline at end of file\n");
            rows = view.Query<VisualElement>("codex-patch-line").ToList();
            Require(Number(rows[1], true) == "" && Number(rows[1], false) == "10" && Number(rows[2], false) == "11", "zero-length old range does not invent old coordinates");
            Require(Number(rows[4], true) == "25" && Number(rows[5], false) == "30", "omitted counts default to one and disjoint hunks reset coordinates");
            Require(Number(rows[6], true) == "" && Number(rows[6], false) == "", "no-newline marker has no source coordinate");
            view.SetPatch("+replacement\n");
            var replacement = view.Q("codex-patch-line");
            Require(Number(replacement, true) == "" && Number(replacement, false) == "", "a patch without hunk coordinates leaves gutters blank");
            view.SetPatch(null);
            Require(view.Patch == "" && view.childCount == 0 && view.Additions == 0 && view.Deletions == 0, "empty patch clears rows and counts");

            const string content = "class Example {}\r\n\r\n@@ literal content\r\n+++ literal content\r\n- literal content\r\n";
            view.SetPatch(content, "add", "Example.cs");
            rows = view.Query<VisualElement>("codex-patch-line").ToList();
            Require(rows.Count == 5 && view.Patch == content && view.Additions == 5 && view.Deletions == 0, "whole-file additions include blank content lines, excluding the terminator");
            Require(rows.All(row => row.ClassListContains("codex-patch-addition")) && Number(rows[4], false) == "5", "new-file rows are numbered on the new side");
            Require(Text(rows[3]) == "+++ literal content", "whole-file content keeps its own diff-looking prefixes");
            Require(rows[0].Q<Label>("codex-patch-code").enableRichText
                && rows[0].Q<Label>("codex-patch-code").text.Contains("<color="), "C# syntax uses the existing language palette");
            view.SetPatch(content, "delete", "Example.cs");
            rows = view.Query<VisualElement>("codex-patch-line").ToList();
            Require(view.Additions == 0 && view.Deletions == 5 && Number(rows[4], true) == "5"
                && Number(rows[4], false) == "", "whole-file deletions reset counts and use old coordinates");

            const string literal = "var value = \"<color=red>literal</color></noparse><size=80>\";\n";
            view.SetPatch(literal, "add", "Literal.cs");
            var code = view.Q<Label>("codex-patch-code");
            Require(code.enableRichText && Text(view.Q("codex-patch-line")) == literal.TrimEnd('\n'), "highlighting never duplicates or interprets source markup");
            Require(code.text.Contains("<noparse><</noparse>"), "literal tag openings are isolated from generated markup");
            view.SetPatch(literal, "add", "Literal.unknown");
            Require(!view.Q<Label>("codex-patch-code").enableRichText && Text(view.Q("codex-patch-line")) == literal.TrimEnd('\n'), "unknown languages keep literal plain text");

            var baseline = new CodexPatchView();
            baseline.SetPatch("int kept = 2;\n", "add", "Example.cs");
            string expectedColor = baseline.Q<Label>("codex-patch-code").text;
            view.SetPatch("@@ -1,3 +1,3 @@\n-/* old comment\n+int active = 1;\n int kept = 2;\n */\n@@ -50 +50 @@\n int kept = 2;\n", "update", "Example.cs");
            rows = view.Query<VisualElement>("codex-patch-line").ToList();
            Require(rows[3].Q<Label>("codex-patch-code").text == expectedColor, "removed multiline comment cannot color the new side");
            Require(rows[6].Q<Label>("codex-patch-code").text == expectedColor, "a later hunk starts fresh lexer state across omitted content");
            view.SetPatch("--- a/a.cs\n+++ b/a.cs\n@@ -0,0 +1 @@\n+public class Example {}\n--- a/b.py\n+++ b/b.py\n@@ -0,0 +1 @@\n+def build():\n");
            Require(view.Query<VisualElement>("codex-patch-line").ToList().Last().Q<Label>("codex-patch-code").text.Contains("<color="), "aggregate patches infer syntax per file");

            string context = string.Join("\n", Enumerable.Range(1, 20).Select(index => " int field" + index + ";"));
            string longPatch = "@@ -1,20 +1,21 @@\n" + context + "\n+int added = 1;\n";
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            host.titleContent = new GUIContent("Patch smoke fixture");
            host.rootVisualElement.Add(view);
            host.ShowUtility();
            try
            {
                view.SetPatch(longPatch, "update", "Example.cs");
                var fold = view.Q<Button>("codex-patch-context-toggle");
                var hidden = view.Q("codex-patch-context");
                Require(fold != null && fold.text.Contains("14") && hidden.childCount == 0
                    && hidden.style.display.value == DisplayStyle.None, "long supplied context retains three lines at each end and folds the middle lazily");
                Require(view.Query<VisualElement>("codex-patch-line").ToList().Count == 8, "changed row and nearby context remain visible");
                Submit(fold);
                Require(hidden.childCount == 14 && hidden.style.display.value == DisplayStyle.Flex
                    && Number(hidden[0], true) == "4" && Number(hidden[13], false) == "17", "expanding restores exactly the hidden source coordinates");
                view.SetPatch(longPatch.Replace("added = 1", "added = 2"), "update", "Example.cs");
                Require(view.Q("codex-patch-context").style.display.value == DisplayStyle.Flex, "unchanged context expansion survives a neighboring patch update");
                Submit(view.Q<Button>("codex-patch-context-toggle"));
                Require(view.Q("codex-patch-context").style.display.value == DisplayStyle.None && view.Patch.EndsWith("+int added = 2;\n"), "recollapse changes display only");
                Require(view.Additions == 1 && view.Deletions == 0, "folding does not alter change totals");
                view.SetPatch("@@ -1 +100 @@\n old\n", "update", "Example.cs");
                Require(view.Q<Button>("codex-patch-context-toggle") == null, "omitted file content never becomes an invented expandable block");
                string unknown = context + "\n+first\n" + context + "\n+second\n";
                view.SetPatch(unknown, "update", "Example.cs");
                Submit(view.Query<Button>("codex-patch-context-toggle").ToList()[0]);
                view.SetPatch(unknown.Replace("+first", "+updated"), "update", "Example.cs");
                var contexts = view.Query<VisualElement>("codex-patch-context").ToList();
                Require(contexts.Count == 2 && contexts[0].style.display.value == DisplayStyle.Flex
                    && contexts[1].style.display.value == DisplayStyle.None,
                    "equal context blocks without source coordinates retain independent expansion across updates");
            }
            finally { host.Close(); }
            var wrapped = new CodexPatchView(wrap: true);
            wrapped.SetPatch("+    indented source\n");
            Require(wrapped.Q<Label>("codex-patch-code").style.whiteSpace.value == WhiteSpace.PreWrap, "wrapping applies to source without moving gutters");

            var transcript = new VisualElement();
            CodexItemRenderer.Render(transcript, new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = "fileChange", ["changes"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = "New.cs", ["kind"] = new Newtonsoft.Json.Linq.JObject { ["type"] = "add" }, ["diff"] = content },
                    new Newtonsoft.Json.Linq.JObject { ["path"] = "Old.cs", ["kind"] = "delete", ["diff"] = content }),
            }, true, true);
            var patches = transcript.Query<CodexPatchView>().ToList();
            Require(patches.Count == 2 && patches[0].Q("codex-patch-line").ClassListContains("codex-patch-addition")
                && patches[1].Q("codex-patch-line").ClassListContains("codex-patch-deletion")
                && patches[0].Q<Label>("codex-patch-code").enableRichText, "transcript supplies file operation and language path");
            return "PASS: source coordinates, zero/omitted hunk ranges, raw CRLF/copy, literal syntax colors, independent lexer sides/files/hunks, whole-file add/delete, lazy context folding and transcript integration";
        }

        static string Number(VisualElement row, bool old) => row.Q<Label>(old ? "codex-patch-old-number" : "codex-patch-new-number").text;

        static string Text(VisualElement row)
        {
            var code = row.Q<Label>("codex-patch-code");
            return code.enableRichText ? Regex.Replace(code.text, @"<color=#[0-9A-Fa-f]{8}>|</color>", "")
                .Replace("<noparse><</noparse>", "<") : code.text;
        }

        static void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Patch smoke: " + message);
        }
    }
}
