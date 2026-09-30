using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Instageram;

/// <summary>
/// Phase 3: dependency-free report writers.
///
/// XLSX is produced as raw SpreadsheetML inside a ZIP, and the printable
/// report as one self-contained HTML file. Avoiding third-party packages keeps
/// the application portable and small, and the HTML report can be turned into
/// a PDF with the browser's own "Print to PDF".
/// </summary>
public static class ReportWriter
{
    private const string SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string OfficeRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    // ------------------------------------------------------------------ XLSX

    public static string WriteXlsx(string path, DataTable table, string sheetName)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
            File.Delete(path);

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        AddEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>");

        AddEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<Relationships xmlns=\"{PackageRelationshipsNamespace}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{OfficeRelationshipsNamespace}/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        AddEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<workbook xmlns=\"{SpreadsheetNamespace}\" xmlns:r=\"{OfficeRelationshipsNamespace}\">" +
            $"<sheets><sheet name=\"{Escape(SafeSheetName(sheetName))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>");

        AddEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            $"<Relationships xmlns=\"{PackageRelationshipsNamespace}\">" +
            $"<Relationship Id=\"rId1\" Type=\"{OfficeRelationshipsNamespace}/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>");

        AddEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(table));

        AppLogger.Info("Report", "XLSX written: " + path);

        return path;
    }

    private static string SheetXml(DataTable table)
    {
        var builder = new StringBuilder();

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append($"<worksheet xmlns=\"{SpreadsheetNamespace}\">");

        builder.Append("<cols>");
        for (var column = 0; column < table.Columns.Count; column++)
        {
            builder.Append(
                $"<col min=\"{column + 1}\" max=\"{column + 1}\" width=\"20\" customWidth=\"1\"/>");
        }
        builder.Append("</cols>");

        builder.Append("<sheetData>");

        builder.Append("<row r=\"1\">");
        for (var column = 0; column < table.Columns.Count; column++)
            builder.Append(CellXml(ColumnName(column) + "1", table.Columns[column].ColumnName));
        builder.Append("</row>");

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var rowNumber = row + 2;

            builder.Append($"<row r=\"{rowNumber}\">");

            for (var column = 0; column < table.Columns.Count; column++)
                builder.Append(CellXml(ColumnName(column) + rowNumber, table.Rows[row][column]));

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");

        return builder.ToString();
    }

    private static string CellXml(string reference, object? value)
    {
        if (value == null || value == DBNull.Value)
            return $"<c r=\"{reference}\"/>";

        if (value is int or long or short or byte)
            return $"<c r=\"{reference}\"><v>{Convert.ToInt64(value, CultureInfo.InvariantCulture)}</v></c>";

        if (value is double or float or decimal)
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture)
                .ToString("0.####", CultureInfo.InvariantCulture);

            return $"<c r=\"{reference}\"><v>{number}</v></c>";
        }

        return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" +
               $"{Escape(value.ToString() ?? "")}</t></is></c>";
    }

    /// <summary>0 -> A, 25 -> Z, 26 -> AA ...</summary>
    private static string ColumnName(int index)
    {
        var name = "";
        var value = index;

        do
        {
            name = (char)('A' + value % 26) + name;
            value = value / 26 - 1;
        }
        while (value >= 0);

        return name;
    }

    private static string SafeSheetName(string sheetName)
    {
        var cleaned = new string((sheetName ?? "Sheet1")
            .Where(c => c != ':' && c != '\\' && c != '/' && c != '?' && c != '*' && c != '[' && c != ']')
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Sheet1";

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        writer.Write(content);
    }

    // ------------------------------------------------------------------ HTML

    public static string WriteHtml(
        string path,
        string title,
        DataTable table,
        IReadOnlyList<KeyValuePair<string, string>> information)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();

        builder.Append("<!DOCTYPE html>");
        builder.Append("<html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"/>");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        builder.Append($"<title>{Escape(title)}</title>");
        builder.Append("<style>");
        builder.Append(
            "*{box-sizing:border-box}" +
            "body{font-family:'Segoe UI',Tahoma,'Iranian Sans',sans-serif;margin:32px;color:#1b263b;background:#fff}" +
            "h1{font-size:22px;margin:0 0 6px}" +
            ".sub{color:#607080;margin:0 0 22px;font-size:13px}" +
            "dl.meta{display:grid;grid-template-columns:auto 1fr;gap:4px 14px;margin:0 0 24px;" +
            "background:#eaf2fa;border:1px solid #c6d6e8;border-radius:8px;padding:16px 20px}" +
            "dl.meta dt{color:#607080;font-size:13px}" +
            "dl.meta dd{margin:0;font-weight:600;font-size:13px}" +
            "table{border-collapse:collapse;width:100%;font-size:13px}" +
            "th,td{border:1px solid #d8e0e8;padding:7px 10px;text-align:right}" +
            "th{background:#1b263b;color:#fff;font-weight:600}" +
            "tbody tr:nth-child(even){background:#f6f9fc}" +
            ".footer{margin-top:26px;color:#8a97a6;font-size:12px}" +
            "@media print{body{margin:12mm}th{background:#1b263b !important;-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
            "tbody tr:nth-child(even){background:#f6f9fc !important;-webkit-print-color-adjust:exact;print-color-adjust:exact}}");
        builder.Append("</style></head><body>");

        builder.Append($"<h1>{Escape(title)}</h1>");
        builder.Append($"<p class=\"sub\">INSTAGERAM &middot; {DateTime.Now:yyyy-MM-dd HH:mm}</p>");

        builder.Append("<dl class=\"meta\">");
        foreach (var pair in information)
        {
            builder.Append($"<dt>{Escape(pair.Key)}</dt><dd>{Escape(pair.Value)}</dd>");
        }
        builder.Append("</dl>");

        builder.Append("<table><thead><tr>");
        foreach (DataColumn column in table.Columns)
            builder.Append($"<th>{Escape(column.ColumnName)}</th>");
        builder.Append("</tr></thead><tbody>");

        foreach (DataRow row in table.Rows)
        {
            builder.Append("<tr>");

            foreach (var value in row.ItemArray)
                builder.Append($"<td>{Escape(value?.ToString() ?? "")}</td>");

            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        builder.Append("<p class=\"footer\">INSTAGERAM &middot; Portable Campaign Manager</p>");
        builder.Append("</body></html>");

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));

        AppLogger.Info("Report", "HTML report written: " + path);

        return path;
    }

    // --------------------------------------------------------------- helpers

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 16);

        foreach (var character in value)
        {
            switch (character)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&apos;"); break;
                default:
                    // XML 1.0 forbids most control characters outright.
                    if (character < 0x20 && character != '\t' && character != '\n' && character != '\r')
                        break;

                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
