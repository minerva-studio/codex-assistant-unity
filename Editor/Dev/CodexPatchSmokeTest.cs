using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks literal patch rendering and semantic line colors without sending a Codex message.</summary>
    internal static class CodexPatchSmokeTest
    {
        [MenuItem("Tools/Minerva/Codex/Check Patch Rendering")]
        public static string Run()
        {
            const string patch = "--- a/Example.cs\r\n+++ b/Example.cs\r\n@@ -1,3 +1,3 @@\r\n     unchanged\r\n-\told value\r\n+\t<color=red>literal markup</color>\r\n+++ counter;\r\n--- counter;\r\n--- a/Second.cs\r\n+++ b/Second.cs\r\n";
            var view = new CodexPatchView();
            view.SetPatch(patch);
            Require(view.Patch == patch, "original patch retains CRLF and its trailing newline");
            Require(view.childCount == 11, "line breaks and trailing blank line remain visible");
            var header = (Label)view[0];
            var target = (Label)view[1];
            var hunk = (Label)view[2];
            var context = (Label)view[3];
            var deletion = (Label)view[4];
            var addition = (Label)view[5];
            Require(header.text == "--- a/Example.cs" && target.text == "+++ b/Example.cs", "file headers retain their prefixes");
            Require(header.style.backgroundColor.value.a == 0 && target.style.backgroundColor.value.a == 0,
                "file headers have neutral backgrounds");
            Color green = addition.style.backgroundColor.value;
            Color red = deletion.style.backgroundColor.value;
            Require(green.a > 0 && green.g > green.r && green.g > green.b, "addition has a visible green background");
            Require(red.a > 0 && red.r > red.g && red.r > red.b, "deletion has a visible red background");
            Color blue = hunk.style.color.value;
            Require(blue.b > blue.r && blue.b > blue.g, "hunk heading is blue");
            Require(context.text == "     unchanged" && context.style.backgroundColor.value.a == 0, "context preserves indentation and stays neutral");
            Require(deletion.text == "-\told value" && addition.text == "+\t<color=red>literal markup</color>",
                "changed lines retain their signs, tabs and markup characters");
            Require(!addition.enableRichText && addition.style.whiteSpace.value == WhiteSpace.Pre,
                "patch text uses literal preformatted rendering");
            Require(((Label)view[6]).style.backgroundColor.value.g > ((Label)view[6]).style.backgroundColor.value.r
                && ((Label)view[7]).style.backgroundColor.value.r > ((Label)view[7]).style.backgroundColor.value.g,
                "source lines beginning with repeated signs remain changes");
            Require(((Label)view[8]).style.backgroundColor.value.a == 0 && ((Label)view[9]).style.backgroundColor.value.a == 0,
                "subsequent file headers remain neutral after the preceding hunk");
            Require(view.Patch == patch, "display classification never rewrites copyable patch text");

            view.SetPatch("+replacement\n");
            Require(view.childCount == 2 && ((Label)view[0]).text == "+replacement", "new patch replaces old display rows");
            view.SetPatch(null);
            Require(view.Patch == "" && view.childCount == 0, "empty patch clears display without inventing patch content");
            return "PASS: raw CRLF, trailing newline, neutral file headers, green/red changes, blue hunks, indentation, literal markup and patch replacement";
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Patch smoke: " + message);
        }
    }
}
