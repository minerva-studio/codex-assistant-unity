using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>
    /// Compact audio preview: play/pause, seek bar, elapsed time. Uses the editor's
    /// preview system so clips play without entering play mode.
    /// </summary>
    internal sealed class CodexAudioRow : VisualElement
    {
        const string PlayGlyph = "▶";
        const string PauseGlyph = "⏸";

        readonly string m_Path;
        AudioClip m_Clip;
        readonly Button m_PlayButton;
        readonly CodexAudioScrubber m_Progress;
        readonly Label m_Time;
        readonly Slider m_Volume;

        bool m_Playing;
        bool m_Updating;
        double m_StartedAt;
        float m_OffsetSeconds;

        public CodexAudioRow(string path)
        {
            m_Path = path;
            m_Clip = CodexAttachments.LoadClip(path);

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.marginTop = 6;
            style.marginBottom = 2;

            m_PlayButton = new Button(Toggle) { text = PlayGlyph };
            m_PlayButton.style.width = 26;
            m_PlayButton.style.marginRight = 6;
            m_PlayButton.SetEnabled(m_Clip != null);

            m_Progress = new CodexAudioScrubber();
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
            m_Time.style.minWidth = 78;

            var name = new Label(Path.GetFileName(path));
            name.style.fontSize = 10;
            name.style.color = CodexPalette.Muted;
            name.style.maxWidth = 90;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;

            m_Volume = new Slider(0f, 1f) { value = CodexAudio.Volume };
            m_Volume.style.width = 70;
            m_Volume.style.flexShrink = 0;
            m_Volume.style.marginLeft = 6;
            m_Volume.SetEnabled(CodexAudio.SupportsVolume(path));
            m_Volume.tooltip = CodexAudio.SupportsVolume(path)
                ? "Volume"
                : "Volume applies to WAV previews";
            m_Volume.RegisterValueChangedCallback(evt =>
            {
                CodexAudio.SetVolume(evt.newValue);
            });

            Add(m_PlayButton);
            Add(m_Progress);
            Add(m_Time);
            Add(name);
            Add(m_Volume);

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

            if (CodexAudio.Muted)
            {
                Add(BuildMuteHint());
            }
        }

        VisualElement BuildMuteHint()
        {
            var hint = new Label("editor audio muted");
            hint.style.fontSize = 9;
            hint.style.color = CodexPalette.Error;
            hint.style.marginLeft = 6;

            var unmute = new Button(() =>
            {
                CodexAudio.Muted = false;
                hint.RemoveFromHierarchy();
            })
            {
                text = "unmute",
            };
            unmute.style.fontSize = 9;
            unmute.style.marginLeft = 4;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.Add(hint);
            row.Add(unmute);
            return row;
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

            int sample = Mathf.Clamp(Mathf.RoundToInt(m_OffsetSeconds * m_Clip.frequency), 0, m_Clip.samples - 1);
            if (!CodexAudio.Play(m_Path, m_Clip, sample))
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
            if (!CodexAudio.CanSeek)
            {
                return;
            }

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

    /// <summary>Thin wrapper over the editor's preview audio API.</summary>
    internal static class CodexAudio
    {
        static MethodInfo s_Play;
        static MethodInfo s_Stop;
        static MethodInfo s_IsPlaying;
        static bool s_Resolved;
        static bool s_UsingWin32;

        /// <summary>Seeking works on both backends: winmm restarts from a trimmed buffer.</summary>
        public static bool CanSeek => true;

        public static bool Muted
        {
            get => EditorUtility.audioMasterMute;
            set => EditorUtility.audioMasterMute = value;
        }

        const string VolumeKey = "Minerva.Codex.AudioVolume";
        static float s_Volume = -1f;

        public static float Volume
        {
            get
            {
                if (s_Volume < 0f)
                {
                    s_Volume = EditorPrefs.GetFloat(VolumeKey, 0.8f);
                }

                return s_Volume;
            }
            set
            {
                s_Volume = Mathf.Clamp01(value);
                EditorPrefs.SetFloat(VolumeKey, s_Volume);
            }
        }

        public static bool SupportsVolume(string path)
        {
            return IsWav(path);
        }

        /// <summary>Applies volume to the running preview without restarting it.</summary>
        public static void SetVolume(float value)
        {
            Volume = value;
#if UNITY_EDITOR_WIN
            if (s_MciOpen)
            {
                Mci($"setaudio {MciAlias} volume to {Mathf.RoundToInt(Volume * 1000f)}");
            }
#endif
        }

        public static bool Play(string path, AudioClip clip, int startSample)
        {
            float startSeconds = clip.frequency > 0 ? (float)startSample / clip.frequency : 0f;
#if UNITY_EDITOR_WIN
            if (IsWav(path) && PlayWin32(path, startSeconds))
            {
                s_UsingWin32 = true;
                Debug.Log($"[CodexAudio] win32 play {Path.GetFileName(path)} at {startSeconds:F2}s");
                return true;
            }
#endif
            s_UsingWin32 = false;

            Resolve();
            if (s_Play == null)
            {
                Debug.LogWarning("[Codex] Editor audio preview API is unavailable in this Unity version.");
                return false;
            }

            try
            {
                s_Play.Invoke(null, new object[] { clip, startSample, false });
                bool playing = s_IsPlaying != null && (bool)s_IsPlaying.Invoke(null, null);
                Debug.Log($"[CodexAudio] play {clip.name} length={clip.length:F2}s muted={Muted} playing={playing}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CodexAudio] play failed: {exception.Message}");
                return false;
            }
        }

        public static void Stop()
        {
#if UNITY_EDITOR_WIN
            if (s_UsingWin32)
            {
                MciClose();
                s_UsingWin32 = false;
            }
#endif

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

        static bool IsWav(string path)
        {
            return path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

#if UNITY_EDITOR_WIN
        const uint SND_ASYNC = 0x0001;
        const uint SND_NODEFAULT = 0x0002;
        const uint SND_MEMORY = 0x0004;

        static GCHandle s_PinnedWav;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

        static bool s_MciOpen;
        const string MciAlias = "codexaudio";

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder result, int length, IntPtr callback);

        static int Mci(string command)
        {
            try
            {
                return mciSendString(command, null, 0, IntPtr.Zero);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CodexAudio] mci failed: {exception.Message}");
                return -1;
            }
        }

        static void MciClose()
        {
            if (!s_MciOpen)
            {
                return;
            }

            Mci($"stop {MciAlias}");
            Mci($"close {MciAlias}");
            s_MciOpen = false;
        }

        static bool PlayWin32(string path, float startSeconds)
        {
            MciClose();

            if (Mci($"open \"{path}\" type waveaudio alias {MciAlias}") != 0)
            {
                Debug.LogWarning($"[CodexAudio] mci cannot open {path}");
                return false;
            }

            s_MciOpen = true;
            Mci($"set {MciAlias} time format milliseconds");
            Mci($"setaudio {MciAlias} volume to {Mathf.RoundToInt(Volume * 1000f)}");

            int from = Mathf.Max(0, Mathf.RoundToInt(startSeconds * 1000f));
            if (Mci($"play {MciAlias} from {from}") != 0)
            {
                MciClose();
                return false;
            }

            return true;
        }

        static void ReleasePin()
        {
            if (s_PinnedWav.IsAllocated)
            {
                s_PinnedWav.Free();
            }
        }

        /// <summary>Scales 16-bit PCM in place; other bit depths keep their original volume.</summary>
        static void ScaleWav16(byte[] wav, float volume)
        {
            if (wav.Length < 44 || BitConverter.ToInt16(wav, 34) != 16)
            {
                return;
            }

            int offset = 12;
            while (offset + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, offset, 4);
                int size = BitConverter.ToInt32(wav, offset + 4);
                if (id == "data")
                {
                    int start = offset + 8;
                    int end = Math.Min(start + size, wav.Length - 1);
                    for (int i = start; i + 1 < end; i += 2)
                    {
                        short sample = BitConverter.ToInt16(wav, i);
                        sample = (short)(sample * volume);
                        wav[i] = (byte)(sample & 0xFF);
                        wav[i + 1] = (byte)((sample >> 8) & 0xFF);
                    }

                    return;
                }

                offset += 8 + size + (size & 1);
            }
        }

        /// <summary>Rebuilds a WAV that starts at the given offset so winmm can seek.</summary>
        static byte[] TrimWav(byte[] source, float startSeconds)
        {
            if (source.Length < 44 ||
                Encoding.ASCII.GetString(source, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(source, 8, 4) != "WAVE")
            {
                return null;
            }

            int format = 1;
            int channels = 0;
            int sampleRate = 0;
            int bits = 0;
            int dataOffset = -1;
            int dataLength = 0;
            byte[] formatChunk = null;

            int offset = 12;
            while (offset + 8 <= source.Length)
            {
                string id = Encoding.ASCII.GetString(source, offset, 4);
                int size = BitConverter.ToInt32(source, offset + 4);
                int body = offset + 8;

                if (id == "fmt " && body + 16 <= source.Length)
                {
                    format = BitConverter.ToInt16(source, body);
                    channels = BitConverter.ToInt16(source, body + 2);
                    sampleRate = BitConverter.ToInt32(source, body + 4);
                    bits = BitConverter.ToInt16(source, body + 14);
                    formatChunk = new byte[16];
                    Buffer.BlockCopy(source, body, formatChunk, 0, 16);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(size, source.Length - body);
                    break;
                }

                offset = body + size + (size & 1);
            }

            int blockAlign = channels * bits / 8;
            if (dataOffset < 0 || sampleRate <= 0 || blockAlign <= 0 || formatChunk == null)
            {
                return null;
            }

            int frames = (int)(startSeconds * sampleRate);
            int skip = frames * blockAlign;
            if (skip >= dataLength)
            {
                skip = 0;
            }

            int length = dataLength - skip;
            length -= length % blockAlign;
            if (length <= 0)
            {
                return null;
            }

            var output = new byte[44 + length];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(output, 0);
            BitConverter.GetBytes(36 + length).CopyTo(output, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(output, 8);
            BitConverter.GetBytes(16).CopyTo(output, 16);
            formatChunk.CopyTo(output, 20);
            Encoding.ASCII.GetBytes("data").CopyTo(output, 36);
            BitConverter.GetBytes(length).CopyTo(output, 40);
            Buffer.BlockCopy(source, dataOffset + skip, output, 44, length);
            return output;
        }
#endif

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
            s_IsPlaying = audioUtil.GetMethod("IsPreviewClipPlaying", flags);
        }
    }
}
