using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AeroSpot.Core.Logging;

namespace AeroSpot.Core.Preview
{
    public sealed class DocxModel
    {
        public string Title { get; set; } = string.Empty;
        public string TextContent { get; set; } = string.Empty;
        public string MarkdownContent { get; set; } = string.Empty;
        public int ParagraphCount { get; set; }
        public int WordCount { get; set; }
    }

    public static class DocxReader
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

        public static DocxModel? ReadDocx(string filePath, int maxParagraphs = 2000)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) return null;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var body = doc.Root?.Element(W + "body");
                if (body == null) return null;

                var plainSb = new StringBuilder();
                var markdownSb = new StringBuilder();
                int paraCount = 0;
                int wordCount = 0;

                foreach (var element in body.Elements())
                {
                    if (paraCount >= maxParagraphs) break;

                    if (element.Name == W + "p")
                    {
                        var (pPlain, pMarkdown) = ParseParagraph(element);
                        if (!string.IsNullOrWhiteSpace(pPlain))
                        {
                            paraCount++;
                            plainSb.AppendLine(pPlain);
                            plainSb.AppendLine();

                            markdownSb.AppendLine(pMarkdown);
                            markdownSb.AppendLine();

                            var words = pPlain.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            wordCount += words.Length;
                        }
                    }
                    else if (element.Name == W + "tbl")
                    {
                        var (tPlain, tMarkdown, rowsCount, tWords) = ParseTable(element, maxParagraphs - paraCount);
                        if (!string.IsNullOrWhiteSpace(tPlain))
                        {
                            paraCount += rowsCount;
                            wordCount += tWords;

                            plainSb.AppendLine(tPlain);
                            plainSb.AppendLine();

                            markdownSb.AppendLine(tMarkdown);
                            markdownSb.AppendLine();
                        }
                    }
                }

                return new DocxModel
                {
                    Title = Path.GetFileName(filePath),
                    TextContent = plainSb.ToString().TrimEnd(),
                    MarkdownContent = markdownSb.ToString().TrimEnd(),
                    ParagraphCount = paraCount,
                    WordCount = wordCount
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"DocxReader: Errore durante la lettura di {filePath}: {ex.Message}");
                return null;
            }
        }

        public static string ReadText(string filePath, int maxParagraphs = 2000)
        {
            return ReadDocx(filePath, maxParagraphs)?.TextContent ?? string.Empty;
        }

        private static (string plain, string markdown) ParseParagraph(XElement para)
        {
            string prefix = "";
            var pPr = para.Element(W + "pPr");
            if (pPr != null)
            {
                var pStyle = pPr.Element(W + "pStyle")?.Attribute(W + "val")?.Value?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(pStyle))
                {
                    if (pStyle.Contains("heading1") || pStyle.Contains("heading 1") || pStyle.Contains("title") || pStyle.Contains("titolo1") || pStyle == "titolo")
                        prefix = "# ";
                    else if (pStyle.Contains("heading2") || pStyle.Contains("heading 2") || pStyle.Contains("titolo2") || pStyle.Contains("intestazione2") || pStyle.Contains("intestazione 2"))
                        prefix = "## ";
                    else if (pStyle.Contains("heading3") || pStyle.Contains("heading 3") || pStyle.Contains("subtitle") || pStyle.Contains("sottotitolo") || pStyle.Contains("titolo3"))
                        prefix = "### ";
                    else if (pStyle.Contains("heading4") || pStyle.Contains("heading 4") || pStyle.Contains("titolo4"))
                        prefix = "#### ";
                }

                if (string.IsNullOrEmpty(prefix) && pPr.Element(W + "numPr") != null)
                {
                    prefix = "- ";
                }
            }

            var plainText = new StringBuilder();
            var markdownText = new StringBuilder();

            foreach (var node in para.Elements())
            {
                if (node.Name == W + "r")
                {
                    ParseRun(node, plainText, markdownText, isHeading: !string.IsNullOrEmpty(prefix) && prefix.StartsWith("#"));
                }
                else if (node.Name == W + "hyperlink")
                {
                    var linkText = new StringBuilder();
                    var linkPlain = new StringBuilder();
                    foreach (var subRun in node.Elements(W + "r"))
                    {
                        ParseRun(subRun, linkPlain, linkText, isHeading: false);
                    }
                    plainText.Append(linkPlain);
                    markdownText.Append(linkText);
                }
            }

            string plainResult = plainText.ToString().Trim();
            string mdResult = markdownText.ToString().Trim();

            if (string.IsNullOrEmpty(plainResult))
                return ("", "");

            string fullMarkdown = !string.IsNullOrEmpty(prefix) ? prefix + mdResult : mdResult;
            return (plainResult, fullMarkdown);
        }

        private static void ParseRun(XElement run, StringBuilder plainSb, StringBuilder mdSb, bool isHeading)
        {
            var rPr = run.Element(W + "rPr");
            bool isBold = false;
            bool isItalic = false;
            bool isStrike = false;

            if (rPr != null)
            {
                isBold = IsPropertyTrue(rPr.Element(W + "b"));
                isItalic = IsPropertyTrue(rPr.Element(W + "i"));
                isStrike = IsPropertyTrue(rPr.Element(W + "strike"));
            }

            var runText = new StringBuilder();
            foreach (var child in run.Elements())
            {
                if (child.Name == W + "t")
                {
                    runText.Append(child.Value);
                }
                else if (child.Name == W + "tab")
                {
                    runText.Append('\t');
                }
                else if (child.Name == W + "br" || child.Name == W + "cr")
                {
                    runText.AppendLine();
                }
            }

            string raw = runText.ToString();
            if (string.IsNullOrEmpty(raw)) return;

            plainSb.Append(raw);

            if (isHeading || (!isBold && !isItalic && !isStrike))
            {
                mdSb.Append(raw);
                return;
            }

            // Separare spazi iniziali e finali per evitare markdown non valido (es. ** testo **)
            int leading = 0;
            while (leading < raw.Length && char.IsWhiteSpace(raw[leading])) leading++;
            int trailing = 0;
            while (trailing < raw.Length - leading && char.IsWhiteSpace(raw[raw.Length - 1 - trailing])) trailing++;

            string core = raw.Substring(leading, raw.Length - leading - trailing);
            string leadStr = raw.Substring(0, leading);
            string trailStr = raw.Substring(raw.Length - trailing);

            if (string.IsNullOrEmpty(core))
            {
                mdSb.Append(raw);
                return;
            }

            mdSb.Append(leadStr);
            if (isStrike) mdSb.Append("~~");
            if (isBold && isItalic) mdSb.Append("***");
            else if (isBold) mdSb.Append("**");
            else if (isItalic) mdSb.Append("*");

            mdSb.Append(core);

            if (isBold && isItalic) mdSb.Append("***");
            else if (isBold) mdSb.Append("**");
            else if (isItalic) mdSb.Append("*");
            if (isStrike) mdSb.Append("~~");
            mdSb.Append(trailStr);
        }

        private static bool IsPropertyTrue(XElement? prop)
        {
            if (prop == null) return false;
            var val = prop.Attribute(W + "val")?.Value?.ToLowerInvariant();
            if (val == null) return true; // <w:b/> senza attributo è vero
            return val != "0" && val != "false" && val != "off";
        }

        private static (string plain, string markdown, int rowsCount, int wordCount) ParseTable(XElement table, int maxRows)
        {
            var plainSb = new StringBuilder();
            var mdSb = new StringBuilder();
            int rowsCount = 0;
            int totalWords = 0;
            bool isFirstRow = true;

            if (maxRows <= 0)
            {
                return (string.Empty, string.Empty, 0, 0);
            }

            foreach (var row in table.Elements(W + "tr"))
            {
                if (rowsCount >= maxRows) break;
                rowsCount++;
                var cellTexts = new System.Collections.Generic.List<string>();

                foreach (var cell in row.Elements(W + "tc"))
                {
                    var cellSb = new StringBuilder();
                    foreach (var para in cell.Elements(W + "p"))
                    {
                        var (pPlain, _) = ParseParagraph(para);
                        if (!string.IsNullOrWhiteSpace(pPlain))
                        {
                            if (cellSb.Length > 0) cellSb.Append(" ");
                            cellSb.Append(pPlain);
                        }
                    }
                    string cellContent = cellSb.ToString().Trim().Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                    cellTexts.Add(cellContent);

                    var words = cellContent.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    totalWords += words.Length;
                }

                if (cellTexts.Count == 0) continue;

                plainSb.AppendLine(string.Join("\t", cellTexts));
                mdSb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");

                if (isFirstRow)
                {
                    mdSb.AppendLine("| " + string.Join(" | ", cellTexts.Select(_ => "---")) + " |");
                    isFirstRow = false;
                }
            }

            return (plainSb.ToString().TrimEnd(), mdSb.ToString().TrimEnd(), rowsCount, totalWords);
        }

        public static DocxModel? ReadOdt(string filePath, int maxParagraphs = 2000)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = zip.GetEntry("content.xml");
                if (entry == null) return null;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var textBody = doc.Root?.Element(Office + "body")?.Element(Office + "text");
                if (textBody == null) return null;

                var plainSb = new StringBuilder();
                var markdownSb = new StringBuilder();
                int paraCount = 0;
                int wordCount = 0;

                void AppendOdtParagraph(string plain, string markdown)
                {
                    if (string.IsNullOrWhiteSpace(plain) || paraCount >= maxParagraphs) return;
                    paraCount++;
                    plainSb.AppendLine(plain);
                    plainSb.AppendLine();
                    markdownSb.AppendLine(markdown);
                    markdownSb.AppendLine();
                    wordCount += plain.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                }

                void ProcessOdtElement(XElement element, string listPrefix)
                {
                    if (paraCount >= maxParagraphs) return;

                    if (element.Name == Text + "h")
                    {
                        string content = ExtractOdtInlineText(element).Trim();
                        if (string.IsNullOrEmpty(content)) return;
                        int level = 1;
                        var levelAttr = element.Attribute(Text + "outline-level")?.Value;
                        if (int.TryParse(levelAttr, out int parsedLevel))
                        {
                            level = Math.Clamp(parsedLevel, 1, 4);
                        }
                        string prefix = new string('#', level) + " ";
                        AppendOdtParagraph(content, prefix + content);
                    }
                    else if (element.Name == Text + "p")
                    {
                        string content = ExtractOdtInlineText(element).Trim();
                        if (string.IsNullOrEmpty(content)) return;
                        AppendOdtParagraph(content, string.IsNullOrEmpty(listPrefix) ? content : listPrefix + content);
                    }
                    else if (element.Name == Text + "list")
                    {
                        foreach (var item in element.Elements(Text + "list-item"))
                        {
                            if (paraCount >= maxParagraphs) break;
                            foreach (var child in item.Elements())
                            {
                                if (paraCount >= maxParagraphs) break;
                                ProcessOdtElement(child, "- ");
                            }
                        }
                    }
                    else if (element.Name == Table + "table")
                    {
                        var (tPlain, tMd, rowsCount, tWords) = ParseOdtTable(element, maxParagraphs - paraCount);
                        if (!string.IsNullOrWhiteSpace(tPlain))
                        {
                            paraCount += rowsCount;
                            wordCount += tWords;
                            plainSb.AppendLine(tPlain);
                            plainSb.AppendLine();
                            markdownSb.AppendLine(tMd);
                            markdownSb.AppendLine();
                        }
                    }
                }

                foreach (var element in textBody.Elements())
                {
                    if (paraCount >= maxParagraphs) break;
                    ProcessOdtElement(element, string.Empty);
                }

                return new DocxModel
                {
                    Title = Path.GetFileName(filePath),
                    TextContent = plainSb.ToString().TrimEnd(),
                    MarkdownContent = markdownSb.ToString().TrimEnd(),
                    ParagraphCount = paraCount,
                    WordCount = wordCount
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"DocxReader.ReadOdt: Errore durante la lettura di {filePath}: {ex.Message}");
                return null;
            }
        }

        private static string ExtractOdtInlineText(XElement element)
        {
            var sb = new StringBuilder();
            foreach (var node in element.Nodes())
            {
                if (node is XText xt)
                {
                    sb.Append(xt.Value);
                }
                else if (node is XElement child)
                {
                    if (child.Name == Text + "s")
                    {
                        int count = 1;
                        if (int.TryParse(child.Attribute(Text + "c")?.Value, out int parsedCount) && parsedCount > 0)
                        {
                            count = Math.Min(parsedCount, 64);
                        }
                        sb.Append(' ', count);
                    }
                    else if (child.Name == Text + "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (child.Name == Text + "line-break")
                    {
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.Append(ExtractOdtInlineText(child));
                    }
                }
            }
            return sb.ToString();
        }

        private static (string plain, string markdown, int rowsCount, int wordCount) ParseOdtTable(XElement table, int maxRows)
        {
            var plainSb = new StringBuilder();
            var mdSb = new StringBuilder();
            int rowsCount = 0;
            int totalWords = 0;
            bool isFirstRow = true;

            if (maxRows <= 0)
            {
                return (string.Empty, string.Empty, 0, 0);
            }

            foreach (var row in table.Elements(Table + "table-row"))
            {
                if (rowsCount >= maxRows) break;
                rowsCount++;
                var cellTexts = new System.Collections.Generic.List<string>();

                foreach (var cell in row.Elements(Table + "table-cell"))
                {
                    var cellSb = new StringBuilder();
                    foreach (var p in cell.Elements().Where(e => e.Name == Text + "p" || e.Name == Text + "h"))
                    {
                        string text = ExtractOdtInlineText(p).Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (cellSb.Length > 0) cellSb.Append(' ');
                            cellSb.Append(text);
                        }
                    }

                    string cellContent = cellSb.ToString().Trim().Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                    cellTexts.Add(cellContent);
                    totalWords += cellContent.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                }

                if (cellTexts.Count == 0) continue;

                plainSb.AppendLine(string.Join("\t", cellTexts));
                mdSb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");

                if (isFirstRow)
                {
                    mdSb.AppendLine("| " + string.Join(" | ", cellTexts.Select(_ => "---")) + " |");
                    isFirstRow = false;
                }
            }

            return (plainSb.ToString().TrimEnd(), mdSb.ToString().TrimEnd(), rowsCount, totalWords);
        }
    }
}
