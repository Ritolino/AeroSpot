using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AeroSpot.Core.Localization;
using AeroSpot.Core.Logging;

namespace AeroSpot.Worker.Services
{
    public sealed class SpreadsheetModel
    {
        public string Title { get; set; } = string.Empty;
        public List<SpreadsheetSheet> Sheets { get; set; } = new();
    }

    public sealed class SpreadsheetSheet
    {
        public string Name { get; set; } = string.Empty;
        public List<string> ColumnHeaders { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public int TotalRowCount { get; set; }
        public int TotalColCount { get; set; }
    }

    public static class XlsxReader
    {
        public static SpreadsheetModel? ReadXlsx(string filePath, int maxRows = 250, int maxCols = 40)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                // 1. Lettura stringhe condivise (xl/sharedStrings.xml)
                var sharedStrings = new List<string>();
                var ssEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using var ssStream = ssEntry.Open();
                    var xDoc = XDocument.Load(ssStream);
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    foreach (var si in xDoc.Descendants(ns + "si"))
                    {
                        var sb = new StringBuilder();
                        foreach (var t in si.Descendants(ns + "t"))
                        {
                            sb.Append(t.Value);
                        }
                        sharedStrings.Add(sb.ToString());
                    }
                }

                // 2. Lettura relazioni workbook (xl/_rels/workbook.xml.rels) per risolvere r:id -> target effettivo
                var relsMap = new Dictionary<string, string>(StringComparer.Ordinal);
                var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
                if (relsEntry != null)
                {
                    using var relsStream = relsEntry.Open();
                    var relsDoc = XDocument.Load(relsStream);
                    XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                    foreach (var rel in relsDoc.Descendants(pkgRelNs + "Relationship"))
                    {
                        string? id = (string?)rel.Attribute("Id");
                        string? target = (string?)rel.Attribute("Target");
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                        {
                            string normalized = target.Replace('\\', '/').TrimStart('/');
                            if (normalized.StartsWith("./", StringComparison.Ordinal))
                            {
                                normalized = normalized.Substring(2);
                            }
                            if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                            {
                                normalized = "xl/" + normalized;
                            }
                            relsMap[id] = normalized;
                        }
                    }
                }

                // 3. Lettura struttura fogli (xl/workbook.xml)
                var sheetDefs = new List<(string Name, string Path)>();
                var wbEntry = archive.GetEntry("xl/workbook.xml");
                if (wbEntry != null)
                {
                    using var wbStream = wbEntry.Open();
                    var xDoc = XDocument.Load(wbStream);
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                    int idx = 1;
                    foreach (var sheet in xDoc.Descendants(ns + "sheet"))
                    {
                        string name = (string?)sheet.Attribute("name") ?? LocalizationService.Get("Xlsx_Sheet_Default", idx);
                        string? relId = (string?)sheet.Attribute(rNs + "id");
                        string path = (!string.IsNullOrEmpty(relId) && relsMap.TryGetValue(relId, out string? resolvedPath))
                            ? resolvedPath
                            : $"xl/worksheets/sheet{idx}.xml";
                        sheetDefs.Add((name, path));
                        idx++;
                    }
                }

                if (sheetDefs.Count == 0)
                {
                    // Fallback se workbook.xml non definisce fogli espliciti
                    sheetDefs.Add((LocalizationService.Get("Xlsx_Sheet_Default", 1), "xl/worksheets/sheet1.xml"));
                }

                var model = new SpreadsheetModel
                {
                    Title = Path.GetFileName(filePath)
                };

                // 4. Lettura contenuto di ciascun foglio
                XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var (name, path) in sheetDefs)
                {
                    var sheetEntry = archive.GetEntry(path)
                        ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
                    if (sheetEntry == null) continue;

                    var sheetObj = new SpreadsheetSheet { Name = name };
                    using var sheetStream = sheetEntry.Open();
                    var sheetDoc = XDocument.Load(sheetStream);

                    int maxColFound = 0;
                    var rowsList = new List<List<string>>();

                    var rowElements = sheetDoc.Descendants(sheetNs + "row");
                    int rowCount = 0;

                    foreach (var rowElem in rowElements)
                    {
                        if (rowCount >= maxRows) break;

                        var cellMap = new Dictionary<int, string>();
                        foreach (var cElem in rowElem.Elements(sheetNs + "c"))
                        {
                            string? cellRef = (string?)cElem.Attribute("r");
                            if (string.IsNullOrEmpty(cellRef)) continue;

                            int colIdx = ExtractColumnIndex(cellRef);
                            if (colIdx >= maxCols) continue;

                            if (colIdx > maxColFound) maxColFound = colIdx;

                            string cellType = (string?)cElem.Attribute("t") ?? string.Empty;
                            string val = string.Empty;

                            if (cellType == "s")
                            {
                                string? v = cElem.Element(sheetNs + "v")?.Value;
                                if (int.TryParse(v, out int sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                                {
                                    val = sharedStrings[sIdx];
                                }
                            }
                            else if (cellType == "inlineStr")
                            {
                                val = cElem.Element(sheetNs + "is")?.Element(sheetNs + "t")?.Value ?? string.Empty;
                            }
                            else if (cellType == "b")
                            {
                                val = cElem.Element(sheetNs + "v")?.Value == "1"
                                    ? LocalizationService.Get("Xlsx_Bool_True")
                                    : LocalizationService.Get("Xlsx_Bool_False");
                            }
                            else
                            {
                                val = cElem.Element(sheetNs + "v")?.Value ?? string.Empty;
                            }

                            cellMap[colIdx] = val;
                        }

                        // Popola la riga con celle vuote dove mancanti
                        var rowList = new List<string>();
                        int colsInThisRow = Math.Min(maxColFound + 1, maxCols);
                        for (int c = 0; c < colsInThisRow; c++)
                        {
                            rowList.Add(cellMap.TryGetValue(c, out var v) ? v : string.Empty);
                        }

                        rowsList.Add(rowList);
                        rowCount++;
                    }

                    // Allinea tutte le righe alla larghezza massima rilevata
                    int finalCols = Math.Min(maxColFound + 1, maxCols);
                    if (finalCols <= 0) finalCols = 1;

                    foreach (var r in rowsList)
                    {
                        while (r.Count < finalCols) r.Add(string.Empty);
                    }

                    sheetObj.TotalColCount = finalCols;
                    sheetObj.TotalRowCount = rowsList.Count;
                    sheetObj.Rows = rowsList;

                    // Genera intestazioni colonne A, B, C...
                    for (int i = 0; i < finalCols; i++)
                    {
                        sheetObj.ColumnHeaders.Add(IndexToColumnName(i));
                    }

                    model.Sheets.Add(sheetObj);
                }

                return model.Sheets.Count > 0 ? model : null;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Impossibile leggere file Excel '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static int ExtractColumnIndex(string cellRef)
        {
            int index = 0;
            foreach (char c in cellRef)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    index = (index * 26) + (c - 'A' + 1);
                }
                else if (c >= 'a' && c <= 'z')
                {
                    index = (index * 26) + (c - 'a' + 1);
                }
                else
                {
                    break;
                }
            }
            return Math.Max(0, index - 1);
        }

        public static string IndexToColumnName(int index)
        {
            string col = string.Empty;
            int dividend = index + 1;
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                col = Convert.ToChar('A' + modulo) + col;
                dividend = (dividend - modulo) / 26;
            }
            return col;
        }
    }
}
