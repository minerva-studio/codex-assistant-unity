using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        /// <summary>Renders a standalone path, used by imageView tool items.</summary>
        public static VisualElement RenderImage(string path)
        {
            return BuildImage(new CodexMediaItem
            {
                Kind = CodexMediaKind.Image,
                Path = path,
                Name = string.IsNullOrEmpty(path) ? "image" : Path.GetFileName(path),
            });
        }

        static string TokenizeSegment(string text)
        {
            text = AttachedImage.Replace(text, match =>
            {
                string reference = match.Groups[2].Value.Trim();
                string name = match.Groups[1].Value;
                if (TryDecodeDataImage(reference, out byte[] bytes))
                {
                    return Token(CodexMediaKind.Image, null, bytes, name);
                }

                if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    // Never put a failed/oversized data URI back into the label.
                    return Token(CodexMediaKind.Image, null, null, name);
                }

                return Token(CodexMediaKind.Image, reference, null, name);
            });

            text = DataImage.Replace(text, match =>
            {
                if (TryDecodeDataImage(match.Value, out byte[] bytes))
                {
                    return Token(CodexMediaKind.Image, null, bytes, "image");
                }

                return Token(CodexMediaKind.Image, null, null, "image");
            });

            text = ReplacePaths(text, AudioInCode, CodexMediaKind.Audio);
            text = ReplacePaths(text, AudioBare, CodexMediaKind.Audio);
            text = ReplacePaths(text, VideoInCode, CodexMediaKind.Video);
            text = ReplacePaths(text, VideoBare, CodexMediaKind.Video);
            return text;
        }

        static bool TryDecodeDataImage(string value, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int marker = value.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return false;
            }

            var payload = new StringBuilder(value.Length - marker);
            for (int index = marker + 8; index < value.Length; index++)
            {
                char character = value[index];
                if (!char.IsWhiteSpace(character))
                {
                    payload.Append(character);
                }
            }

            // Avoid a multi-second decode/layout for pathological clipboard data.
            if (payload.Length == 0 || payload.Length > 16_000_000)
            {
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(payload.ToString());
                return bytes.Length > 0;
            }
            catch (FormatException)
            {
                bytes = null;
                return false;
            }
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
                var fallback = new Label(!string.IsNullOrEmpty(item.Path) ? item.Path : item.Name ?? "image");
                fallback.style.whiteSpace = WhiteSpace.Normal;
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
            if (!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
            {
                return new CodexVideoRow(item.Path);
            }

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

    /// <summary>
    /// Removes Codex's file/image attachment preamble so the bubble shows the
    /// user's request instead of "Files mentioned by the user / My request".
    /// Image payloads stay in the text as [Attached image] tokens and are handled
    /// by <see cref="CodexMedia"/>.
    /// </summary>
    internal static class CodexAnnotations
    {
        static readonly Regex FileLine = new Regex(
            "^\\s*#{1,4}\\s*([^:\\r\\n]+):\\s*([^\\r\\n]+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        static readonly Regex Attachment = new Regex(
            "\\[Attached image \"[^\"]+\" is saved at:[^\\]]+\\]",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public static string Clean(string text)
        {
            const string headerMarker = "Files mentioned by the user:";
            if (string.IsNullOrEmpty(text) ||
                text.IndexOf(headerMarker, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return text;
            }

            int requestMarker = text.IndexOf("My request:", StringComparison.OrdinalIgnoreCase);
            string header = requestMarker >= 0 ? text.Substring(0, requestMarker) : text;
            string request = requestMarker >= 0
                ? text.Substring(requestMarker + "My request:".Length).Trim()
                : string.Empty;

            var blocks = new List<string>();
            var seen = new HashSet<string>();
            var files = new List<string>();
            foreach (Match match in FileLine.Matches(header))
            {
                string name = match.Groups[1].Value.Trim();
                string path = match.Groups[2].Value.Trim();
                if (path.Length == 0 ||
                    name.Equals("Files mentioned by the user", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("My request", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add("file:" + name))
                {
                    files.Add("Attachment: " + name);
                }
            }

            if (files.Count > 0)
            {
                blocks.Add(string.Join("\n", files));
            }

            var attachments = new List<string>();
            foreach (Match match in Attachment.Matches(header))
            {
                if (seen.Add("attachment:" + match.Value))
                {
                    attachments.Add(match.Value);
                }
            }

            if (attachments.Count > 0)
            {
                blocks.Add(string.Join("\n", attachments));
            }

            if (request.Length > 0)
            {
                blocks.Add(request);
            }

            string cleaned = string.Join("\n\n", blocks).Trim();
            return cleaned.Length > 0 ? cleaned : text;
        }
    }
}
