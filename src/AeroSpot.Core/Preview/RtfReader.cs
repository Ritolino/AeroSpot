using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Preview
{
    /// <summary>
    /// Parser RTF 100% managed C# in-process (zero COM, zero RichTextBox/STA, zero processi esterni).
    /// </summary>
    public static class RtfReader
    {
        private static readonly HashSet<string> IgnoredDestinations = new(StringComparer.OrdinalIgnoreCase)
        {
            "fonttbl", "colortbl", "stylesheet", "info", "pict", "object",
            "header", "headerl", "headerr", "headerf",
            "footer", "footerl", "footerr", "footerf",
            "footnote", "listtable", "listoverridetable",
            "revtbl", "rsidtbl", "mmathPr", "themedata", "colorschememapping",
            "latentstyles", "datastore", "xmlnstbl", "pgdsctbl", "aftncn", "aftnsep", "aftnsepc",
            "annotation", "atnauthor", "atndate", "atnid", "atnparent", "atnref", "atntime",
            "background", "bkmkend", "bkmkstart", "datafield", "do", "dptxbxtext",
            "falt", "file", "filetbl", "fldinst", "fname", "fontemb", "fontfile",
            "ftncn", "ftnsep", "ftnsepc", "g", "gridtbl", "htmltag",
            "keycode", "latentstyles", "leveltext", "levelnumbers", "listname",
            "nesttableprops", "nonesttables", "objdata", "objclass", "objname",
            "oldcprops", "oldpprops", "oldsprops", "oldtprops",
            "operator", "panose", "password", "passwordhash", "pgp", "pgptbl",
            "picprop", "pn", "pnseclvl", "pntext", "pntxta", "pntxtb",
            "printim", "private", "protusertbl", "pxe", "result", "rxe",
            "shp", "shpgrp", "shpinst", "shppict", "shprslt", "shptxt",
            "sn", "sp", "staticval", "sv", "tc", "template", "title", "txe",
            "ud", "upr", "userprops", "wgrffmtfilter", "xe"
        };

        private struct RtfState
        {
            public bool Bold;
            public bool Italic;
            public bool Strike;
            public bool IgnoredDestination;
            public int UcSkipCount;
            public int CodePage;
            public bool IsGroupStart;
        }

        static RtfReader()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static DocxModel? ReadRtf(string filePath, int maxParagraphs = 2000)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                string raw;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.Latin1, detectEncodingFromByteOrderMarks: false))
                {
                    raw = sr.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(raw)) return null;

                int startIdx = 0;
                while (startIdx < raw.Length && char.IsWhiteSpace(raw[startIdx])) startIdx++;

                if (startIdx + 5 > raw.Length || !raw.AsSpan(startIdx, 5).Equals("{\\rtf", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warn($"RtfReader: Firma '{{\\rtf' assente in {filePath}");
                    return null;
                }

                var plainSb = new StringBuilder();
                var mdSb = new StringBuilder();
                var runPlain = new StringBuilder();
                bool runBold = false;
                bool runItalic = false;
                bool runStrike = false;

                var stack = new Stack<RtfState>();
                var current = new RtfState
                {
                    Bold = false,
                    Italic = false,
                    Strike = false,
                    IgnoredDestination = false,
                    UcSkipCount = 1,
                    CodePage = 1252,
                    IsGroupStart = false
                };

                int skipChars = 0;
                int paraCount = 0;
                int wordCount = 0;
                var pendingBytes = new List<byte>();

                void FlushPendingBytes()
                {
                    if (pendingBytes.Count == 0) return;
                    if (!current.IgnoredDestination)
                    {
                        Encoding enc;
                        try
                        {
                            enc = Encoding.GetEncoding(current.CodePage);
                        }
                        catch
                        {
                            enc = Encoding.GetEncoding(1252);
                        }
                        string decoded = enc.GetString(pendingBytes.ToArray());
                        AppendRunText(decoded);
                    }
                    pendingBytes.Clear();
                }

                void FlushRun()
                {
                    if (runPlain.Length == 0) return;
                    string text = runPlain.ToString();
                    runPlain.Clear();

                    plainSb.Append(text);

                    if (!runBold && !runItalic && !runStrike)
                    {
                        mdSb.Append(text);
                        return;
                    }

                    int leading = 0;
                    while (leading < text.Length && char.IsWhiteSpace(text[leading])) leading++;
                    int trailing = 0;
                    while (trailing < text.Length - leading && char.IsWhiteSpace(text[text.Length - 1 - trailing])) trailing++;

                    string core = text.Substring(leading, text.Length - leading - trailing);
                    if (string.IsNullOrEmpty(core))
                    {
                        mdSb.Append(text);
                        return;
                    }

                    mdSb.Append(text, 0, leading);
                    if (runStrike) mdSb.Append("~~");
                    if (runBold && runItalic) mdSb.Append("***");
                    else if (runBold) mdSb.Append("**");
                    else if (runItalic) mdSb.Append("*");

                    mdSb.Append(core);

                    if (runBold && runItalic) mdSb.Append("***");
                    else if (runBold) mdSb.Append("**");
                    else if (runItalic) mdSb.Append("*");
                    if (runStrike) mdSb.Append("~~");
                    mdSb.Append(text, text.Length - trailing, trailing);
                }

                void AppendRunText(string text)
                {
                    if (string.IsNullOrEmpty(text) || current.IgnoredDestination) return;

                    if (runPlain.Length > 0 && (runBold != current.Bold || runItalic != current.Italic || runStrike != current.Strike))
                    {
                        FlushRun();
                    }

                    if (runPlain.Length == 0)
                    {
                        runBold = current.Bold;
                        runItalic = current.Italic;
                        runStrike = current.Strike;
                    }

                    runPlain.Append(text);
                }

                void AppendParagraphBreak()
                {
                    FlushRun();
                    plainSb.AppendLine();
                    mdSb.AppendLine();
                    paraCount++;
                }

                int i = startIdx;
                while (i < raw.Length && paraCount < maxParagraphs)
                {
                    char c = raw[i];

                    if (skipChars > 0 && c != '{' && c != '}' && c != '\\')
                    {
                        if (c != '\r' && c != '\n')
                        {
                            skipChars--;
                        }
                        i++;
                        continue;
                    }

                    if (c == '{')
                    {
                        FlushPendingBytes();
                        FlushRun();
                        stack.Push(current);
                        current.IsGroupStart = true;
                        skipChars = 0;
                        i++;
                        continue;
                    }

                    if (c == '}')
                    {
                        FlushPendingBytes();
                        FlushRun();
                        if (stack.Count > 0)
                        {
                            current = stack.Pop();
                        }
                        skipChars = 0;
                        i++;
                        continue;
                    }

                    if (c == '\r' || c == '\n')
                    {
                        i++;
                        continue;
                    }

                    if (c == '\\')
                    {
                        i++;
                        if (i >= raw.Length) break;

                        char next = raw[i];

                        // Destinazione opzionale \*
                        if (next == '*')
                        {
                            FlushPendingBytes();
                            if (current.IsGroupStart)
                            {
                                current.IgnoredDestination = true;
                            }
                            current.IsGroupStart = false;
                            i++;
                            continue;
                        }

                        // Carattere hex \'hh
                        if (next == '\'')
                        {
                            i++;
                            if (i + 1 < raw.Length &&
                                byte.TryParse(raw.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte hexByte))
                            {
                                if (skipChars > 0)
                                {
                                    skipChars--;
                                }
                                else if (!current.IgnoredDestination)
                                {
                                    pendingBytes.Add(hexByte);
                                }
                                i += 2;
                            }
                            current.IsGroupStart = false;
                            continue;
                        }

                        FlushPendingBytes();

                        // Simboli escape singoli
                        if (!char.IsLetter(next))
                        {
                            current.IsGroupStart = false;
                            if (skipChars > 0)
                            {
                                skipChars--;
                                i++;
                                continue;
                            }

                            switch (next)
                            {
                                case '\\':
                                case '{':
                                case '}':
                                    AppendRunText(next.ToString());
                                    break;
                                case '~':
                                    AppendRunText("\u00A0");
                                    break;
                                case '_':
                                    AppendRunText("-");
                                    break;
                                case '\r':
                                case '\n':
                                    AppendParagraphBreak();
                                    break;
                            }
                            i++;
                            continue;
                        }

                        // Parola di controllo alfabetica + parametro numerico opzionale
                        int wordStart = i;
                        while (i < raw.Length && char.IsLetter(raw[i])) i++;
                        string word = raw.Substring(wordStart, i - wordStart);

                        int? param = null;
                        int paramStart = i;
                        if (i < raw.Length && (raw[i] == '-' || char.IsDigit(raw[i])))
                        {
                            if (raw[i] == '-') i++;
                            int digitStart = i;
                            while (i < raw.Length && char.IsDigit(raw[i])) i++;
                            if (i > digitStart && int.TryParse(raw.AsSpan(paramStart, i - paramStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedParam))
                            {
                                param = parsedParam;
                            }
                        }

                        // Lo spazio delimitatore dopo una control word fa parte della sintassi RTF
                        if (i < raw.Length && raw[i] == ' ')
                        {
                            i++;
                        }

                        bool wasGroupStart = current.IsGroupStart;
                        current.IsGroupStart = false;

                        if (wasGroupStart && IgnoredDestinations.Contains(word))
                        {
                            current.IgnoredDestination = true;
                            continue;
                        }

                        if (current.IgnoredDestination)
                        {
                            continue;
                        }

                        switch (word)
                        {
                            case "ansicpg":
                                if (param.HasValue && param.Value > 0)
                                    current.CodePage = param.Value;
                                break;
                            case "uc":
                                if (param.HasValue && param.Value >= 0)
                                    current.UcSkipCount = param.Value;
                                break;
                            case "u":
                                if (param.HasValue)
                                {
                                    int codePoint = param.Value < 0 ? (ushort)param.Value : param.Value;
                                    AppendRunText(char.ConvertFromUtf32(codePoint));
                                    skipChars = current.UcSkipCount;
                                }
                                break;
                            case "par":
                            case "line":
                                AppendParagraphBreak();
                                break;
                            case "tab":
                                AppendRunText("\t");
                                break;
                            case "emdash":
                                AppendRunText("—");
                                break;
                            case "endash":
                                AppendRunText("–");
                                break;
                            case "lquote":
                                AppendRunText("‘");
                                break;
                            case "rquote":
                                AppendRunText("’");
                                break;
                            case "ldblquote":
                                AppendRunText("“");
                                break;
                            case "rdblquote":
                                AppendRunText("”");
                                break;
                            case "bullet":
                                AppendRunText(IsAtLineStart(plainSb, runPlain) ? "- " : "•");
                                break;
                            case "b":
                                current.Bold = !param.HasValue || param.Value != 0;
                                break;
                            case "i":
                                current.Italic = !param.HasValue || param.Value != 0;
                                break;
                            case "strike":
                                current.Strike = !param.HasValue || param.Value != 0;
                                break;
                            case "plain":
                                current.Bold = false;
                                current.Italic = false;
                                current.Strike = false;
                                break;
                        }

                        continue;
                    }

                    // Testo ordinario
                    FlushPendingBytes();
                    current.IsGroupStart = false;
                    AppendRunText(c.ToString());
                    i++;
                }

                FlushPendingBytes();
                FlushRun();

                string finalPlain = NormalizeLines(plainSb.ToString(), out int normalizedParagraphs);
                string finalMarkdown = NormalizeLines(mdSb.ToString(), out _);

                if (string.IsNullOrWhiteSpace(finalPlain))
                {
                    return new DocxModel
                    {
                        Title = Path.GetFileName(filePath),
                        TextContent = string.Empty,
                        MarkdownContent = string.Empty,
                        ParagraphCount = 0,
                        WordCount = 0
                    };
                }

                wordCount = finalPlain.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

                return new DocxModel
                {
                    Title = Path.GetFileName(filePath),
                    TextContent = finalPlain,
                    MarkdownContent = finalMarkdown,
                    ParagraphCount = Math.Max(normalizedParagraphs, 1),
                    WordCount = wordCount
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"RtfReader: Errore durante la lettura di {filePath}: {ex.Message}");
                return null;
            }
        }

        public static string ReadText(string filePath, int maxParagraphs = 2000)
        {
            return ReadRtf(filePath, maxParagraphs)?.TextContent ?? string.Empty;
        }

        private static bool IsAtLineStart(StringBuilder plainSb, StringBuilder runPlain)
        {
            if (runPlain.Length > 0)
            {
                return runPlain[^1] == '\n';
            }
            return plainSb.Length == 0 || plainSb[^1] == '\n';
        }

        private static string NormalizeLines(string input, out int nonEmptyParagraphs)
        {
            var lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            int consecutiveEmpty = 0;
            nonEmptyParagraphs = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (sb.Length > 0 && consecutiveEmpty == 0)
                    {
                        sb.AppendLine();
                        consecutiveEmpty++;
                    }
                    continue;
                }

                consecutiveEmpty = 0;
                nonEmptyParagraphs++;
                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }
    }
}
