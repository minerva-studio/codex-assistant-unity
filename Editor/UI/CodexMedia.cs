using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    internal enum CodexMediaKind
    {
        Image,
        Audio,
        Video,
    }

    internal sealed class CodexMediaItem
    {
        public CodexMediaKind Kind;
        public string Path;
        public byte[] Bytes;
        public string Name;
    }

    /// <summary>
    /// Turns media references in message text into markdown image nodes with a
    /// media:// id, then resolves those ids back into elements. Both images and
    /// audio therefore render at the position the text mentions them, instead of
    /// being appended to the message.
    /// </summary>
    internal static class CodexMedia
    {
        static readonly Dictionary<string, CodexMediaItem> s_Items = new Dictionary<string, CodexMediaItem>();
        static readonly Dictionary<string, Texture2D> s_Textures = new Dictionary<string, Texture2D>();
        static int s_NextId;

        static readonly Regex AttachedImage = new Regex(
            "\\[Attached image \"([^\"]+)\" is saved at:\\s*([^\\]]+)\\]",
            RegexOptions.Compiled);

        static readonly Regex DataImage = new Regex(
            "data:image/[a-zA-Z0-9.+-]+;base64,[A-Za-z0-9+/=]+",
            RegexOptions.Compiled);

        static readonly Regex AudioInCode = new Regex(
            "`([^`]+\\.(?:wav|mp3|ogg|aiff|flac))`",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex AudioBare = new Regex(
            "([A-Za-z]:\\\\[^\\r\\n`\"<>|]+\\.(?:wav|mp3|ogg|aiff|flac))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex VideoInCode = new Regex(
            "`([^`]+\\.(?:mp4|mov|webm|m4v|avi))`",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex VideoBare = new Regex(
            "([A-Za-z]:\\\\[^\\r\\n`\"<>|]+\\.(?:mp4|mov|webm|m4v|avi))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Rewrites media references into markdown nodes. Fenced code is untouched.</summary>
        public static string Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string[] parts = text.Split(new[] { "```" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i += 2)
            {
                parts[i] = TokenizeSegment(parts[i]);
            }

            return string.Join("```", parts);
        }

        public static VisualElement Render(string id)
        {
            if (!s_Items.TryGetValue(id, out CodexMediaItem item))
            {
                return null;
            }

            switch (item.Kind)
            {
                case CodexMediaKind.Image:
                    return BuildImage(item);
                case CodexMediaKind.Audio:
                    return new CodexAudioRow(item.Path);
                default:
                    return BuildVideo(item);
            }
        }

        static string TokenizeSegment(string text)
        {
            text = AttachedImage.Replace(text, match =>
                Token(CodexMediaKind.Image, match.Groups[2].Value.Trim(), null, match.Groups[1].Value));

            text = DataImage.Replace(text, match =>
            {
                int comma = match.Value.IndexOf(',');
                byte[] bytes = null;
                try
                {
                    bytes = Convert.FromBase64String(match.Value.Substring(comma + 1));
                }
                catch (FormatException)
                {
                    return match.Value;
                }

                return Token(CodexMediaKind.Image, null, bytes, "image");
            });

            text = ReplacePaths(text, AudioInCode, CodexMediaKind.Audio);
            text = ReplacePaths(text, AudioBare, CodexMediaKind.Audio);
            text = ReplacePaths(text, VideoInCode, CodexMediaKind.Video);
            text = ReplacePaths(text, VideoBare, CodexMediaKind.Video);
            return text;
        }

        static string ReplacePaths(string text, Regex pattern, CodexMediaKind kind)
        {
            return pattern.Replace(text, match =>
            {
                string candidate = match.Groups[1].Value.Trim();
                candidate = candidate.TrimEnd('.', ',', ';', ':', ')', ']', '。', '，');

                if (!File.Exists(candidate))
                {
                    return match.Value;
                }

                return Token(kind, candidate, null, Path.GetFileName(candidate));
            });
        }

        static string Token(CodexMediaKind kind, string path, byte[] bytes, string name)
        {
            string id = (++s_NextId).ToString();
            s_Items[id] = new CodexMediaItem
            {
                Kind = kind,
                Path = path,
                Bytes = bytes,
                Name = name,
            };

            return $"![{kind.ToString().ToLowerInvariant()}](media://{id})";
        }

        static VisualElement BuildImage(CodexMediaItem item)
        {
            Texture2D texture = LoadTexture(item);
            if (texture == null)
            {
                var fallback = new Label(item.Name ?? "image");
                fallback.style.fontSize = 11;
                fallback.style.color = CodexPalette.Muted;
                return fallback;
            }

            var image = new Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit,
                name = "MediaImage",
            };
            image.style.maxWidth = 420;
            image.style.maxHeight = 320;
            image.style.marginTop = 6;
            image.style.marginBottom = 4;
            return image;
        }

        static VisualElement BuildVideo(CodexMediaItem item)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.marginTop = 6;
            card.style.marginBottom = 4;

            var badge = new Label("VIDEO");
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = CodexPalette.Muted;
            badge.style.marginRight = 8;

            var name = new Label(item.Name ?? "video");
            name.style.fontSize = 11;
            name.style.color = CodexPalette.Text;
            name.style.marginRight = 8;

            var open = new Button(() =>
            {
                if (!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
                {
                    EditorUtility.RevealInFinder(item.Path);
                }
            })
            {
                text = "Show",
            };
            open.style.fontSize = 10;

            card.Add(badge);
            card.Add(name);
            card.Add(open);
            return card;
        }

        static Texture2D LoadTexture(CodexMediaItem item)
        {
            string key = item.Path ?? "bytes:" + item.Bytes?.Length;
            if (s_Textures.TryGetValue(key, out Texture2D cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = item.Bytes;
                if (bytes == null)
                {
                    if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path))
                    {
                        return null;
                    }

                    bytes = File.ReadAllBytes(item.Path);
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }

                s_Textures[key] = texture;
                return texture;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
