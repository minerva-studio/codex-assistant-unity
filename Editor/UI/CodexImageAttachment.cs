using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Minerva.Codex
{
    /// <summary>
    /// One image staged in the composer. The source path is what gets sent as a
    /// <c>localImage</c> input; <see cref="Thumbnail"/> exists only for the UI.
    /// </summary>
    internal sealed class CodexImageAttachment : IDisposable
    {
        const int ThumbnailSize = 128;
        const long MaxSourceBytes = 24L * 1024 * 1024;

        public string Path { get; private set; }
        public Texture2D Thumbnail { get; private set; }

        public static CodexImageAttachment TryLoad(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxSourceBytes)
                {
                    return null;
                }

                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(File.ReadAllBytes(path)))
                {
                    Object.DestroyImmediate(source);
                    return null;
                }

                Texture2D thumbnail = Downscale(source);
                if (thumbnail != source)
                {
                    Object.DestroyImmediate(source);
                }

                if (thumbnail == null)
                {
                    return null;
                }

                thumbnail.hideFlags = HideFlags.HideAndDontSave;
                return new CodexImageAttachment
                {
                    Path = path,
                    Thumbnail = thumbnail,
                };
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        static Texture2D Downscale(Texture2D source)
        {
            if (source.width <= ThumbnailSize && source.height <= ThumbnailSize)
            {
                return source;
            }

            float scale = Mathf.Min((float)ThumbnailSize / source.width, (float)ThumbnailSize / source.height);
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var thumbnail = new Texture2D(width, height, TextureFormat.RGBA32, false);
                thumbnail.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                thumbnail.Apply(false, false);
                return thumbnail;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public void Dispose()
        {
            if (Thumbnail != null)
            {
                Object.DestroyImmediate(Thumbnail);
                Thumbnail = null;
            }
        }
    }
}
