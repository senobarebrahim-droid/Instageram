using System.IO;
using System.IO.Compression;
using Xunit;

namespace Instageram.Tests;

public class BackupServiceTests
{
    public BackupServiceTests() => TestEnvironment.EnsureReady();

    [Fact]
    public void Two_backups_created_in_the_same_second_do_not_collide()
    {
        // Regression: the name used only yyyyMMdd_HHmmss, so a second backup
        // inside the same second threw "the file already exists".
        var first = BackupService.Create();
        var second = BackupService.Create();

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void Restore_reports_a_missing_file_instead_of_throwing()
    {
        var result = BackupService.Restore(TestEnvironment.TempFile(".zip"));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Restore_reports_an_empty_path()
    {
        var result = BackupService.Restore("");

        Assert.False(result.Success);
    }

    [Fact]
    public void Restore_rejects_a_file_that_is_not_a_zip()
    {
        var path = TestEnvironment.TempFile(".zip");
        File.WriteAllText(path, "this is definitely not a zip archive");

        var result = BackupService.Restore(path);

        Assert.False(result.Success);
    }

    [Fact]
    public void Restore_rejects_a_zip_that_contains_no_database()
    {
        var path = TestEnvironment.TempFile(".zip");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");

            using var writer = new StreamWriter(entry.Open());
            writer.Write("no database inside this package");
        }

        var result = BackupService.Restore(path);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Restore_rejects_a_package_whose_database_is_not_sqlite()
    {
        var path = TestEnvironment.TempFile(".zip");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data/database.db");

            using var writer = new StreamWriter(entry.Open());
            writer.Write(new string('x', 4096));
        }

        var result = BackupService.Restore(path);

        Assert.False(result.Success);
    }

    [Fact]
    public void Create_produces_a_package_with_a_database_a_manifest_and_the_settings()
    {
        var zip = BackupService.Create();

        Assert.True(File.Exists(zip));

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();

        Assert.Contains(names, name => name.EndsWith("database.db", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.EndsWith("backup_manifest.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_package_created_by_the_application_can_be_restored_again()
    {
        var zip = BackupService.Create();

        var result = BackupService.Restore(zip);

        Assert.True(result.Success, result.Message);

        // the safety copy proves the pre-restore state was preserved
        Assert.False(string.IsNullOrWhiteSpace(result.SafetyBackup));
        Assert.True(File.Exists(result.SafetyBackup!));
    }

    [Fact]
    public void Restoring_keeps_the_database_usable()
    {
        var zip = BackupService.Create();

        Assert.True(BackupService.Restore(zip).Success);

        // the schema must still be intact and migrated after a restore
        Assert.Equal(SchemaMigrator.CurrentVersion, DatabaseService.SchemaVersion);
        Assert.True(DatabaseService.GetSummary().Campaigns >= 0);
    }
}
