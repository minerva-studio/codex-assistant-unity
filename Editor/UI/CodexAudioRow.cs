using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Compact audio preview: play/pause, seek bar, elapsed time. Playback goes
    /// through the editor's preview API so the same code runs on Windows and macOS.
    /// </summary>
    internal sealed class CodexAudioRow : VisualElement
    {
        const string PlayGlyph = "▶";
        const string PauseGlyph = "⏸";

        readonly string m_Path;
        readonly Button m_PlayButton;
        readonly CodexAudioScrubber m_Progress;
        readonly Label m_Time;

        AudioClip m_Clip;
        bool m_Playing;
        double m_StartedAt;
        float m_OffsetSeconds;

        public CodexAudioRow(string path)
        {
            m_Path = path;
            m_Clip = CodexAttachments.GetCachedClip(path);

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.marginTop = 6;
            style.marginBottom = 2;

            m_PlayButton = new Button(Toggle) { text = PlayGlyph };
            m_PlayButton.style.width = 26;
            m_PlayButton.style.marginRight = 6;
            m_PlayButton.SetEnabled(m_Clip != null);

            m_Progress = new CodexAudioScrubber();
            m_Progress.style.flexBasis = 0;
            m_Progress.SeekRequested += progress =>
            {
                if (m_Clip != null)
                {
                    Seek(progress * m_Clip.length);
                }
            };
            m_Progress.SetEnabled(m_Clip != null);

            m_Time = new Label(m_Clip == null ? "--:--" : $"0:00 / {Format(m_Clip.length)}");
            m_Time.style.fontSize = 10;
            m_Time.style.color = CodexPalette.Muted;
            m_Time.style.marginLeft = 6;
            m_Time.style.marginRight = 6;
            m_Time.style.width = 78;
            m_Time.style.flexShrink = 0;

            var name = new Label(Path.GetFileName(path));
            name.style.fontSize = 10;
            name.style.color = CodexPalette.Muted;
            name.style.width = 140;
            name.style.flexShrink = 0;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;

            Add(m_PlayButton);
            Add(m_Progress);
            Add(m_Time);
            Add(name);

            if (m_Clip == null && File.Exists(path))
            {
                m_Time.text = "loading...";
                CodexAttachments.LoadClipAsync(path, clip =>
                {
                    if (clip == null)
                    {
                        m_Time.text = "unreadable";
                        return;
                    }

                    m_Clip = clip;
                    m_PlayButton.SetEnabled(true);
                    m_Progress.SetEnabled(true);
                    m_Time.text = $"0:00 / {Format(clip.length)}";
                });
            }
        }

        void Toggle()
        {
            if (m_Playing)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        void Play()
        {
            if (m_Clip == null)
            {
                return;
            }

            if (m_OffsetSeconds >= m_Clip.length - 0.01f)
            {
                m_OffsetSeconds = 0f;
            }

            int sample = Mathf.Clamp(
                Mathf.RoundToInt(m_OffsetSeconds * m_Clip.frequency),
                0,
                Mathf.Max(0, m_Clip.samples - 1));

            if (!CodexAudio.Play(m_Clip, sample))
            {
                return;
            }

            m_StartedAt = EditorApplication.timeSinceStartup;
            m_Playing = true;
            m_PlayButton.text = PauseGlyph;
            EditorApplication.update += Tick;
        }

        void Pause()
        {
            m_OffsetSeconds = CurrentSeconds();
            StopPlayback();
        }

        void Seek(float seconds)
        {
            m_OffsetSeconds = Mathf.Clamp(seconds, 0f, m_Clip.length);
            if (m_Playing)
            {
                CodexAudio.Stop();
                Play();
            }
            else
            {
                UpdateVisuals(m_OffsetSeconds);
            }
        }

        float CurrentSeconds()
        {
            if (!m_Playing)
            {
                return m_OffsetSeconds;
            }

            return Mathf.Min(
                m_Clip.length,
                m_OffsetSeconds + (float)(EditorApplication.timeSinceStartup - m_StartedAt));
        }

        void Tick()
        {
            if (!m_Playing)
            {
                return;
            }

            float seconds = CurrentSeconds();
            UpdateVisuals(seconds);

            if (seconds >= m_Clip.length - 0.01f)
            {
                m_OffsetSeconds = 0f;
                StopPlayback();
            }
        }

        void UpdateVisuals(float seconds)
        {
            m_Progress.SetProgress(m_Clip.length <= 0f ? 0f : seconds / m_Clip.length);
            m_Time.text = $"{Format(seconds)} / {Format(m_Clip.length)}";
        }

        void StopPlayback()
        {
            CodexAudio.Stop();
            m_Playing = false;
            m_PlayButton.text = PlayGlyph;
            EditorApplication.update -= Tick;
            UpdateVisuals(m_OffsetSeconds);
        }

        static string Format(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }

    /// <summary>Seek bar that does not feed its own updates back as user input.</summary>
    internal sealed class CodexAudioScrubber : VisualElement
    {
        readonly VisualElement m_Fill;

        public event Action<float> SeekRequested;

        public CodexAudioScrubber()
        {
            style.height = 6;
            style.minWidth = 80;
            style.flexGrow = 1;
            style.marginLeft = 6;
            style.marginRight = 6;
            style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;

            m_Fill = new VisualElement();
            m_Fill.style.height = Length.Percent(100);
            m_Fill.style.width = Length.Percent(0);
            m_Fill.style.backgroundColor = CodexPalette.Text;
            m_Fill.style.borderTopLeftRadius = 3;
            m_Fill.style.borderBottomLeftRadius = 3;
            Add(m_Fill);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        public void SetProgress(float value)
        {
            m_Fill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            this.CapturePointer(evt.pointerId);
            Emit(evt.localPosition.x);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId))
            {
                Emit(evt.localPosition.x);
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId))
            {
                this.ReleasePointer(evt.pointerId);
            }
        }

        void Emit(float x)
        {
            float width = layout.width;
            if (width > 1f)
            {
                SeekRequested?.Invoke(Mathf.Clamp01(x / width));
            }
        }
    }

    /// <summary>Thin wrapper over Unity's editor preview audio API (cross-platform).</summary>
    internal static class CodexAudio
    {
        static MethodInfo s_Play;
        static MethodInfo s_Stop;
        static bool s_Resolved;

        public static bool Play(AudioClip clip, int startSample)
        {
            Resolve();
            if (s_Play == null)
            {
                Debug.LogWarning("[Codex] Editor audio preview API is unavailable in this Unity version.");
                return false;
            }

            try
            {
                s_Play.Invoke(null, new object[] { clip, startSample, false });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Codex] Audio preview failed: {exception.Message}");
                return false;
            }
        }

        public static void Stop()
        {
            Resolve();
            try
            {
                s_Stop?.Invoke(null, null);
            }
            catch (Exception)
            {
                // Preview is best effort.
            }
        }

        static void Resolve()
        {
            if (s_Resolved)
            {
                return;
            }

            s_Resolved = true;
            Type audioUtil = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtil == null)
            {
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            s_Play = audioUtil.GetMethod(
                "PlayPreviewClip",
                flags,
                null,
                new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                null);
            s_Stop = audioUtil.GetMethod("StopAllPreviewClips", flags);
        }
    }
}
