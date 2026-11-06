using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Minerva.Codex
{
    /// <summary>Projects streamed terminal output into plain text, retaining a bounded scrollback.</summary>
    /// <remarks>
    /// This buffer owns display state only. LF preserves the column; CRLF starts a new line.
    /// Styling and private modes are ignored. Control payloads larger than 64 KiB are discarded.
    /// Unicode widths cover common CJK and emoji; joined emoji are not composed into a single cell.
    /// Text is capped at 200,000 UTF-16 units; a cell retains at most 64 units including combining marks.
    /// </remarks>
    internal sealed class CodexTerminalBuffer
    {
        const int TextLimit = 200000;
        const int SequenceLimit = 65536;
        const int CellTextLimit = 64;

        enum ParserState { Text, Escape, EscapeIntermediate, Csi, Osc, OscEscape, String, StringEscape }

        readonly List<string[]> m_Screen = new List<string[]>();
        readonly Queue<string> m_History = new Queue<string>();
        readonly StringBuilder m_Sequence = new StringBuilder();
        int m_Columns;
        int m_Rows;
        int m_Column;
        int m_Row;
        int m_HistoryLength;
        ParserState m_State;
        char m_HighSurrogate;
        bool m_SequenceOverflow;

        /// <summary>Receives complete OSC payloads, without introducer or terminator.</summary>
        public event Action<string> Control;

        /// <summary>Receives terminal response bytes to write back to the PTY.</summary>
        public event Action<string> Reply;

        /// <summary>Creates a terminal viewport with 1–500 columns and 1–200 rows.</summary>
        public CodexTerminalBuffer(int columns = 100, int rows = 30)
        {
            Resize(columns, rows);
        }

        /// <summary>Returns scrollback and visible rows as literal text, without unused trailing screen rows.</summary>
        public string Text
        {
            get
            {
                var result = new StringBuilder();
                foreach (string line in m_History) result.Append(line).Append('\n');
                int last = m_Row;
                for (int i = m_Rows - 1; i > last; i--)
                    if (RowLength(m_Screen[i]) != 0) { last = i; break; }
                for (int i = 0; i <= last; i++)
                {
                    if (i > 0) result.Append('\n');
                    result.Append(RenderRow(m_Screen[i]));
                }
                return result.ToString();
            }
        }

        /// <summary>Consumes an output chunk. Escape sequences and surrogate pairs may span chunks.</summary>
        /// <exception cref="ArgumentNullException">The chunk is null.</exception>
        public void Append(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            foreach (char value in text) Consume(value);
            TrimHistory();
        }

        /// <summary>Changes the viewport without reflowing rows; shrinking height moves top rows into scrollback.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Columns are outside 1–500 or rows outside 1–200.</exception>
        public void Resize(int columns, int rows)
        {
            if (columns <= 0 || columns > 500) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0 || rows > 200) throw new ArgumentOutOfRangeException(nameof(rows));
            // Unused rows below the cursor are viewport space, not scrollback.
            while (m_Screen.Count > rows && m_Screen.Count - 1 > m_Row && RowLength(m_Screen[m_Screen.Count - 1]) == 0)
                m_Screen.RemoveAt(m_Screen.Count - 1);
            while (m_Screen.Count > rows)
            {
                Remember(m_Screen[0]);
                m_Screen.RemoveAt(0);
                m_Row = Math.Max(0, m_Row - 1);
            }
            for (int i = 0; i < m_Screen.Count; i++)
            {
                string[] line = m_Screen[i];
                Array.Resize(ref line, columns);
                if (line[columns - 1] != null && line[columns - 1].Length > 0 && CellWidth(line[columns - 1]) == 2)
                    line[columns - 1] = null;
                m_Screen[i] = line;
            }
            m_Columns = columns;
            m_Rows = rows;
            while (m_Screen.Count < rows) m_Screen.Add(new string[columns]);
            m_Column = Math.Min(m_Column, columns - 1);
            m_Row = Math.Min(m_Row, rows - 1);
            TrimHistory();
        }

        /// <summary>Clears scrollback, visible content, cursor position and incomplete parser input.</summary>
        public void Clear()
        {
            ClearDisplay();
            m_Column = m_Row = 0;
            m_State = ParserState.Text;
            m_Sequence.Clear();
            m_HighSurrogate = '\0';
            m_SequenceOverflow = false;
        }

        /// <summary>Clears visible content and scrollback while retaining the remote cursor and incomplete control sequence.</summary>
        public void ClearDisplay()
        {
            m_History.Clear();
            m_HistoryLength = 0;
            foreach (string[] line in m_Screen) Array.Clear(line, 0, line.Length);
        }

        void Consume(char value)
        {
            switch (m_State)
            {
                case ParserState.Escape:
                    m_Sequence.Clear();
                    m_SequenceOverflow = false;
                    switch (value)
                    {
                        case '[': m_State = ParserState.Csi; return;
                        case ']': m_State = ParserState.Osc; return;
                        case 'P': case '^': case '_': m_State = ParserState.String; return;
                        case 'D': LineFeed(); break;
                        case 'E': m_Column = 0; LineFeed(); break;
                        case 'c': Clear(); break;
                    }
                    if (value >= ' ' && value <= '/') { m_State = ParserState.EscapeIntermediate; return; }
                    m_State = ParserState.Text;
                    return;
                case ParserState.EscapeIntermediate:
                    if (value >= '0' && value <= '~') m_State = ParserState.Text;
                    else if (value == '\x1b') m_State = ParserState.Escape;
                    return;
                case ParserState.Csi:
                    if (value >= '@' && value <= '~')
                    {
                        m_State = ParserState.Text;
                        if (!m_SequenceOverflow) ApplyCsi(value, m_Sequence.ToString());
                        m_Sequence.Clear();
                    }
                    else if (value == '\x1b') m_State = ParserState.Escape;
                    else Collect(value);
                    return;
                case ParserState.Osc:
                    if (value == '\a') CompleteOsc();
                    else if (value == '\x1b') m_State = ParserState.OscEscape;
                    else Collect(value);
                    return;
                case ParserState.OscEscape:
                    if (value == '\\') CompleteOsc();
                    else
                    {
                        Collect('\x1b');
                        if (value == '\a') CompleteOsc();
                        else if (value != '\x1b') { Collect(value); m_State = ParserState.Osc; }
                    }
                    return;
                case ParserState.String:
                    if (value == '\x1b') m_State = ParserState.StringEscape;
                    return;
                case ParserState.StringEscape:
                    m_State = value == '\\' ? ParserState.Text : ParserState.String;
                    return;
            }

            if (m_HighSurrogate != '\0')
            {
                char high = m_HighSurrogate;
                m_HighSurrogate = '\0';
                if (char.IsLowSurrogate(value)) { Write(new string(new[] { high, value })); return; }
                Write("\ufffd");
            }
            switch (value)
            {
                case '\x1b': m_State = ParserState.Escape; return;
                case '\r': m_Column = 0; return;
                case '\n': LineFeed(); return;
                case '\b': m_Column = Math.Max(0, Math.Min(m_Column, m_Columns - 1) - 1); return;
                case '\t': m_Column = Math.Min(m_Columns - 1, (m_Column / 8 + 1) * 8); return;
            }
            if (char.IsHighSurrogate(value)) m_HighSurrogate = value;
            else if (char.IsLowSurrogate(value)) Write("\ufffd");
            else if (!char.IsControl(value)) Write(value.ToString());
        }

        void Collect(char value)
        {
            if (m_Sequence.Length < SequenceLimit) m_Sequence.Append(value);
            else m_SequenceOverflow = true;
        }

        void CompleteOsc()
        {
            string payload = m_SequenceOverflow ? null : m_Sequence.ToString();
            m_State = ParserState.Text;
            m_Sequence.Clear();
            m_SequenceOverflow = false;
            if (payload != null) Control?.Invoke(payload);
        }

        void ApplyCsi(char command, string sequence)
        {
            // Private modes (including cursor visibility) do not change this plain-text projection.
            if (sequence.Length > 0 && (sequence[0] == '?' || sequence[0] == '>' || sequence[0] == '!')) return;
            string[] fields = sequence.Split(';');
            int first = Parameter(fields, 0, 1);
            int count = Math.Max(1, Math.Min(Math.Max(m_Columns, m_Rows), first));
            switch (command)
            {
                case 'H': case 'f':
                    m_Row = Math.Min(m_Rows - 1, Math.Max(1, first) - 1);
                    m_Column = Math.Min(m_Columns - 1, Math.Max(1, Parameter(fields, 1, 1)) - 1);
                    break;
                case 'G': m_Column = Math.Min(m_Columns - 1, Math.Max(1, first) - 1); break;
                case 'A': m_Row = Math.Max(0, m_Row - count); m_Column = Math.Min(m_Column, m_Columns - 1); break;
                case 'B': m_Row = Math.Min(m_Rows - 1, m_Row + count); m_Column = Math.Min(m_Column, m_Columns - 1); break;
                case 'C': m_Column = Math.Min(m_Columns - 1, m_Column + count); break;
                case 'D': m_Column = Math.Max(0, Math.Min(m_Column, m_Columns - 1) - count); break;
                case 'J': EraseDisplay(Parameter(fields, 0, 0)); break;
                case 'K':
                    int mode = Parameter(fields, 0, 0);
                    if (mode == 0) Erase(m_Row, m_Column, m_Columns);
                    else if (mode == 1) Erase(m_Row, 0, m_Column + 1);
                    else if (mode == 2) Erase(m_Row, 0, m_Columns);
                    break;
                case 'X': Erase(m_Row, m_Column, Math.Min(m_Columns, m_Column + count)); break;
                case 'n':
                    if (first == 6) Reply?.Invoke("\x1b[" + (m_Row + 1) + ";" + (Math.Min(m_Column, m_Columns - 1) + 1) + "R");
                    break;
                // SGR is intentionally omitted: the view owns its single text style.
            }
        }

        static int Parameter(string[] fields, int index, int fallback)
        {
            return index < fields.Length && int.TryParse(fields[index], out int value) && value >= 0 ? value : fallback;
        }

        void EraseDisplay(int mode)
        {
            if (mode == 3) { m_History.Clear(); m_HistoryLength = 0; return; }
            if (mode == 0)
            {
                Erase(m_Row, m_Column, m_Columns);
                for (int i = m_Row + 1; i < m_Rows; i++) Erase(i, 0, m_Columns);
            }
            else if (mode == 1)
            {
                for (int i = 0; i < m_Row; i++) Erase(i, 0, m_Columns);
                Erase(m_Row, 0, m_Column + 1);
            }
            else if (mode == 2)
                foreach (string[] line in m_Screen) Array.Clear(line, 0, line.Length);
        }

        void Erase(int row, int start, int end)
        {
            string[] line = m_Screen[row];
            start = Math.Min(start, m_Columns);
            end = Math.Min(end, m_Columns);
            if (start >= end) return;
            if (start > 0 && line[start] == "") line[start - 1] = null;
            if (end < m_Columns && line[end] == "") line[end] = null;
            Array.Clear(line, start, end - start);
        }

        void Write(string glyph)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(glyph, 0);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.Format)
            {
                int previous = Math.Min(m_Column, m_Columns) - 1;
                while (previous >= 0 && m_Screen[m_Row][previous] == "") previous--;
                if (previous >= 0 && m_Screen[m_Row][previous] != null
                    && m_Screen[m_Row][previous].Length + glyph.Length <= CellTextLimit)
                    m_Screen[m_Row][previous] += glyph;
                return;
            }
            int width = Math.Min(m_Columns, CellWidth(glyph));
            if (m_Column + width > m_Columns) { m_Column = 0; LineFeed(); }
            Erase(m_Row, m_Column, m_Column + width);
            m_Screen[m_Row][m_Column] = glyph;
            if (width == 2) m_Screen[m_Row][m_Column + 1] = "";
            m_Column += width;
        }

        static int CellWidth(string glyph)
        {
            int code = char.ConvertToUtf32(glyph, 0);
            return code >= 0x1100 && (code <= 0x115f || code == 0x2329 || code == 0x232a
                || code >= 0x2e80 && code <= 0xa4cf && code != 0x303f
                || code >= 0xac00 && code <= 0xd7a3 || code >= 0xf900 && code <= 0xfaff
                || code >= 0xfe10 && code <= 0xfe19 || code >= 0xfe30 && code <= 0xfe6f
                || code >= 0xff00 && code <= 0xff60 || code >= 0xffe0 && code <= 0xffe6
                || code >= 0x1f300 && code <= 0x1faff || code >= 0x20000 && code <= 0x3fffd) ? 2 : 1;
        }

        void LineFeed()
        {
            // LF preserves the column; shells normally emit CRLF when starting a new output line.
            m_Column = Math.Min(m_Column, m_Columns - 1);
            if (m_Row + 1 < m_Rows) { m_Row++; return; }
            Remember(m_Screen[0]);
            m_Screen.RemoveAt(0);
            m_Screen.Add(new string[m_Columns]);
        }

        void Remember(string[] line)
        {
            string text = RenderRow(line);
            m_History.Enqueue(text);
            m_HistoryLength += text.Length + 1;
            while (m_HistoryLength > TextLimit && m_History.Count > 0)
                m_HistoryLength -= m_History.Dequeue().Length + 1;
        }

        void TrimHistory()
        {
            int visibleLength = m_Rows - 1;
            foreach (string[] line in m_Screen) visibleLength += RowTextLength(line);
            // Combining marks occupy no columns but do consume the text budget. Keep cursor coordinates
            // stable when evicting old screen content, rather than shifting rows under the remote cursor.
            for (int row = 0; visibleLength > TextLimit && row < m_Rows; row++)
            {
                visibleLength -= RowTextLength(m_Screen[row]);
                Array.Clear(m_Screen[row], 0, m_Columns);
            }
            int allowance = TextLimit - visibleLength;
            while (m_HistoryLength > allowance && m_History.Count > 0)
            {
                string line = m_History.Dequeue();
                m_HistoryLength -= line.Length + 1;
            }
        }

        static int RowTextLength(string[] line)
        {
            int total = 0;
            int length = RowLength(line);
            for (int i = 0; i < length; i++) total += line[i]?.Length ?? 1;
            return total;
        }

        static int RowLength(string[] line)
        {
            int length = line.Length;
            while (length > 0 && (line[length - 1] == null || line[length - 1] == " ")) length--;
            return length;
        }

        static string RenderRow(string[] line)
        {
            var result = new StringBuilder();
            int length = RowLength(line);
            for (int i = 0; i < length; i++) result.Append(line[i] ?? " ");
            return result.ToString();
        }
    }
}
