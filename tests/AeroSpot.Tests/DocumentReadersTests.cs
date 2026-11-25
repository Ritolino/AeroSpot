using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using AeroSpot.Core.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class DocumentReadersTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"AeroSpot_DocReaderTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        private string WriteRtf(string name, string content)
        {
            string path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content, Encoding.Latin1);
            return path;
        }

        private string WriteOdt(string name, string contentXml)
        {
            string path = Path.Combine(_tempDir, name);
            using var fs = new FileStream(path, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("content.xml");
            using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
            sw.Write(contentXml);
            return path;
        }

        [TestMethod]
        public void RtfReader_Utf16Surrogates_DecodesSignedAndUnsignedPairs()
        {
            // -10179 / -8688 -> U+1F610 (😐); 55357 / 56832 -> U+1F600 (😀)
            string path = WriteRtf("surrogates.rtf",
                @"{\rtf1\ansi\deff0 Signed: \u-10179?\u-8688? Unsigned: \u55357?\u56832?}");

            var model = RtfReader.ReadRtf(path);
            Assert.IsNotNull(model);
            StringAssert.Contains(model.TextContent, "Signed: \U0001F610");
            StringAssert.Contains(model.TextContent, "Unsigned: \U0001F600");
        }

        [TestMethod]
        public void RtfReader_UcSkipCounts_RespectsUc0AndUc2()
        {
            string path = WriteRtf("uc_counts.rtf",
                @"{\rtf1\ansi\deff0 \uc0\u8364A \uc2\u8364??B}");

            var model = RtfReader.ReadRtf(path);
            Assert.IsNotNull(model);
            StringAssert.Contains(model.TextContent, "€A €B");
        }

        [TestMethod]
        public void RtfReader_IgnoredDestinations_AreCompletelySkipped()
        {
            string path = WriteRtf("destinations.rtf",
                @"{\rtf1\ansi\deff0{\*\generator SecretGen}{\stylesheet{\s0 Normal;}}{\info{\title HiddenTitle}{\author HiddenAuthor}}{\pict\pngblip 89504E470D0A}VisibleText}");

            var model = RtfReader.ReadRtf(path);
            Assert.IsNotNull(model);
            Assert.AreEqual("VisibleText", model.TextContent);
            Assert.IsFalse(model.TextContent.Contains("SecretGen", StringComparison.Ordinal));
            Assert.IsFalse(model.TextContent.Contains("Normal", StringComparison.Ordinal));
            Assert.IsFalse(model.TextContent.Contains("HiddenTitle", StringComparison.Ordinal));
            Assert.IsFalse(model.TextContent.Contains("89504E47", StringComparison.Ordinal));
        }

        [TestMethod]
        public void RtfReader_TypographicEscapes_RenderExpectedCharacters()
        {
            string path = WriteRtf("escapes.rtf",
                @"{\rtf1\ansi\deff0 \bullet Item one\par A\emdash B\endash C mid\bullet D\\E\{F\}}");

            var model = RtfReader.ReadRtf(path);
            Assert.IsNotNull(model);
            StringAssert.Contains(model.TextContent, "- Item one");
            StringAssert.Contains(model.TextContent, "A—B–C mid•D\\E{F}");
        }

        [TestMethod]
        public void RtfReader_MaxParagraphs_TruncatesOutput()
        {
            var sb = new StringBuilder(@"{\rtf1\ansi\deff0 ");
            for (int i = 1; i <= 10; i++)
            {
                sb.Append($"Para {i}\\par ");
            }
            sb.Append('}');

            string path = WriteRtf("maxpara.rtf", sb.ToString());
            var model = RtfReader.ReadRtf(path, maxParagraphs: 3);

            Assert.IsNotNull(model);
            StringAssert.Contains(model.TextContent, "Para 1");
            StringAssert.Contains(model.TextContent, "Para 3");
            Assert.IsFalse(model.TextContent.Contains("Para 4", StringComparison.Ordinal));
        }

        [TestMethod]
        public void RtfReader_InvalidOrMissingHeader_ReturnsNull()
        {
            string invalidPath = WriteRtf("invalid.rtf", "Not an RTF document at all");
            Assert.IsNull(RtfReader.ReadRtf(invalidPath));
            Assert.IsNull(RtfReader.ReadRtf(Path.Combine(_tempDir, "missing.rtf")));
        }

        [TestMethod]
        public void DocxReader_ReadOdt_ParsesListsTablesEscapesAndWhitespace()
        {
            string odtPath = WriteOdt("sample.odt", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content
    xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0""
    xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0""
    xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"">
  <office:body>
    <office:text>
      <text:h text:outline-level=""2"">Sezione ODT</text:h>
      <text:p>A<text:s text:c=""3""/>B<text:tab/>C<text:line-break/>Riga2</text:p>
      <text:list>
        <text:list-item><text:p>Primo punto</text:p></text:list-item>
        <text:list-item><text:p>Secondo punto</text:p></text:list-item>
      </text:list>
      <table:table>
        <table:table-row>
          <table:table-cell><text:p>Colonna</text:p></table:table-cell>
          <table:table-cell><text:p>Valore</text:p></table:table-cell>
        </table:table-row>
        <table:table-row>
          <table:table-cell><text:p>A|B</text:p></table:table-cell>
          <table:table-cell><text:p>100</text:p></table:table-cell>
        </table:table-row>
      </table:table>
    </office:text>
  </office:body>
</office:document-content>");

            var model = DocxReader.ReadOdt(odtPath);
            Assert.IsNotNull(model);
            StringAssert.Contains(model.MarkdownContent, "## Sezione ODT");
            StringAssert.Contains(model.TextContent, "A   B\tC");
            StringAssert.Contains(model.TextContent, "Riga2");
            StringAssert.Contains(model.MarkdownContent, "- Primo punto");
            StringAssert.Contains(model.MarkdownContent, "- Secondo punto");
            StringAssert.Contains(model.MarkdownContent, "| Colonna | Valore |");
            StringAssert.Contains(model.MarkdownContent, "| --- | --- |");
            StringAssert.Contains(model.MarkdownContent, @"| A\|B | 100 |");
        }

        [TestMethod]
        public void DocxReader_ReadOdt_InvalidArchive_ReturnsNull()
        {
            string badArchive = Path.Combine(_tempDir, "bad.odt");
            File.WriteAllBytes(badArchive, new byte[] { 0x50, 0x4B, 0x01, 0x02 });
            Assert.IsNull(DocxReader.ReadOdt(badArchive));
            Assert.IsNull(DocxReader.ReadOdt(Path.Combine(_tempDir, "missing.odt")));
        }
    }
}
