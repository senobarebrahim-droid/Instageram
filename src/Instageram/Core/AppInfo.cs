using System.Reflection;
using System.Runtime.InteropServices;

namespace Instageram;

/// <summary>
/// Phase 4: one place that knows what this build is, so the window, the log,
/// the diagnostics report and the release manifest can never disagree.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "INSTAGERAM";

    /// <summary>Version taken from the assembly, e.g. "0.2.0".</summary>
    public static string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Some build setups append "+<commit>"; keep only the version.
                var plus = informational.IndexOf('+');

                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    public static int SchemaVersion => SchemaMigrator.CurrentVersion;

    public static string Framework => RuntimeInformation.FrameworkDescription;

    public static string OperatingSystem => RuntimeInformation.OSDescription;

    /// <summary>Short line used in the UI and in the log.</summary>
    public static string Summary() => $"{ProductName} {Version} (schema v{SchemaVersion})";
}
