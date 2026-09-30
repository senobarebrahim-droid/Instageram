using System.Data;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace Instageram.Tests;

public class ReportWriterTests
{
    [Fact]
    public void Xlsx_is_a_valid_package_with_every_required_part()
    {
        var path = TestEnvironment.TempFile(".xlsx");

        ReportWriter.WriteXlsx(path, SampleTable(), "Campaigns");

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
    }

    [Fact]
    public void Xlsx_keeps_numbers_numeric_and_escapes_markup()
    {
        var path = TestEnvironment.TempFile(".xlsx");

        ReportWriter.WriteXlsx(path, SampleTable(), "Campaigns");

        var sheet = ReadEntry(path, "xl/worksheets/sheet1.xml");

        Assert.Contains("<v>2000000</v>", sheet);
        Assert.Contains("<v>1.25</v>", sheet);
        Assert.Contains("A &amp; B &lt;test&gt;", sheet);
    }

    [Fact]
    public void Xlsx_sheet_name_is_sanitised_and_length_limited()
    {
        var path = TestEnvironment.TempFile(".xlsx");

        ReportWriter.WriteXlsx(path, SampleTable(), new string('x', 40) + ":/\\?*[]");

        var workbook = ReadEntry(path, "xl/workbook.xml");
        var match = System.Text.RegularExpressions.Regex.Match(workbook, "name=\"([^\"]*)\"");

        Assert.True(match.Success);
        Assert.True(match.Groups[1].Value.Length <= 31);
        Assert.DoesNotContain(":", match.Groups[1].Value);
    }

    [Fact]
    public void Html_report_is_printable_rtl_and_escapes_markup()
    {
        var path = TestEnvironment.TempFile(".html");

        ReportWriter.WriteHtml(
            path,
            "Campaign Report",
            SampleTable(),
            new[] { new KeyValuePair<string, string>("Projects", "2") });

        var html = File.ReadAllText(path);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("@media print", html);
        Assert.Contains("<th>Campaign</th>", html);
        Assert.Contains("A &amp; B &lt;test&gt;", html);
        Assert.Contains("<dd>2</dd>", html);
    }

    [Fact]
    public void Html_report_is_utf8_without_a_byte_order_mark()
    {
        var path = TestEnvironment.TempFile(".html");

        ReportWriter.WriteHtml(
            path,
            "Ú¯Ø²Ø§Ø±Ø´ Ú©Ù…Ù¾ÛŒÙ†â€ŒÙ‡Ø§",
            SampleTable(),
            Array.Empty<KeyValuePair<string, string>>());

        var bytes = File.ReadAllBytes(path);

        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "a BOM would break some browsers and the print pipeline");

        Assert.Contains("Ú¯Ø²Ø§Ø±Ø´ Ú©Ù…Ù¾ÛŒÙ†â€ŒÙ‡Ø§", File.ReadAllText(path));
    }

    [Fact]
    public void Writers_create_the_target_directory_when_it_is_missing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "instageram-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nested", "report.xlsx");

        ReportWriter.WriteXlsx(path, SampleTable(), "Campaigns");

        Assert.True(File.Exists(path));
    }

    private static DataTable SampleTable()
    {
        var table = new DataTable();

        table.Columns.Add("ID", typeof(long));
        table.Columns.Add("Campaign", typeof(string));
        table.Columns.Add("Target", typeof(long));
        table.Columns.Add("Progress", typeof(double));

        table.Rows.Add(1L, "Iran Campaign", 2000000L, 1.25);
        table.Rows.Add(2L, "A & B <test>", 500L, 0.0);

        return table;
    }

    private static string ReadEntry(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName);

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());

        return reader.ReadToEnd();
    }
}
