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
        static AudioSource s_AudioInit;
        static AudioClip s_AudioInitClip;
        static bool s_PreviousMute;

        readonly string m_Path;
        readonly GameObject m_Object;
        readonly VideoPlayer m_Player;
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

            // Direct output, not AudioSource: an AudioSource does not run in edit mode,
            // so routing the track into one produces silence.
            m_Player.audioOutputMode = VideoAudioOutputMode.Direct;
            m_Player.SetDirectAudioVolume(0, 1f);

            EnsureAudioInitialized();

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

        /// <summary>
        /// The editor audio system is not initialized in edit mode until something plays.
        /// A dummy clip kicks it awake so VideoPlayer direct audio is audible.
        /// </summary>
        static void EnsureAudioInitialized()
        {
            if (s_AudioInit != null)
            {
                return;
            }

            s_AudioInitClip = AudioClip.Create("CodexAudioInit", 4410, 1, 44100, false);
            var host = new GameObject("CodexAudioInit") { hideFlags = HideFlags.HideAndDontSave };
            s_AudioInit = host.AddComponent<AudioSource>();
            s_AudioInit.clip = s_AudioInitClip;
            s_AudioInit.playOnAwake = false;
            s_AudioInit.volume = 0f;
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
                if (s_PreviousMute)
                {
                    EditorUtility.audioMasterMute = true;
                    s_PreviousMute = false;
                }
            }
            else
            {
                if (m_Player.time >= m_Player.length - 0.05)
                {
                    m_Player.time = 0;
                }

                s_PreviousMute = EditorUtility.audioMasterMute;
                EditorUtility.audioMasterMute = false;
                s_AudioInit?.Play();
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
                if (s_PreviousMute)
                {
                    EditorUtility.audioMasterMute = true;
                    s_PreviousMute = false;
                }
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
