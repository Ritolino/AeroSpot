using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AeroSpot.Core.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class LocalizationParityTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("AEROSPOT_UI_CULTURE", null);
            LocalizationService.Initialize("auto");
        }

        [TestMethod]
        public void Localization_AllKeysExistInBothLanguages_AndNoneEmpty()
        {
            var en = LocalizationService.English;
            var it = LocalizationService.Italian;

            Assert.IsNotEmpty(en, "Il dizionario inglese non deve essere vuoto.");
            Assert.IsNotEmpty(it, "Il dizionario italiano non deve essere vuoto.");

            // Verifica che ogni chiave in EN esista in IT
            foreach (var kvp in en)
            {
                Assert.IsTrue(it.ContainsKey(kvp.Key), $"Chiave mancante nel dizionario italiano: '{kvp.Key}'");
                Assert.IsFalse(string.IsNullOrWhiteSpace(kvp.Value), $"Valore vuoto nel dizionario inglese per la chiave: '{kvp.Key}'");
                Assert.IsFalse(string.IsNullOrWhiteSpace(it[kvp.Key]), $"Valore vuoto nel dizionario italiano per la chiave: '{kvp.Key}'");
            }

            // Verifica che ogni chiave in IT esista in EN
            foreach (var kvp in it)
            {
                Assert.IsTrue(en.ContainsKey(kvp.Key), $"Chiave presente in italiano ma mancante in inglese: '{kvp.Key}'");
            }
        }

        [TestMethod]
        public void Localization_PlaceholdersMatchExactly_BetweenLanguages()
        {
            var placeholderRegex = new Regex(@"\{(\d+)[^}]*\}");

            foreach (var kvp in LocalizationService.English)
            {
                string enValue = kvp.Value;
                string itValue = LocalizationService.Italian[kvp.Key];

                var enMatches = placeholderRegex.Matches(enValue);
                var itMatches = placeholderRegex.Matches(itValue);

                var enPlaceholders = new HashSet<string>();
                foreach (Match m in enMatches) enPlaceholders.Add(m.Groups[1].Value);

                var itPlaceholders = new HashSet<string>();
                foreach (Match m in itMatches) itPlaceholders.Add(m.Groups[1].Value);

                Assert.HasCount(enPlaceholders.Count, itPlaceholders,
                    $"Il numero di segnaposto per la chiave '{kvp.Key}' differisce tra EN ({enPlaceholders.Count}) e IT ({itPlaceholders.Count}).");

                foreach (var ph in enPlaceholders)
                {
                    Assert.Contains(ph, itPlaceholders,
                        $"Segnaposto {{{ph}}} per la chiave '{kvp.Key}' presente in EN ma assente in IT.");
                }
            }
        }

        [TestMethod]
        public void Localization_EnvironmentVariableOverride_WorksForQA()
        {
            try
            {
                // Imposta variabile ambiente per forzare italiano
                Environment.SetEnvironmentVariable("AEROSPOT_UI_CULTURE", "it");
                LocalizationService.Initialize("en"); // Config dice "en", ma env deve vincere
                Assert.AreEqual("it", LocalizationService.CurrentLanguage);
                Assert.AreEqual("Impostazioni...", LocalizationService.Get("Tray_Settings"));

                // Imposta variabile ambiente per forzare inglese
                Environment.SetEnvironmentVariable("AEROSPOT_UI_CULTURE", "en");
                LocalizationService.Initialize("it"); // Config dice "it", ma env deve vincere
                Assert.AreEqual("en", LocalizationService.CurrentLanguage);
                Assert.AreEqual("Settings...", LocalizationService.Get("Tray_Settings"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("AEROSPOT_UI_CULTURE", null);
                LocalizationService.Initialize("auto");
            }
        }

        [TestMethod]
        public void Localization_ExplicitPreferences_ResolveCorrectly()
        {
            Assert.AreEqual("en", LocalizationService.ResolveLanguageCode("en"));
            Assert.AreEqual("it", LocalizationService.ResolveLanguageCode("it"));
            Assert.AreEqual("en", LocalizationService.ResolveLanguageCode("EN"));
            Assert.AreEqual("it", LocalizationService.ResolveLanguageCode("IT"));

            // "auto" restituisce una lingua supportata valida ("en" o "it")
            string autoLang = LocalizationService.ResolveLanguageCode("auto");
            Assert.IsTrue(autoLang == "en" || autoLang == "it");
        }
    }
}
