using System;
using System.Diagnostics;
using System.Text;

namespace Minerva.Codex
{
    /// <summary>Starts <c>codex app-server</c> and keeps its stderr drained.</summary>
    internal static class CodexAppServerProcess
    {
        public static Process Start(
            CodexLaunchSpec spec,
            string workingDirectory,
            string codexHome,
            StringBuilder stderr)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            if (!string.IsNullOrWhiteSpace(codexHome))
            {
                // app-server reads its config, auth and threads from this directory.
                startInfo.Environment["CODEX_HOME"] = codexHome;
            }

            var process = new Process { StartInfo = startInfo };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                {
                    return;
                }

                lock (stderr)
                {
                    stderr.AppendLine(args.Data);
                    if (stderr.Length > 16000)
                    {
                        stderr.Remove(0, stderr.Length - 16000);
                    }
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start {spec.FileName}");
            }

            process.BeginErrorReadLine();
            return process;
        }
    }
}
