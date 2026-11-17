using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroSpot.Core.Search
{
    public static class SearchRanker
    {
        public static int CalculateScore(IndexedItem item, string query, List<string>? categoryPriority = null, bool prioritizeExactMatches = true)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return item.IsApp ? 100 : 10;
            }

            string q = query.Trim();
            string title = item.Title;
            int score = 0;

            // 1. Corrispondenza esatta (Rank massimo)
            if (string.Equals(title, q, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }
            // 2. Prefisso del nome
            else if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                score += 500;
            }
            // 3. Inizio di parola (es. "Studio" in "Visual Studio")
            else if (HasWordStartingWith(title, q))
            {
                score += 350;
            }

            // 4. Acronimo (es. "vsc" per "Visual Studio Code")
            if (q.Length >= 2 && MatchesAcronym(title, q))
            {
                score += 300;
            }

            // 5. Tutte le parole presenti nel titolo o nel percorso
            string[] terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 1 && terms.All(t => title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || item.FullPath.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                bool allInTitle = terms.All(t => title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                score += allInTitle ? 250 : 120;
            }
            // 6. Sottostringa nel titolo
            else if (title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 200;
            }

            // 7. Sottostringa nel percorso (fallback)
            if (score == 0 && item.FullPath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 50;
            }

            // Se c'è corrispondenza, applica i bonus di gerarchia categoria e le ponderazioni
            if (score > 0)
            {
                // Se la gerarchia mista è attiva, garantisce che i file o cartelle con il nome esatto abbiano sempre precedenza assoluta
                // rispetto a file la cui corrispondenza è solo nella cartella genitrice (path-only match)
                if (prioritizeExactMatches)
                {
                    string titleWithoutExt = title.Contains('.') ? System.IO.Path.GetFileNameWithoutExtension(title) : title;
                    bool isExactTitle = string.Equals(title, q, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(titleWithoutExt, q, StringComparison.OrdinalIgnoreCase);

                    if (isExactTitle)
                    {
                        score += 30000;
                    }
                    else if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase) || titleWithoutExt.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 15000;
                    }
                    else if (HasWordStartingWith(title, q))
                    {
                        score += 10000;
                    }
                    else if (title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 5000;
                    }
                    // Le corrispondenze solo nel percorso (path-only) ricevono +0 tier bonus
                }

                string cat = (item.IsApp ||
                              string.Equals(item.Category, "Applicazione", StringComparison.OrdinalIgnoreCase) ||
                              item.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              item.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    ? "Applicazione"
                    : item.Category;

                var priorityList = categoryPriority ?? Config.AppConfig.GetDefaultCategoryPriority();
                int catIndex = priorityList.FindIndex(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));
                if (catIndex >= 0)
                {
                    // L'elemento in cima alla gerarchia (indice 0) riceve il bonus massimo
                    int priorityBonus = Math.Max(0, (priorityList.Count - catIndex) * 1200);
                    score += priorityBonus;
                }
                else
                {
                    score += 500;
                }

                if (string.Equals(cat, "Applicazione", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasWordStartingWith(title, q))
                    {
                        score += 300;
                    }
                }

                // Penalità lunghezza (preferisce corrispondenze più dense e compatte)
                int lengthDiff = Math.Abs(title.Length - q.Length);
                score -= Math.Min(lengthDiff, 50);

                // Bonus recency/freschezza proporzionale (max 40 punti)
                // Consente ai file modificati più di recente di prevalere tra corrispondenze analoghe (es. "Capitolo 12" vs "Capitolo 1")
                // senza intaccare i tier di corrispondenza o gerarchia (separati da >= 1200 punti).
                if (item.LastModifiedUtcTicks > 0)
                {
                    long nowTicks = DateTime.UtcNow.Ticks;
                    if (item.LastModifiedUtcTicks <= nowTicks)
                    {
                        double daysAgo = (nowTicks - item.LastModifiedUtcTicks) / (double)TimeSpan.TicksPerDay;
                        int recencyBonus = daysAgo switch
                        {
                            <= 1.0 => 40,
                            <= 7.0 => 30,
                            <= 30.0 => 20,
                            <= 180.0 => 10,
                            <= 365.0 => 5,
                            _ => 0
                        };
                        score += recencyBonus;
                    }
                }
            }

            return Math.Max(0, score);
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

        public static List<IndexedItem> Rank(IEnumerable<IndexedItem> candidates, string query, int maxResults = 10, List<string>? categoryPriority = null, bool prioritizeExactMatches = true)
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
