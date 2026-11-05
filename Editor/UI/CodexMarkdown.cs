using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using UMarkdown;
using UMarkdown.Parsing;
using UMarkdown.Styling;
using UMarkdown.VisualElements;
using Object = UnityEngine.Object;

namespace Minerva.Codex
{
    /// <summary>
    /// Markdown rendering for assistant messages. All style decisions live here so
    /// the rest of the window does not depend on the renderer's API.
    /// </summary>
    internal static class CodexMarkdown
    {
        static readonly Regex SkillTokenPattern = new Regex(
            @"(?<![\w`\[\]$])\$([A-Za-z_][A-Za-z0-9_.-]*)",
            RegexOptions.Compiled);

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static VisualElement Render(string markdown, string linkRoot = null)
        {
            string baseDirectory = string.IsNullOrEmpty(linkRoot) ? ProjectRoot : linkRoot;
            markdown = DecorateSkillTokens(markdown);
            var style = new RichTextMarkdownStyle
            {
                FontColor = CodexPalette.Text,
                FontSize = 12f,
                HeadingScale = 0.62f,
                CodeBackgroundOpacity = 0.25f,
                CodeTheme = EditorGUIUtility.isProSkin ? MarkdownCodeTheme.Dark : MarkdownCodeTheme.Light,
            };

            var factory = new CodexMarkdownFactory
            {
                ImageBaseDirectory = baseDirectory,
                MediaResolver = CodexMedia.Render,
            };

            VisualElement element = UnityMarkdown.ToVisualElement(markdown, style, factory);
            element.RegisterCallback<PointerDownLinkTagEvent>(evt =>
            {
                OpenLink(evt.linkID, baseDirectory);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            return element;
        }

        static string DecorateSkillTokens(string markdown)
        {
            if (string.IsNullOrEmpty(markdown) || markdown.IndexOf('$') < 0)
            {
                return markdown;
            }

            string normalized = markdown.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            bool fenced = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                    trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                    continue;
                }

                if (fenced)
                {
                    continue;
                }

                lines[index] = SkillTokenPattern.Replace(
                    lines[index],
                    match => "`$" + match.Groups[1].Value + "`");
            }

            return string.Join("\n", lines);
        }

        static void OpenLink(string link, string baseDirectory)
        {
            if (string.IsNullOrEmpty(link) || link[0] == '#')
            {
                return;
            }

            if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                Application.OpenURL(link);
                return;
            }

            string path = link;
            if (link.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(link, UriKind.Absolute, out Uri uri))
            {
                path = uri.LocalPath;
            }

            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(baseDirectory, path));
            }

            if (Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"Codex Assistant: link target does not exist: {link}");
                return;
            }

            string assetPath = path.Replace('\\', '/');
            int assetsIndex = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                assetPath = assetPath.Substring(assetsIndex + 1);
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset);
                    return;
                }
            }

            EditorUtility.OpenWithDefaultApp(path);
        }

        sealed class CodexMarkdownFactory : DefaultMarkdownVisualElementFactory
        {
            public override VisualElement CodeBlock(RichTextRenderer renderer, string richText)
            {
                VisualElement block = base.CodeBlock(renderer, richText);
                Label code = block.Q<Label>("CodeContent");
                if (code != null)
                {
                    // UI Toolkit labels default to WhiteSpace.Normal, which
                    // collapses the leading indentation of every code line.
                    // Keep rich text enabled: UMarkdown's syntax colors are
                    // emitted as color tags inside this label.
                    code.style.whiteSpace = WhiteSpace.PreWrap;
                }

                return block;
            }
        }
    }
}
