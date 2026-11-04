using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UMarkdown;
using UMarkdown.Styling;
using UMarkdown.VisualElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Markdown rendering for assistant messages. All style decisions live here so
    /// the rest of the window does not depend on the renderer's API.
    /// </summary>
    internal static class CodexMarkdown
    {
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static VisualElement Render(string markdown)
        {
            var style = new RichTextMarkdownStyle
            {
                FontColor = CodexPalette.Text,
                FontSize = 12f,
                HeadingScale = 0.62f,
                CodeBackgroundOpacity = 0.25f,
                CodeTheme = EditorGUIUtility.isProSkin ? MarkdownCodeTheme.Dark : MarkdownCodeTheme.Light,
            };

            var factory = new DefaultMarkdownVisualElementFactory
            {
                ImageBaseDirectory = ProjectRoot,
                MediaResolver = CodexMedia.Render,
            };

            return UnityMarkdown.ToVisualElement(markdown, style, factory);
        }
    }
}
