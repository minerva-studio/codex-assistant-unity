using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Minerva.Codex
{
    internal enum CodexShellState { Idle, Starting, Ready, Executing, Stopping, Exited, Disconnected }

    /// <summary>
    /// Owns one connection-scoped PTY shell, its output and command history.
    /// Closing the view leaves the shell alive; disconnecting its workspace ends it.
    /// </summary>
    internal sealed class CodexTerminalSession
    {
        const string PromptControl = "777;codex;";
        readonly CodexClient m_Client;
        readonly CodexTerminalBuffer m_Buffer = new CodexTerminalBuffer();
        readonly Decoder m_Stdout = Encoding.UTF8.GetDecoder();
        readonly Decoder m_Stderr = Encoding.UTF8.GetDecoder();
        readonly List<string> m_History = new List<string>();
        string m_Process;
        int m_Generation;
        bool m_Registered;
        int m_Columns = 100;
        int m_Rows = 30;
        (string Directory, bool Local)? m_Restart;

        public event Action Changed;
        public CodexShellState State { get; private set; }
        public string Directory { get; private set; }
        public bool Local { get; private set; }
        public string Error { get; private set; }
        public int? ExitCode { get; private set; }
        public bool? LastCommandSucceeded { get; private set; }
        public string Shell => Application.platform == RuntimePlatform.WindowsEditor ? "PowerShell" : "bash";
        public string Output => m_Buffer.Text;
        public IReadOnlyList<string> History => m_History;
        public bool CanInput => State == CodexShellState.Ready || State == CodexShellState.Executing;
        bool Alive => State == CodexShellState.Starting || CanInput || State == CodexShellState.Stopping;

        public CodexTerminalSession(CodexClient client)
        {
            m_Client = client;
            m_Buffer.Control += OnControl;
            m_Buffer.Reply += WriteRaw;
        }

        /// <summary>Starts a shell, or restarts the existing shell after its termination is confirmed.</summary>
        /// <param name="directory">Absolute initial working directory on the app-server host.</param>
        /// <param name="local">True for a host process; false for the configured server sandbox.</param>
        public void Start(string directory, bool local)
        {
            if (State == CodexShellState.Disconnected) return;
            if (Alive)
            {
                m_Restart = (directory, local);
                if (State == CodexShellState.Stopping) return;
                var previous = State;
                State = CodexShellState.Stopping;
                if (m_Registered) StopForRestart(previous);
                Changed?.Invoke();
                return;
            }

            Begin(directory, local);
        }

        void StopForRestart(CodexShellState previous)
        {
            int generation = m_Generation;
            Action<string> failed = message =>
            {
                if (generation != m_Generation || State != CodexShellState.Stopping) return;
                m_Restart = null;
                State = previous;
                Error = message;
                Changed?.Invoke();
            };
            if (Local) m_Client.KillProcess(m_Process, null, failed);
            else m_Client.TerminateCommand(m_Process, null, failed);
        }

        void Begin(string directory, bool local)
        {
            Directory = directory;
            Local = local;
            Error = null;
            ExitCode = null;
            LastCommandSucceeded = null;
            m_Registered = false;
            m_Stdout.Reset();
            m_Stderr.Reset();
            m_Buffer.Clear();
            m_Buffer.Resize(m_Columns, m_Rows);
            m_Process = "terminal-" + Guid.NewGuid().ToString("N");
            int generation = ++m_Generation;
            State = CodexShellState.Starting;
            Changed?.Invoke();

            var environment = new JObject { ["TERM"] = "xterm-256color" };
            JArray shell;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                const string script = "Remove-Module PSReadLine -ErrorAction SilentlyContinue; " +
                    "function global:prompt { $ok=$?; $status=if($ok){'0'}else{'1'}; " +
                    "$dir=(Get-Location).Path; " +
                    "[Console]::Write([char]27+']777;codex;'+$status+':'+" +
                    "[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($dir))+[char]7); " +
                    "return 'PS '+$dir+'> ' }";
                shell = new JArray("powershell.exe", "-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand",
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            }
            else
            {
                environment["PS1"] = @"\w $ ";
                environment["PROMPT_COMMAND"] = "__codex_status=$?; printf '\\033]777;codex;%s:%s\\007' " +
                    "\"$__codex_status\" \"$(printf '%s' \"$PWD\" | base64 | tr -d '\\r\\n')\"";
                shell = new JArray("/bin/bash", "--noprofile", "--norc", "-i");
            }

            Action<string> onError = message => Finish(generation, null, message);
            if (local)
                m_Client.StartLocalShell(shell, directory, m_Process, environment, m_Columns, m_Rows, _ => Register(generation), onError);
            else
                m_Client.StartSandboxedShell(shell, directory, m_Process, environment, m_Columns, m_Rows, result => Finish(generation, result, null), onError);
        }

        /// <summary>Submits a command at a shell prompt, or a line of stdin while a program is running.</summary>
        public bool Send(string text)
        {
            if (!CanInput) return false;
            if (State == CodexShellState.Ready)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    m_History.Add(text);
                    if (m_History.Count > 100) m_History.RemoveAt(0);
                }
                State = CodexShellState.Executing;
                LastCommandSucceeded = null;
            }

            Error = null;
            WriteRaw(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r") + "\r");
            Changed?.Invoke();
            return true;
        }

        /// <summary>Sends the PTY interrupt character without terminating the shell session.</summary>
        public void Interrupt()
        {
            if (!CanInput) return;
            State = CodexShellState.Executing;
            WriteRaw("\u0003");
            Changed?.Invoke();
        }

        /// <summary>Clears displayed output without clearing the shell environment or command history.</summary>
        public void Clear()
        {
            m_Buffer.ClearDisplay();
            Changed?.Invoke();
        }

        /// <summary>Resizes the renderer and the registered PTY using the same character-cell dimensions.</summary>
        public void Resize(int columns, int rows)
        {
            columns = Math.Max(20, Math.Min(500, columns));
            rows = Math.Max(5, Math.Min(200, rows));
            if (columns == m_Columns && rows == m_Rows) return;
            m_Columns = columns;
            m_Rows = rows;
            m_Buffer.Resize(columns, rows);
            ResizeRemote();
        }

        void Register(int generation)
        {
            if (generation != m_Generation || !Alive || m_Registered) return;
            m_Registered = true;
            if (State == CodexShellState.Stopping) StopForRestart(CodexShellState.Starting);
            else ResizeRemote();
        }

        void ResizeRemote()
        {
            if (!m_Registered || !Alive) return;
            int generation = m_Generation;
            m_Client.ResizeShell(m_Process, Local, m_Columns, m_Rows, message => Report(generation, message));
        }

        void WriteRaw(string input)
        {
            if (!Alive) return;
            int generation = m_Generation;
            if (Local) m_Client.WriteProcessInput(m_Process, input, null, message => Report(generation, message));
            else m_Client.WriteCommandInput(m_Process, input, null, message => Report(generation, message));
        }

        void Report(int generation, string message)
        {
            if (generation != m_Generation || !Alive) return;
            Error = message;
            Changed?.Invoke();
        }

        void OnControl(string control)
        {
            if (!control.StartsWith(PromptControl, StringComparison.Ordinal)) return;
            string payload = control.Substring(PromptControl.Length);
            int separator = payload.IndexOf(':');
            if (separator < 0 || !int.TryParse(payload.Substring(0, separator), out int status)) return;
            try
            {
                Directory = Encoding.UTF8.GetString(Convert.FromBase64String(payload.Substring(separator + 1)));
                LastCommandSucceeded = status == 0;
                if (State != CodexShellState.Stopping) State = CodexShellState.Ready;
            }
            catch (FormatException) { }
        }

        /// <summary>Consumes only output whose connection-scoped id belongs to this shell.</summary>
        public bool HandleOutput(JObject parameters)
        {
            string id = parameters[Local ? "processHandle" : "processId"]?.Value<string>();
            if (!Alive || id != m_Process) return false;
            Register(m_Generation);
            string encoded = parameters["deltaBase64"]?.Value<string>();
            if (!string.IsNullOrEmpty(encoded))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(encoded);
                    var characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
                    Decoder decoder = parameters["stream"]?.Value<string>() == "stderr" ? m_Stderr : m_Stdout;
                    int count = decoder.GetChars(bytes, 0, bytes.Length, characters, 0, false);
                    m_Buffer.Append(new string(characters, 0, count));
                }
                catch (FormatException) { }
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Consumes a local-shell exit; sandboxed shells finish through their command/exec response.</summary>
        public bool HandleExit(JObject parameters)
        {
            if (!Local || !Alive || parameters["processHandle"]?.Value<string>() != m_Process) return false;
            Finish(m_Generation, parameters, null);
            return true;
        }

        void Finish(int generation, JObject result, string error)
        {
            if (generation != m_Generation || State == CodexShellState.Disconnected || !Alive) return;
            var characters = new char[8];
            foreach (Decoder decoder in new[] { m_Stdout, m_Stderr })
            {
                int count = decoder.GetChars(Array.Empty<byte>(), 0, 0, characters, 0, true);
                if (count > 0) m_Buffer.Append(new string(characters, 0, count));
            }
            m_Buffer.Append(result?["stdout"]?.Value<string>() ?? "");
            m_Buffer.Append(result?["stderr"]?.Value<string>() ?? "");
            ExitCode = result?["exitCode"]?.Value<int?>();
            Error = error;
            State = CodexShellState.Exited;
            m_Process = null;
            m_Registered = false;
            Changed?.Invoke();
            if (m_Restart.HasValue)
            {
                var restart = m_Restart.Value;
                m_Restart = null;
                Begin(restart.Directory, restart.Local);
            }
        }

        /// <summary>Invalidates all callbacks after the owning connection is closed or replaced.</summary>
        public void Disconnect(string reason)
        {
            ++m_Generation;
            m_Process = null;
            m_Restart = null;
            m_Registered = false;
            State = CodexShellState.Disconnected;
            Error = reason;
            Changed?.Invoke();
        }
    }
}
