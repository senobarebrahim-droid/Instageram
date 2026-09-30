using System.IO;

namespace Instageram;

/// <summary>
/// Small, dependency-free file helpers shared by the services.
/// Phase 3: cleanup must never invalidate an otherwise successful operation.
/// </summary>
public static class FileTools
{
    /// <summary>
    /// Deletes a folder if it exists. A locked or half-deleted folder is
    /// logged instead of thrown, so a successful operation stays successful.
    /// </summary>
    public static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Cleanup", path + " could not be removed: " + ex.Message);
        }
    }

    /// <summary>
    /// Removes sub-folders left behind by an interrupted run. Files (such as
    /// .gitkeep) are left alone on purpose.
    /// </summary>
    public static int CleanDirectoryChildren(string path)
    {
        var removed = 0;

        try
        {
            if (!Directory.Exists(path))
                return 0;

            foreach (var child in Directory.GetDirectories(path))
            {
                SafeDeleteDirectory(child);
                removed++;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Cleanup", path + " could not be cleaned: " + ex.Message);
        }

        return removed;
    }
}
