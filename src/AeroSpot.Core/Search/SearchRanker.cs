using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroSpot.Core.Search
{
    public static class SearchRanker
    {
        public static int CalculateScore(IndexedItem item, string query, List<string>? categoryPriority = null, bool prioritizeExactMatches = false)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return item.IsApp ? 100 : 10;
            }

            string q = query.Trim();
            string title = item.Title ?? string.Empty;
            string titleWithoutExt = title.Contains('.') ? System.IO.Path.GetFileNameWithoutExtension(title) : title;

            string normQ = NormalizeForComparison(q);
            string normTitle = NormalizeForComparison(title);
            string normTitleWithoutExt = NormalizeForComparison(titleWithoutExt);

            string[] terms = q.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            bool isRawExactTitle = string.Equals(title, q, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(titleWithoutExt, q, StringComparison.OrdinalIgnoreCase);

            bool isNormExactTitle = !string.IsNullOrEmpty(normQ) &&
                                    (string.Equals(normTitle, normQ, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(normTitleWithoutExt, normQ, StringComparison.OrdinalIgnoreCase));

            bool isExact100 = isRawExactTitle || isNormExactTitle;

            bool isPrefix = title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
                            titleWithoutExt.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(normQ) &&
                             (normTitle.StartsWith(normQ, StringComparison.OrdinalIgnoreCase) ||
                              normTitleWithoutExt.StartsWith(normQ, StringComparison.OrdinalIgnoreCase)));

            bool allTermsStartWords = terms.Length > 1 && terms.All(t => HasWordStartingWith(title, t));
            bool hasWordStart = HasWordStartingWith(title, q);
            bool isAcronym = q.Length >= 2 && terms.Length == 1 && MatchesAcronym(title, q);
            bool allTermsInTitle = terms.Length > 0 && terms.All(t => title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            bool isSubstringInTitle = title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            bool isTitleMatch = isExact100 || isPrefix || allTermsStartWords || hasWordStart || isAcronym || allTermsInTitle || isSubstringInTitle;

            string fullPath = item.FullPath ?? string.Empty;
            bool isPathMatch = !isTitleMatch &&
                               (fullPath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (terms.Length > 0 && terms.All(t =>
                                    title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    fullPath.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)));

            if (!isTitleMatch && !isPathMatch)
            {
                return 0;
            }

            string cat = (item.IsApp ||
                          string.Equals(item.Category, "Applicazione", StringComparison.OrdinalIgnoreCase) ||
                          fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                          fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                ? "Applicazione"
                : item.Category;

            var priorityList = categoryPriority ?? Config.AppConfig.GetDefaultCategoryPriority();
            int catIndex = priorityList.FindIndex(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));
            int categoryRankSteps = catIndex >= 0 ? Math.Max(1, priorityList.Count - catIndex) : 0;

            int lengthDiff = Math.Abs(title.Length - q.Length);
            int lengthPenalty = Math.Min(lengthDiff, 50);

            int recencyBonus = 0;
            if (item.LastModifiedUtcTicks > 0)
            {
                long nowTicks = DateTime.UtcNow.Ticks;
                if (item.LastModifiedUtcTicks <= nowTicks)
                {
                    double daysAgo = (nowTicks - item.LastModifiedUtcTicks) / (double)TimeSpan.TicksPerDay;
                    recencyBonus = daysAgo switch
                    {
                        <= 1.0 => 40,
                        <= 7.0 => 30,
                        <= 30.0 => 20,
                        <= 180.0 => 10,
                        <= 365.0 => 5,
                        _ => 0
                    };
                }
            }

            if (isTitleMatch)
            {
                // Fascia 1 — Match nel Nome (Title): sempre >= 100.000 punti (precede sempre qualsiasi match solo nel percorso)
                int score = 100000;

                // Se l'opzione "Priorità corrispondenza esatta del nome" è attiva, SOLO un match esatto al 100% sul nome scavalca le categorie
                if (prioritizeExactMatches && isExact100)
                {
                    score += 500000;
                }

                // Bonus Gerarchia Categorie: comanda sempre l'ordine tra i match sul Nome (step da 5.000 punti)
                if (catIndex >= 0)
                {
                    score += categoryRankSteps * 5000;
                }
                else
                {
                    score += 2000;
                }

                // Qualità del match sul Nome (ordina gli elementi dentro la stessa categoria, max < 2.500 punti)
                if (isRawExactTitle)
                {
                    score += 2000;
                }
                else if (isNormExactTitle)
                {
                    score += 1800;
                }
                else if (isPrefix)
                {
                    score += 1400;
                }
                else if (allTermsStartWords)
                {
                    score += 1100;
                }
                else if (hasWordStart)
                {
                    score += 900;
                }
                else if (isAcronym)
                {
                    score += 750;
                }
                else if (allTermsInTitle)
                {
                    score += 600;
                }
                else if (isSubstringInTitle)
                {
                    score += 400;
                }

                if (string.Equals(cat, "Applicazione", StringComparison.OrdinalIgnoreCase) && (hasWordStart || allTermsStartWords))
                {
                    score += 150;
                }

                score += recencyBonus - lengthPenalty;
                return Math.Max(1, score);
            }
            else
            {
                // Fascia 2 — Match solo nel Percorso (path-only): confinato sotto i 10.000 punti (compare solo in coda come riempitivo)
                int score = 500;
                if (catIndex >= 0)
                {
                    score += categoryRankSteps * 800;
                }
                else
                {
                    score += 200;
                }

                score += recencyBonus - lengthPenalty;
                return Math.Clamp(score, 50, 9500);
            }
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var parts = text.Split(new[] { ' ', '-', '_', '.', '—' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private static bool HasWordStartingWith(string title, string prefix)
        {
            var words = title.Split(new[] { ' ', '-', '_', '.', '—' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MatchesAcronym(string title, string query)
        {
            var words = title.Split(new[] { ' ', '-', '_', '—' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < query.Length) return false;

            for (int i = 0; i < query.Length; i++)
            {
                if (char.ToLowerInvariant(words[i][0]) != char.ToLowerInvariant(query[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static List<IndexedItem> Rank(IEnumerable<IndexedItem> candidates, string query, int maxResults = 10, List<string>? categoryPriority = null, bool prioritizeExactMatches = false)
        {
            var scored = new List<IndexedItem>();
            foreach (var item in candidates)
            {
                item.SearchScore = CalculateScore(item, query, categoryPriority, prioritizeExactMatches);
                if (item.SearchScore > 0 || string.IsNullOrWhiteSpace(query))
                {
                    scored.Add(item);
                }
            }

            return scored
                .OrderByDescending(i => i.SearchScore)
                .ThenByDescending(i => i.LastModifiedUtcTicks)
                .ThenBy(i => i.Title.Length)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }
    }
}
