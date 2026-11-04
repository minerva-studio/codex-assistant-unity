using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Minerva.Codex
{
    /// <summary>
    /// Inline video preview driven by Unity's VideoPlayer rendering into a
    /// RenderTexture, so clips play in the editor without entering play mode.
    /// </summary>
    internal sealed class CodexVideoRow : VisualElement
    {
        readonly string m_Path;
        readonly GameObject m_Object;
        readonly VideoPlayer m_Player;
        readonly AudioSource m_Audio;
        readonly RenderTexture m_Target;
        readonly Image m_Screen;
        readonly Button m_PlayButton;
        readonly CodexAudioScrubber m_Progress;
        readonly Label m_Time;

        bool m_Prepared;
        bool m_Playing;

        public CodexVideoRow(string path)
        {
            m_Path = path;

            m_Target = new RenderTexture(480, 270, 0) { hideFlags = HideFlags.DontSave };
            m_Object = new GameObject("CodexVideoPreview") { hideFlags = HideFlags.HideAndDontSave };
            m_Player = m_Object.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.source = VideoSource.Url;
            m_Player.url = new Uri(path).AbsoluteUri;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_Target;

            m_Audio = m_Object.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.volume = 1f;
            m_Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            m_Player.EnableAudioTrack(0, true);
            m_Player.SetTargetAudioSource(0, m_Audio);

            m_Player.prepareCompleted += OnPrepared;
            m_Player.Prepare();

            m_Screen = new Image { image = m_Target, scaleMode = ScaleMode.ScaleToFit };
            m_Screen.style.width = 320;
            m_Screen.style.height = 180;
            m_Screen.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            m_Screen.style.marginTop = 6;
            m_Screen.style.marginBottom = 4;

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;

            m_PlayButton = new Button(Toggle) { text = "▶" };
            m_PlayButton.style.width = 26;
            m_PlayButton.style.marginRight = 6;
            m_PlayButton.SetEnabled(false);

            m_Progress = new CodexAudioScrubber();
            m_Progress.style.flexBasis = 0;
            m_Progress.SeekRequested += progress =>
            {
                if (!m_Prepared)
                {
                    return;
                }

                m_Player.time = progress * m_Player.length;
                UpdateVisuals();
            };

            m_Time = new Label("--:--");
            m_Time.style.fontSize = 10;
            m_Time.style.color = CodexPalette.Muted;
            m_Time.style.width = 78;
            m_Time.style.flexShrink = 0;
            m_Time.style.marginLeft = 6;

            var name = new Label(Path.GetFileName(path));
            name.style.fontSize = 10;
            name.style.color = CodexPalette.Muted;
            name.style.width = 140;
            name.style.flexShrink = 0;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;

            controls.Add(m_PlayButton);
            controls.Add(m_Progress);
            controls.Add(m_Time);
            controls.Add(name);

            Add(m_Screen);
            Add(controls);

            EditorApplication.update += Tick;
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        void OnPrepared(VideoPlayer player)
        {
            m_Prepared = true;
            m_PlayButton.SetEnabled(true);
            m_Time.text = $"0:00 / {Format((float)player.length)}";
            player.frame = 0;
            player.Pause();
        }

        void Toggle()
        {
            if (!m_Prepared)
            {
                return;
            }

            if (m_Playing)
            {
                m_Player.Pause();
                m_Playing = false;
                m_PlayButton.text = "▶";
            }
            else
            {
                if (m_Player.time >= m_Player.length - 0.05)
                {
                    m_Player.time = 0;
                }

                m_Player.Play();
                m_Playing = true;
                m_PlayButton.text = "⏸";
            }
        }

        void Tick()
        {
            if (!m_Prepared)
            {
                return;
            }

            if (m_Playing && !m_Player.isPlaying)
            {
                m_Playing = false;
                m_PlayButton.text = "▶";
            }

            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            float length = (float)m_Player.length;
            float time = (float)m_Player.time;
            m_Progress.SetProgress(length <= 0f ? 0f : time / length);
            m_Time.text = $"{Format(time)} / {Format(length)}";
        }

        void Dispose()
        {
            EditorApplication.update -= Tick;
            try
            {
                m_Player.prepareCompleted -= OnPrepared;
                m_Player.Stop();
                m_Audio.Stop();
            }
            catch (Exception)
            {
                // Player already torn down.
            }

            UnityEngine.Object.DestroyImmediate(m_Object);
            if (m_Target != null)
            {
                m_Target.Release();
                UnityEngine.Object.DestroyImmediate(m_Target);
            }
        }

        static string Format(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
