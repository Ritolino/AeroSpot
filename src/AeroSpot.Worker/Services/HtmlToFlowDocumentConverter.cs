using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AeroSpot.Worker.Services
{
    public static class HtmlToFlowDocumentConverter
    {
        private static readonly SolidColorBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));       // #E2E8F0
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));      // #94A3B8
        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));     // #38BDF8
        private static readonly SolidColorBrush H1Brush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));         // #38BDF8
        private static readonly SolidColorBrush H2Brush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));         // #F8FAFC
        private static readonly SolidColorBrush TableHeaderBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));   // #1E293B
        private static readonly SolidColorBrush TableBorder = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));     // #334155
        private static readonly FontFamily TextFont = new FontFamily("Segoe UI, Aptos, Arial");
        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, Courier New");

        static HtmlToFlowDocumentConverter()
        {
            TextBrush.Freeze();
            MutedBrush.Freeze();
            AccentBrush.Freeze();
            H1Brush.Freeze();
            H2Brush.Freeze();
            TableHeaderBg.Freeze();
            TableBorder.Freeze();
        }

        public static FlowDocument Convert(string htmlContent, string title = "")
        {
            var doc = new FlowDocument
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground = TextBrush,
                FontFamily = TextFont,
                FontSize = 13.5,
                PagePadding = new Thickness(24, 18, 24, 24)
            };

            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                var emptyPara = new Paragraph(new Run("Nessun contenuto visualizzabile"))
                {
                    Foreground = MutedBrush,
                    FontStyle = FontStyles.Italic
                };
                doc.Blocks.Add(emptyPara);
                return doc;
            }

            // 1. Estrai eventuale <title> se presente
            string pageTitle = title;
            var titleMatch = Regex.Match(htmlContent, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success && string.IsNullOrWhiteSpace(pageTitle))
            {
                pageTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
            }

            if (!string.IsNullOrWhiteSpace(pageTitle))
            {
                var titlePara = new Paragraph(new Run(pageTitle))
                {
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = H1Brush,
                    Margin = new Thickness(0, 0, 0, 14)
                };
                doc.Blocks.Add(titlePara);
            }

            // 2. Rimuovi blocchi non testuali: script, style, commenti, head
            string clean = Regex.Replace(htmlContent, @"<!--.*?-->", "", RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            clean = Regex.Replace(clean, @"<head[^>]*>.*?</head>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // 3. Estrai o itera sui blocchi principali (h1-h6, p, ul, ol, table, pre, blockquote, div)
            // Sostituisci <br> con newline
            clean = Regex.Replace(clean, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            // Regex per catturare i tag di blocco
            var blockMatches = Regex.Matches(clean, @"<(h[1-6]|p|blockquote|pre|table|ul|ol|li)[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            int blockCount = 0;
            if (blockMatches.Count > 0)
            {
                foreach (Match m in blockMatches)
                {
                    string tag = m.Groups[1].Value.ToLowerInvariant();
                    string inner = m.Groups[2].Value;

                    Block? block = CreateBlock(tag, inner);
                    if (block != null)
                    {
                        doc.Blocks.Add(block);
                        blockCount++;
                    }
                }
            }

            // Fallback: se la pagina non ha blocchi standard catturabili (es. solo div innestati),
            // rimuovi tutti i tag HTML e crea paragrafi dal testo pulito
            if (blockCount == 0)
            {
                string textOnly = Regex.Replace(clean, @"<[^>]+>", " ");
                textOnly = WebUtility.HtmlDecode(textOnly);
                string[] lines = textOnly.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        doc.Blocks.Add(new Paragraph(new Run(trimmed))
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            LineHeight = 20
                        });
                    }
                }
            }

            return doc;
        }

        private static Block? CreateBlock(string tag, string innerHtml)
        {
            string plainText = StripInlineTags(innerHtml).Trim();
            if (string.IsNullOrWhiteSpace(plainText)) return null;

            switch (tag)
            {
                case "h1":
                    return new Paragraph(new Run(plainText))
                    {
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = H1Brush,
                        Margin = new Thickness(0, 14, 0, 6)
                    };

                case "h2":
                    return new Paragraph(new Run(plainText))
                    {
                        FontSize = 15.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = H2Brush,
                        Margin = new Thickness(0, 12, 0, 4)
                    };

                case "h3":
                case "h4":
                    return new Paragraph(new Run(plainText))
                    {
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = AccentBrush,
                        Margin = new Thickness(0, 10, 0, 4)
                    };

                case "h5":
                case "h6":
                    return new Paragraph(new Run(plainText))
                    {
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = MutedBrush,
                        Margin = new Thickness(0, 8, 0, 2)
                    };

                case "blockquote":
                    return new Paragraph(new Run(plainText))
                    {
                        Foreground = MutedBrush,
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(16, 6, 0, 6)
                    };

                case "pre":
                    return new Paragraph(new Run(plainText))
                    {
                        FontFamily = CodeFont,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)),
                        Background = TableHeaderBg,
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 6, 0, 8)
                    };

                case "li":
                    return new Paragraph(new Run("•  " + plainText))
                    {
                        Margin = new Thickness(12, 2, 0, 2),
                        Foreground = TextBrush
                    };

                case "p":
                default:
                    return new Paragraph(new Run(plainText))
                    {
                        Margin = new Thickness(0, 0, 0, 8),
                        LineHeight = 20,
                        Foreground = TextBrush
                    };
            }
        }

        private static string StripInlineTags(string html)
        {
            string text = Regex.Replace(html, @"<[^>]+>", " ");
            return WebUtility.HtmlDecode(text);
        }
    }
}
