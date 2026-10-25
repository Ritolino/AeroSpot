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

        public static FlowDocument Convert(string markdownText)
        {
            var doc = new FlowDocument
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground = TextBrush,
                FontFamily = TextFont,
                FontSize = 13.5,
                PagePadding = new Thickness(24, 20, 24, 24)
            };

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
                        doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeBlockLines)));
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
                    doc.Blocks.Add(CreateHorizontalRule());
                    continue;
                }

                // Headers
                if (trimmed.StartsWith("# ") && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(2), 22, FontWeights.Bold, AccentBrush, new Thickness(0, 16, 0, 8)));
                    continue;
                }
                if (trimmed.StartsWith("## ") && trimmed.Length > 3)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(3), 18, FontWeights.Bold, H2Brush, new Thickness(0, 14, 0, 6)));
                    continue;
                }
                if (trimmed.StartsWith("### ") && trimmed.Length > 4)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(4), 15.5, FontWeights.SemiBold, AccentBrush, new Thickness(0, 12, 0, 4)));
                    continue;
                }
                if (trimmed.StartsWith("#### ") && trimmed.Length > 5)
                {
                    doc.Blocks.Add(CreateHeading(trimmed.Substring(5), 14, FontWeights.SemiBold, H2Brush, new Thickness(0, 10, 0, 4)));
                    continue;
                }

                // Blockquotes (> ...)
                if (trimmed.StartsWith("> ") && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateBlockQuote(trimmed.Substring(2)));
                    continue;
                }

                // Bullet Lists (- or * or •)
                if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ")) && trimmed.Length > 2)
                {
                    doc.Blocks.Add(CreateListItem(trimmed.Substring(2)));
                    continue;
                }

                // Numbered lists (1. , 2. )
                var numMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.*)$");
                if (numMatch.Success)
                {
                    doc.Blocks.Add(CreateNumberedListItem(numMatch.Groups[1].Value, numMatch.Groups[2].Value));
                    continue;
                }

                // Regular Paragraph with inline formatting
                var p = new Paragraph
                {
                    Margin = new Thickness(0, 3, 0, 5),
                    LineHeight = 22
                };
                AddInlineFormattedText(p.Inlines, trimmed);
                doc.Blocks.Add(p);
            }

            if (inCodeBlock && codeBlockLines.Count > 0)
            {
                doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeBlockLines)));
            }

            return doc;
        }

        private static Block CreateHeading(string text, double fontSize, FontWeight weight, Brush foreground, Thickness margin)
        {
            var p = new Paragraph
            {
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = foreground,
                Margin = margin,
                LineHeight = fontSize * 1.3
            };
            AddInlineFormattedText(p.Inlines, text);
            return p;
        }

        private static Block CreateCodeBlock(string code)
        {
            var border = new Border
            {
                Background = CodeBgBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 8, 0, 10)
            };

            var tb = new TextBlock
            {
                Text = code,
                FontFamily = CodeFont,
                FontSize = 12.5,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = tb;
            return new BlockUIContainer(border);
        }

        private static Block CreateBlockQuote(string text)
        {
            var border = new Border
            {
                Background = QuoteBgBrush,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 6, 0, 8)
            };

            var tb = new TextBlock
            {
                FontFamily = TextFont,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap
            };
            tb.Inlines.Add(new Run(text));

            border.Child = tb;
            return new BlockUIContainer(border);
        }

        private static Block CreateHorizontalRule()
        {
            var rect = new Border
            {
                Height = 1,
                Background = BorderBrush,
                Margin = new Thickness(0, 12, 0, 12)
            };
            return new BlockUIContainer(rect);
        }

        private static Block CreateListItem(string text)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(16, 2, 0, 2),
                LineHeight = 20
            };
            p.Inlines.Add(new Run("•  ") { Foreground = AccentBrush, FontWeight = FontWeights.Bold });
            AddInlineFormattedText(p.Inlines, text);
            return p;
        }

        private static Block CreateNumberedListItem(string num, string text)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(16, 2, 0, 2),
                LineHeight = 20
            };
            p.Inlines.Add(new Run($"{num}.  ") { Foreground = AccentBrush, FontWeight = FontWeights.Bold });
            AddInlineFormattedText(p.Inlines, text);
            return p;
        }

        private static void AddInlineFormattedText(InlineCollection inlines, string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return;

            // Pattern for:
            // 1. **bold**
            // 2. *italic*
            // 3. `inline code`
            // 4. [text](url)
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
                    inlines.Add(new Bold(new Run(m.Groups[2].Value)) { Foreground = H2Brush });
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
                        Background = InlineCodeBg,
                        Foreground = AccentBrush
                    };
                    inlines.Add(run);
                }
                else if (m.Value.StartsWith("["))
                {
                    string linkText = m.Groups[5].Value;
                    string linkUrl = m.Groups[6].Value;
                    var linkRun = new Run(linkText) { Foreground = AccentBrush };
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
