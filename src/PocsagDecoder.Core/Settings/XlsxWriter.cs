using System.IO.Compression;
using System.Security;
using System.Text;

namespace PocsagDecoder.Core.Settings;

/// <summary>Schreibt eine einfache Excel-Datei (.xlsx): eine Tabelle, fette Kopfzeile, Filter, fixierte erste Zeile.</summary>
public static class XlsxWriter
{
    public static void Write(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows,
        IReadOnlyList<double>? widths = null)
    {
        var tmp = path + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Add(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="{Esc(SheetName(sheetName))}" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            // Stil 0 = normal, 1 = fett mit grauem Hintergrund (Kopf), 2 = Datum/Uhrzeit
            Add(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <numFmts count="1"><numFmt numFmtId="164" formatCode="dd.mm.yyyy hh:mm:ss"/></numFmts>
                <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE5E7EB"/></patternFill></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs>
                <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                </styleSheet>
                """);

            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            w.Write("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
            if (widths is { Count: > 0 })
            {
                w.Write("<cols>");
                for (var i = 0; i < widths.Count; i++)
                    w.Write($"""<col min="{i + 1}" max="{i + 1}" width="{widths[i].ToString(System.Globalization.CultureInfo.InvariantCulture)}" customWidth="1"/>""");
                w.Write("</cols>");
            }
            w.Write("<sheetData>");
            WriteRow(w, 1, headers.Cast<object?>().ToList(), header: true);
            var r = 2;
            foreach (var row in rows) WriteRow(w, r++, row, header: false);
            w.Write("</sheetData>");
            w.Write($"""<autoFilter ref="A1:{Column(headers.Count - 1)}{Math.Max(1, r - 1)}"/>""");
            w.Write("</worksheet>");
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static void WriteRow(TextWriter w, int r, IReadOnlyList<object?> cells, bool header)
    {
        w.Write($"""<row r="{r}">""");
        for (var c = 0; c < cells.Count; c++)
        {
            var at = Column(c) + r;
            var style = header ? " s=\"1\"" : "";
            switch (cells[c])
            {
                case null:
                    break;
                case DateTime d:
                    w.Write($"""<c r="{at}" s="2"><v>{d.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                case int or long or double:
                    w.Write($"""<c r="{at}"{style}><v>{Convert.ToString(cells[c], System.Globalization.CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                default:
                    w.Write($"""<c r="{at}" t="inlineStr"{style}><is><t xml:space="preserve">{Esc(cells[c]!.ToString()!)}</t></is></c>""");
                    break;
            }
        }
        w.Write("</row>");
    }

    /// <summary>0 → A, 25 → Z, 26 → AA</summary>
    public static string Column(int index)
    {
        var s = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26)
            s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    private static string SheetName(string name)
    {
        var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return clean.Length == 0 ? "Tabelle1" : clean.Length > 31 ? clean[..31] : clean;
    }

    // XML erlaubt keine Steuerzeichen außer Tab/Zeilenumbruch.
    private static string Esc(string s) =>
        SecurityElement.Escape(new string(s.Where(c => c is '\t' or '\n' or '\r' || !char.IsControl(c)).ToArray())) ?? string.Empty;

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content.Trim());
    }
}
