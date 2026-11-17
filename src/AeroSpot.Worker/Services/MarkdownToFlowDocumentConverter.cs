using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AeroSpot.Worker.Services
{
    public static class MarkdownToFlowDocumentConverter
    {
        private static readonly SolidColorBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));       // #E2E8F0
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));      // #94A3B8
        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));     // #38BDF8
        private static readonly SolidColorBrush H2Brush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));         // #F8FAFC
        private static readonly SolidColorBrush CodeBgBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));     // #0F172A
        private static readonly SolidColorBrush InlineCodeBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));    // #1E293B
        private static readonly SolidColorBrush BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));     // #334155
        private static readonly SolidColorBrush QuoteBgBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x29, 0x3B));
        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, Courier New");
        private static readonly FontFamily TextFont = new FontFamily("Segoe UI, Aptos, Arial");

        static MarkdownToFlowDocumentConverter()
        {
            TextBrush.Freeze();
            MutedBrush.Freeze();
            AccentBrush.Freeze();
            H2Brush.Freeze();
            CodeBgBrush.Freeze();
            InlineCodeBg.Freeze();
            BorderBrush.Freeze();
            QuoteBgBrush.Freeze();
        }

        public static FlowDocument Convert(string markdownText, bool forPrinting = false)
        {
            var textBrush = forPrinting ? Brushes.Black : TextBrush;
            var h2Brush = forPrinting ? Brushes.Black : H2Brush;
            var accentBrush = forPrinting ? new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)) : AccentBrush;
            var codeBg = forPrinting ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)) : CodeBgBrush;
            var quoteBg = forPrinting ? new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)) : QuoteBgBrush;
            var quoteText = forPrinting ? new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)) : MutedBrush;
            var borderBrush = forPrinting ? new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)) : BorderBrush;
            var inlineCodeBg = forPrinting ? new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)) : InlineCodeBg;

            var doc = new FlowDocument
            {
                Background = forPrinting ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground = textBrush,
                FontFamily = TextFont,
                FontSize = forPrinting ? 11.5 : 13.5,
                PagePadding = forPrinting ? new Thickness(48) : new Thickness(24, 20, 24, 24)
            };

            if (forPrinting)
            {
                doc.PageWidth = 793.7; // A4 a 96 DPI
                doc.PageHeight = 1122.5;
                doc.ColumnWidth = double.PositiveInfinity;
            }

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                return doc;
            }

            string[] lines = markdownText.Replace("\r\n", "\n").Split('\n');
            bool inCodeBlock = false;
            var codeBlockLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Fenced code block (```)
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        // End of code block
                        doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeBlockLines), codeBg, borderBrush, textBrush));
                        codeBlockLines.Clear();
                        inCodeBlock = false;
                    }
                    else
                    {
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockLines.Add(line);
                    continue;
                }

                string trimmed = line.Trim();

                // Empty line
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                // Horizontal Rule
                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    doc.Blocks.Add(CreateHorizontalRule(borderBrush));
                    continue;
                }

                // Headers
                if (trimmed.StartsWith("# ") && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(2), forPrinting ? 18 : 22, FontWeights.Bold, accentBrush, new Thickness(0, 16, 0, 8), h2Brush, inlineCodeBg));
                    continue;
                }
                if (trimmed.StartsWith("## ") && trimmed.Length > 3)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(3), forPrinting ? 15 : 18, FontWeights.Bold, h2Brush, new Thickness(0, 14, 0, 6), h2Brush, inlineCodeBg));
                    continue;
                }
                if (trimmed.StartsWith("### ") && trimmed.Length > 4)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(4), forPrinting ? 13 : 15.5, FontWeights.SemiBold, accentBrush, new Thickness(0, 12, 0, 4), h2Brush, inlineCodeBg));
                    continue;
                }
                if (trimmed.StartsWith("#### ") && trimmed.Length > 5)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(5), forPrinting ? 12 : 14, FontWeights.SemiBold, h2Brush, new Thickness(0, 10, 0, 4), h2Brush, inlineCodeBg));
                    continue;
                }

                // Blockquotes (> ...)
                if (trimmed.StartsWith("> ") && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateBlockQuote(trimmed.Substring(2), quoteBg, accentBrush, quoteText, h2Brush, inlineCodeBg));
                    continue;
                }

                // Bullet Lists (- or * or •)
                if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ")) && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateListItem(trimmed.Substring(2), accentBrush, h2Brush, inlineCodeBg));
                    continue;
                }

                // Numbered lists (1. , 2. )
                var numMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.*)$");
                if (numMatch.Success)
                {
                    doc.Blocks.Add(CreateNumberedListItem(numMatch.Groups[1].Value, numMatch.Groups[2].Value, accentBrush, h2Brush, inlineCodeBg));
                    continue;
                }

                // Regular Paragraph with inline formatting
                var p = new Paragraph
                {
                    Margin = new Thickness(0, 3, 0, 5),
                    LineHeight = forPrinting ? 18 : 22
                };
                AddInlineFormattedText(p.Inlines, trimmed, accentBrush, h2Brush, inlineCodeBg);
                doc.Blocks.Add(p);
            }

            if (inCodeBlock && codeBlockLines.Count > 0)
            {
                doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeBlockLines), codeBg, borderBrush, textBrush));
            }

            return doc;
        }

        private static Block CreateHeading(string text, double fontSize, FontWeight weight, Brush foreground, Thickness margin, Brush h2Brush, Brush inlineCodeBg)
        {
            var p = new Paragraph
            {
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = foreground,
                Margin = margin,
                LineHeight = fontSize * 1.3
            };
            AddInlineFormattedText(p.Inlines, text, foreground, h2Brush, inlineCodeBg);
            return p;
        }

        private static Block CreateCodeBlock(string code, Brush codeBg, Brush borderBrush, Brush textBrush)
        {
            var p = new Paragraph
            {
                Background = codeBg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 8, 0, 10),
                FontFamily = CodeFont,
                FontSize = 12.0,
                Foreground = textBrush,
                LineHeight = 18
            };
            p.Inlines.Add(new Run(code));
            return p;
        }

        private static Block CreateBlockQuote(string text, Brush quoteBg, Brush accentBrush, Brush quoteText, Brush h2Brush, Brush inlineCodeBg)
        {
            var p = new Paragraph
            {
                Background = quoteBg,
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 6, 0, 8),
                FontFamily = TextFont,
                FontSize = 12.5,
                FontStyle = FontStyles.Italic,
                Foreground = quoteText,
                LineHeight = 20
            };
            AddInlineFormattedText(p.Inlines, text, accentBrush, h2Brush, inlineCodeBg);
            return p;
        }

        private static Block CreateHorizontalRule(Brush borderBrush)
        {
            var rect = new Border
            {
                Height = 1,
                Background = borderBrush,
                Margin = new Thickness(0, 12, 0, 12)
            };
            return new BlockUIContainer(rect);
        }

        private static Block CreateListItem(string text, Brush accentBrush, Brush h2Brush, Brush inlineCodeBg)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(16, 2, 0, 2),
                LineHeight = 20
            };
            p.Inlines.Add(new Run("•  ") { Foreground = accentBrush, FontWeight = FontWeights.Bold });
            AddInlineFormattedText(p.Inlines, text, accentBrush, h2Brush, inlineCodeBg);
            return p;
        }

        private static Block CreateNumberedListItem(string num, string text, Brush accentBrush, Brush h2Brush, Brush inlineCodeBg)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(16, 2, 0, 2),
                LineHeight = 20
            };
            p.Inlines.Add(new Run($"{num}.  ") { Foreground = accentBrush, FontWeight = FontWeights.Bold });
            AddInlineFormattedText(p.Inlines, text, accentBrush, h2Brush, inlineCodeBg);
            return p;
        }

        private static void AddInlineFormattedText(InlineCollection inlines, string rawText, Brush accentBrush, Brush h2Brush, Brush inlineCodeBg)
        {
            if (string.IsNullOrEmpty(rawText)) return;

            var pattern = @"(\*\*(.*?)\*\*|\*(.*?)\*|`(.*?)`|\[(.*?)\]\((.*?)\))";
            var matches = Regex.Matches(rawText, pattern);

            int lastIndex = 0;
            foreach (Match m in matches)
            {
                if (m.Index > lastIndex)
                {
                    inlines.Add(new Run(rawText.Substring(lastIndex, m.Index - lastIndex)));
                }

                if (m.Value.StartsWith("**") && m.Value.EndsWith("**"))
                {
                    inlines.Add(new Bold(new Run(m.Groups[2].Value)) { Foreground = h2Brush });
                }
                else if (m.Value.StartsWith("*") && m.Value.EndsWith("*"))
                {
                    inlines.Add(new Italic(new Run(m.Groups[3].Value)));
                }
                else if (m.Value.StartsWith("`") && m.Value.EndsWith("`"))
                {
                    var run = new Run(" " + m.Groups[4].Value + " ")
                    {
                        FontFamily = CodeFont,
                        FontSize = 12,
                        Background = inlineCodeBg,
                        Foreground = accentBrush
                    };
                    inlines.Add(run);
                }
                else if (m.Value.StartsWith("["))
                {
                    string linkText = m.Groups[5].Value;
                    string linkUrl = m.Groups[6].Value;
                    var linkRun = new Run(linkText) { Foreground = accentBrush };
                    inlines.Add(new Bold(linkRun));
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < rawText.Length)
            {
                inlines.Add(new Run(rawText.Substring(lastIndex)));
            }
        }
    }
}
