using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Pulls images and audio out of message text. Codex embeds attachments as
    /// "[Attached image ... is saved at: PATH]" lines, data URIs, and plain file
    /// paths; none of those should reach the reader as text.
    /// </summary>
    internal static class CodexAttachments
    {
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

        static readonly Dictionary<string, Texture2D> s_Textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, AudioClip> s_Clips = new Dictionary<string, AudioClip>();

        public static string Extract(string text, out List<Texture2D> images, out List<string> audioPaths)
        {
            images = new List<Texture2D>();
            audioPaths = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string cleaned = text;

            foreach (Match match in AttachedImage.Matches(text))
            {
                string path = match.Groups[2].Value.Trim();
                Texture2D texture = LoadTexture(path);
                if (texture != null)
                {
                    images.Add(texture);
                }

                cleaned = cleaned.Replace(match.Value, string.Empty);
            }

            foreach (Match match in DataImage.Matches(cleaned))
            {
                Texture2D texture = DecodeDataImage(match.Value);
                if (texture != null)
                {
                    images.Add(texture);
                }

                cleaned = cleaned.Replace(match.Value, string.Empty);
            }

            CollectAudio(text, AudioInCode, audioPaths);
            CollectAudio(text, AudioBare, audioPaths);

            return cleaned.Trim();
        }

        static void CollectAudio(string text, Regex pattern, List<string> audioPaths)
        {
            foreach (Match match in pattern.Matches(text))
            {
                string path = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ':');
                if (!audioPaths.Contains(path) && File.Exists(path))
                {
                    audioPaths.Add(path);
                }
            }
        }

        public static void Render(VisualElement parent, IReadOnlyList<Texture2D> images, IReadOnlyList<string> audioPaths)
        {
            foreach (Texture2D texture in images)
            {
                var image = new Image
                {
                    image = texture,
                    scaleMode = ScaleMode.ScaleToFit,
                    name = "AttachmentImage",
                };
                image.style.maxWidth = 320;
                image.style.maxHeight = 240;
                image.style.marginTop = 6;
                image.style.marginBottom = 4;
                parent.Add(image);
            }

            foreach (string path in audioPaths)
            {
                parent.Add(BuildAudioRow(path));
            }
        }

        static VisualElement BuildAudioRow(string path)
        {
            return new CodexAudioRow(path);
        }

        static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            if (s_Textures.TryGetValue(path, out Texture2D cached))
            {
                return cached;
            }

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }

                s_Textures[path] = texture;
                return texture;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Texture2D DecodeDataImage(string uri)
        {
            int comma = uri.IndexOf(',');
            if (comma < 0)
            {
                return null;
            }

            string key = uri.Substring(0, Math.Min(48, uri.Length)) + uri.Length;
            if (s_Textures.TryGetValue(key, out Texture2D cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(uri.Substring(comma + 1));
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

        internal static AudioClip LoadClip(string path)
        {
            if (s_Clips.TryGetValue(path, out AudioClip cached))
            {
                return cached;
            }

            try
            {
                AudioClip clip = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    ? DecodeWav(path)
                    : null;

                if (clip != null)
                {
                    var probe = new float[Mathf.Min(clip.samples, 4096)];
                    clip.GetData(probe, 0);
                    float peak = 0f;
                    foreach (float sample in probe)
                    {
                        peak = Mathf.Max(peak, Mathf.Abs(sample));
                    }

                    Debug.Log($"[CodexAudio] clip {Path.GetFileName(path)} {clip.length:F2}s peak={peak:F3}");
                    s_Clips[path] = clip;
                }

                return clip;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Loads any supported format through Unity's audio importer, which runs
        /// asynchronously on the editor update loop and works on every editor platform.
        /// </summary>
        public static void LoadClipAsync(string path, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                onLoaded?.Invoke(null);
                return;
            }

            if (s_Clips.TryGetValue(path, out AudioClip cached))
            {
                onLoaded?.Invoke(cached);
                return;
            }

            AudioType type = AudioType.UNKNOWN;
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                type = AudioType.WAV;
            }
            else if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                type = AudioType.OGGVORBIS;
            }
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                type = AudioType.MPEG;
            }
            else if (path.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase))
            {
                type = AudioType.AIFF;
            }

            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            request.SendWebRequest();

            void Tick()
            {
                if (!request.isDone)
                {
                    return;
                }

                EditorApplication.update -= Tick;

                AudioClip clip = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        clip.hideFlags = HideFlags.DontSave;
                        s_Clips[path] = clip;
                    }
                }
                else
                {
                    Debug.LogWarning($"[CodexAudio] load failed: {request.error} ({path})");
                }

                request.Dispose();
                onLoaded?.Invoke(clip);
            }

            EditorApplication.update += Tick;
        }

        public static AudioClip GetCachedClip(string path)
        {
            return !string.IsNullOrEmpty(path) && s_Clips.TryGetValue(path, out AudioClip clip) ? clip : null;
        }

        /// <summary>Minimal RIFF/WAVE decoder for PCM16 and float32, which covers generated clips.</summary>
        static AudioClip DecodeWav(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44 ||
                Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            {
                return null;
            }

            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int format = 1;
            int dataOffset = -1;
            int dataLength = 0;

            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                string chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
                int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
                int body = offset + 8;

                if (chunkId == "fmt " && body + 16 <= bytes.Length)
                {
                    format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bitsPerSample = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(chunkSize, bytes.Length - body);
                    break;
                }

                offset = body + chunkSize + (chunkSize & 1);
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            {
                return null;
            }

            int bytesPerSample = bitsPerSample / 8;
            int sampleCount = dataLength / bytesPerSample;
            if (sampleCount <= 0)
            {
                return null;
            }

            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int at = dataOffset + i * bytesPerSample;
                if (bitsPerSample == 16)
                {
                    samples[i] = BitConverter.ToInt16(bytes, at) / 32768f;
                }
                else if (bitsPerSample == 32 && format == 3)
                {
                    samples[i] = BitConverter.ToSingle(bytes, at);
                }
                else if (bitsPerSample == 32)
                {
                    samples[i] = BitConverter.ToInt32(bytes, at) / 2147483648f;
                }
                else
                {
                    return null;
                }
            }

            AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), sampleCount / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
