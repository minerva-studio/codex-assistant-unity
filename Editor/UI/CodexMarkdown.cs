using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using UMarkdown;
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
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static VisualElement Render(string markdown, string linkRoot = null)
        {
            string baseDirectory = string.IsNullOrEmpty(linkRoot) ? ProjectRoot : linkRoot;
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
    }
}
