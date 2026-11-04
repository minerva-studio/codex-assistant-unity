using System;
using System.Collections.Generic;
using System.IO;

namespace Minerva.Codex
{
    /// <summary>What to launch and with which arguments.</summary>
    internal sealed class CodexLaunchSpec
    {
        public string FileName;
        public string Arguments;
        public string ResolvedPath;
        public bool IsBatch;

        public string ArgumentsFor(string command)
        {
            return IsBatch ? $"/c \"\"{ResolvedPath}\" {command}\"" : command;
        }

        public override string ToString()
        {
            return ResolvedPath != null
                ? $"{ResolvedPath} ({FileName} {Arguments})"
                : $"{FileName} {Arguments}";
        }
    }

    /// <summary>
    /// Finds the Codex executable. The Unity Editor does not inherit a shell
    /// environment, so PATH alone is not enough on macOS.
    /// </summary>
    internal static class CodexPaths
    {
        public const string OverrideEnvironmentVariable = "CODEX_ASSISTANT_PATH";
        public const string AppServerArguments = "app-server";

        public static CodexLaunchSpec Resolve()
        {
            string overridden = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
            if (!string.IsNullOrEmpty(overridden) && File.Exists(overridden))
            {
                return ForPath(overridden);
            }

            foreach (string candidate in CandidatePaths())
            {
                if (File.Exists(candidate))
                {
                    return ForPath(candidate);
                }
            }

            return new CodexLaunchSpec
            {
                FileName = "codex",
                Arguments = AppServerArguments,
            };
        }

        static IEnumerable<string> CandidatePaths()
        {
            string[] names = { "codex.exe", "codex.cmd", "codex.bat", "codex" };
            foreach (string directory in PathDirectories())
            {
                foreach (string name in names)
                {
                    string candidate = null;
                    try
                    {
                        candidate = Path.Combine(directory, name);
                    }
                    catch (ArgumentException)
                    {
                        // Malformed PATH entry; ignore it.
                    }

                    if (!string.IsNullOrEmpty(candidate))
                    {
                        yield return candidate;
                    }
                }
            }

            foreach (string known in KnownLocations())
            {
                yield return known;
            }
        }

        static IEnumerable<string> PathDirectories()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            foreach (string entry in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(entry))
                {
                    yield return entry.Trim().Trim('"');
                }
            }
        }

        static IEnumerable<string> KnownLocations()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            }

            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin", "codex");
                yield return Path.Combine(home, ".npm-global", "bin", "codex");
                yield return Path.Combine(home, "bin", "codex");
            }

            yield return "/usr/local/bin/codex";
            yield return "/opt/homebrew/bin/codex";
        }

        static CodexLaunchSpec ForPath(string path)
        {
            string extension = Path.GetExtension(path);
            bool batchScript =
                string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);

            if (batchScript)
            {
                string commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
                if (string.IsNullOrEmpty(commandProcessor))
                {
                    commandProcessor = "cmd.exe";
                }

                return new CodexLaunchSpec
                {
                    FileName = commandProcessor,
                    Arguments = $"/c \"\"{path}\" {AppServerArguments}\"",
                    ResolvedPath = path,
                    IsBatch = true,
                };
            }

            return new CodexLaunchSpec
            {
                FileName = path,
                Arguments = AppServerArguments,
                ResolvedPath = path,
            };
        }
    }
}
