using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroSpot.Core.Search
{
    public static class SearchRanker
    {
        public static int CalculateScore(IndexedItem item, string query)
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

            // Se c'è corrispondenza, applica i bonus e le ponderazioni
            if (score > 0)
            {
                bool isExecutableOrApp = item.IsApp ||
                                        string.Equals(item.Category, "Applicazione", StringComparison.OrdinalIgnoreCase) ||
                                        item.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                        item.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

                if (isExecutableOrApp)
                {
                    // Priorità assoluta ai programmi rispetto alle cartelle o documenti
                    score += 1000;

                    // Se il nome dell'applicazione contiene la query come parola (es. "Photoshop" in "Adobe Photoshop 2024")
                    if (HasWordStartingWith(title, q))
                    {
                        score += 300;
                    }
                }
                else if (string.Equals(item.Category, "Cartella", StringComparison.OrdinalIgnoreCase))
                {
                    // Penalizza le cartelle rispetto alle applicazioni
                    score -= 300;
                }

                // Penalità lunghezza (preferisce corrispondenze più dense e compatte)
                int lengthDiff = Math.Abs(title.Length - q.Length);
                score -= Math.Min(lengthDiff, 50);
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

        public static List<IndexedItem> Rank(IEnumerable<IndexedItem> candidates, string query, int maxResults = 10)
        {
            var scored = new List<IndexedItem>();
            foreach (var item in candidates)
            {
                item.SearchScore = CalculateScore(item, query);
                if (item.SearchScore > 0 || string.IsNullOrWhiteSpace(query))
                {
                    scored.Add(item);
                }
            }

            return scored
                .OrderByDescending(i => i.SearchScore)
                .ThenBy(i => i.Title.Length)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }
    }
}
