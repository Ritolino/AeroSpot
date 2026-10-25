using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class InstructionsHtmlTests
    {
        [TestMethod]
        public void OfflineInstructionsHtml_BothLanguagesExist_AndAreSelfContained()
        {
            string docsDir = TestEnvironment.DocsDir;
            string itHtml = Path.Combine(docsDir, "instructions.it.html");
            string enHtml = Path.Combine(docsDir, "instructions.en.html");

            Assert.IsTrue(File.Exists(itHtml), $"Il file delle istruzioni italiane deve esistere in {itHtml}");
            Assert.IsTrue(File.Exists(enHtml), $"Il file delle istruzioni inglesi deve esistere in {enHtml}");

            string itContent = File.ReadAllText(itHtml);
            string enContent = File.ReadAllText(enHtml);

            Assert.IsGreaterThan(2000, itContent.Length, "Le istruzioni italiane devono contenere la documentazione completa.");
            Assert.IsGreaterThan(2000, enContent.Length, "Le istruzioni inglesi devono contenere la documentazione completa.");

            // Verifica assenza di CDN remoti o script esterni
            Assert.DoesNotContain("http://", itContent);
            Assert.DoesNotContain("https://", itContent);
            Assert.DoesNotContain("http://", enContent);
            Assert.DoesNotContain("https://", enContent);

            Assert.DoesNotContain("<script", itContent);
            Assert.DoesNotContain("<script", enContent);

            // Verifica presenza delle sezioni chiave
            Assert.Contains("Alt + Space", enContent);
        }
    }
}
