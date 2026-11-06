using System;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Exercises terminal rendering through the same streamed text contract used by the PTY session.</summary>
    internal static class CodexTerminalBufferSmokeTest
    {
        /// <summary>Throws on a failed rendering assertion; returns a concise report when all cases pass.</summary>
        public static string Run()
        {
            var buffer = new CodexTerminalBuffer(40, 6);
            buffer.Append("\x1b");
            buffer.Append("[2J\x1b[m\x1b[HPS C:\\Demo> \x1b[1");
            buffer.Append("C\x1b]0;PowerShell");
            buffer.Append("\a\x1b[?25h<literal>");
            Require(buffer.Text == "PS C:\\Demo>  <literal>", "split PowerShell startup sequences and literal text");

            buffer.Clear();
            buffer.Append("Downloading 10%\rDownloading 90%\x1b[K");
            Require(buffer.Text == "Downloading 90%", "carriage-return progress replaces its line");
            buffer.Append("\rDone\x1b[K");
            Require(buffer.Text == "Done", "erase removes the previous longer progress message");

            buffer.Clear();
            buffer.Append("first\r\nsecond\r\nthird\x1b[2;1HSECOND\x1b[K\x1b[1;3H\x1b[2X");
            Require(buffer.Text == "fi  t\nSECOND\nthird", "cursor addressing and erase preserve existing screen rows");
            buffer.Append("\x1b[3;1f\x1b[1A\x1b[2C!\x1b[1D?\x1b[1B\x1b[2GZ");
            Require(buffer.Text == "fi  t\nSE?OND\ntZird", "relative cursor movement and absolute column");
            buffer.Append("\x1b[2;1H\x1b[J");
            Require(buffer.Text == "fi  t\n", "erase display keeps the cursor row");

            buffer.Clear();
            buffer.Append("A中\ud83d");
            buffer.Append("\ude00e\u0301\x1b[1;8HZ");
            Require(buffer.Text == "A中😀é Z", "CJK, split surrogate and combining mark use terminal columns");
            buffer.Append("\x1b[1;3HX");
            Require(buffer.Text == "A X😀é Z", "overwriting half of a wide glyph clears its other cell");

            buffer.Clear();
            buffer.Append("ab\bX\tZ\r\nnext");
            Require(buffer.Text == "aX      Z\nnext", "backspace, tab and CRLF");
            string control = null;
            string reply = null;
            buffer.Control += value => control = value;
            buffer.Reply += value => reply = value;
            buffer.Append("\x1b]777;codex;eyJjd2QiOiJ0ZXN0In0=\x1b");
            Require(control == null, "partial OSC is retained until its terminator");
            buffer.Append("\\\x1b[6");
            buffer.Append("n");
            Require(control == "777;codex;eyJjd2QiOiJ0ZXN0In0=", "OSC event excludes its envelope");
            Require(reply == "\x1b[2;5R", "cursor-position query replies with one-based coordinates");
            Require(buffer.Text == "aX      Z\nnext", "OSC metadata and terminal queries never appear as output");
            control = null;
            buffer.Append("\x1b]777;codex;partial");
            buffer.ClearDisplay();
            buffer.Append("-prompt\a");
            Require(control == "777;codex;partial-prompt" && !buffer.Text.Contains("prompt"),
                "clearing output does not break a split prompt control message");

            buffer.Clear();
            buffer.Resize(100, 30);
            buffer.Append("\x1b[2J\x1b[m\x1b[HPS C:\\Demo> ls\r\nfile.txt\x1b[K\x1b[19;1HPS C:\\Demo> ");
            string[] positioned = buffer.Text.Split('\n');
            Require(positioned.Length == 19 && positioned[1] == "file.txt" && positioned[18] == "PS C:\\Demo>",
                "PowerShell final absolute cursor position retains the viewport row");
            buffer.Clear();
            buffer.Resize(4, 3);
            buffer.Append("1234X");
            Require(buffer.Text == "1234\nX", "printing beyond the right edge wraps once");
            buffer.Clear();
            buffer.Append("ab\nZ");
            Require(buffer.Text == "ab\n  Z", "bare LF preserves the terminal column");

            buffer.Clear();
            buffer.Resize(12, 3);
            buffer.Append("one\r\ntwo\r\nthree\r\nfour");
            Require(buffer.Text == "one\ntwo\nthree\nfour", "scrolling preserves completed rows");
            buffer.Resize(12, 2);
            Require(buffer.Text == "one\ntwo\nthree\nfour", "height reduction retains screen text in scrollback");
            buffer.Clear();
            for (int i = 0; i < 24000; i++) buffer.Append("line " + i.ToString("D5") + "\r\n");
            string capped = buffer.Text;
            Require(capped.Length <= 200000 && !capped.Contains("line 00000") && capped.Contains("line 23999"),
                "scrollback stays bounded while retaining recent complete lines");
            Require(capped.StartsWith("line ", StringComparison.Ordinal), "history eviction never retains a partial old row");
            buffer.Clear();
            buffer.Append("a" + new string('\u0301', 2048));
            Require(buffer.Text.Length <= 64, "combining marks cannot grow one cell without bound");
            buffer.Clear();
            buffer.Resize(100, 50);
            var dense = new System.Text.StringBuilder();
            string cluster = "a" + new string('\u0301', 63);
            for (int row = 0; row < 40; row++)
            {
                for (int column = 0; column < 100; column++) dense.Append(cluster);
                dense.Append("\r\n");
            }
            dense.Append("latest");
            buffer.Append(dense.ToString());
            Require(buffer.Text.Length <= 200000 && buffer.Text.EndsWith("latest", StringComparison.Ordinal),
                "actual visible combining text counts toward the output cap and retains recent output");
            buffer.Clear();
            buffer.Append("clean");
            Require(buffer.Text == "clean", "clear resets parser, history and cursor");
            return "PASS: streamed VT sequences, progress overwrite, cursor/erase, literal Unicode, OSC/reply, resizing and bounded scrollback";
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Terminal smoke: " + message);
        }
    }
}
