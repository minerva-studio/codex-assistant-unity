using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEditor;

namespace Minerva.Codex
{
    /// <summary>
    /// Runs one app-server WebSocket listener per Codex home and reconnects to it.
    /// The listener is a separate process, so a Unity domain reload only closes the
    /// client socket; a running turn keeps going in the listener.
    /// </summary>
    internal static class CodexWebSocketHost
    {
        static readonly List<int> s_StartedPids = new List<int>();

        static CodexWebSocketHost()
        {
            EditorApplication.quitting += StopStarted;
        }

        public static bool TryConnect(
            CodexLaunchSpec spec,
            string workingDirectory,
            string codexHome,
            bool restart,
            StringBuilder stderr,
            out ClientWebSocket socket)
        {
            socket = null;
            string homeKey = HomeKey(codexHome);
            string portKey = PortKey(homeKey);
            string pidKey = PidKey(homeKey);
            int port = EditorPrefs.GetInt(portKey, 0);
            int pid = EditorPrefs.GetInt(pidKey, 0);

            if (restart)
            {
                Stop(pid);
                EditorPrefs.DeleteKey(portKey);
                EditorPrefs.DeleteKey(pidKey);
                port = 0;
                pid = 0;
            }

            if (port > 0 && TryOpen(port, out socket))
            {
                Remember(pid);
                return true;
            }

            if (pid > 0)
            {
                Stop(pid);
            }

            port = FindFreePort();
            if (!StartServer(spec, workingDirectory, codexHome, port, stderr, out Process server))
            {
                return false;
            }

            pid = server.Id;
            try
            {
                for (int attempt = 0; attempt < 75; attempt++)
                {
                    if (TryOpen(port, out socket))
                    {
                        EditorPrefs.SetInt(portKey, port);
                        EditorPrefs.SetInt(pidKey, pid);
                        Remember(pid);
                        return true;
                    }

                    if (server.HasExited)
                    {
                        AppendDiagnostics(stderr, "[websocket] app-server exited during startup");
                        return false;
                    }

                    Thread.Sleep(200);
                }

                AppendDiagnostics(stderr, "[websocket] app-server did not start listening");
                Stop(pid);
                return false;
            }
            finally
            {
                server.Dispose();
            }
        }

        static bool StartServer(
            CodexLaunchSpec spec,
            string workingDirectory,
            string codexHome,
            int port,
            StringBuilder stderr,
            out Process process)
        {
            process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = spec.FileName,
                    Arguments = spec.ArgumentsFor($"app-server --listen ws://127.0.0.1:{port}"),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (!string.IsNullOrWhiteSpace(codexHome))
                {
                    startInfo.Environment["CODEX_HOME"] = codexHome;
                }

                var server = new Process { StartInfo = startInfo };
                if (!server.Start())
                {
                    return false;
                }

                process = server;
                return true;
            }
            catch (Exception exception)
            {
                AppendDiagnostics(stderr, $"[websocket] failed to start app-server: {exception.Message}");
                return false;
            }
        }

        static bool TryOpen(int port, out ClientWebSocket socket)
        {
            socket = null;
            var candidate = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(1200);
            try
            {
                candidate.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token)
                    .GetAwaiter()
                    .GetResult();
                socket = candidate;
                return true;
            }
            catch (Exception)
            {
                candidate.Dispose();
                return false;
            }
        }

        static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        static void Remember(int pid)
        {
            if (pid <= 0)
            {
                return;
            }

            if (!s_StartedPids.Contains(pid))
            {
                s_StartedPids.Add(pid);
            }
        }

        static void StopStarted()
        {
            foreach (int pid in s_StartedPids)
            {
                Stop(pid);
            }

            s_StartedPids.Clear();
        }

        static void Stop(int pid)
        {
            if (pid <= 0)
            {
                return;
            }

            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.ProcessName.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Already gone.
            }
        }

        static string HomeKey(string codexHome)
        {
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                return "default";
            }

            uint hash = 2166136261;
            foreach (char character in codexHome.ToLowerInvariant())
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }

        static string PortKey(string homeKey) => $"Minerva.Codex.WebSocket.{homeKey}.Port";

        static string PidKey(string homeKey) => $"Minerva.Codex.WebSocket.{homeKey}.Pid";

        static void AppendDiagnostics(StringBuilder stderr, string message)
        {
            lock (stderr)
            {
                stderr.AppendLine(message);
                if (stderr.Length > 16000)
                {
                    stderr.Remove(0, stderr.Length - 16000);
                }
            }
        }
    }
}
