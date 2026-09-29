// Phase 4: extracted from MainWindow.xaml.cs. Behaviour is unchanged;
// this file only groups one responsibility so the code stays maintainable.
using System.Text.RegularExpressions;

namespace Instageram;

public static class InstagramValidator
{
    public static bool TryGetUsername(string? value, out string username, out string message)
    {
        username = "";
        message = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            message = "آدرس Instagram را وارد کنید.";
            return false;
        }

        var url = value.Trim();

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            message = "URL معتبر نیست.";
            return false;
        }

        if (uri.Host.ToLowerInvariant() != "instagram.com" &&
            uri.Host.ToLowerInvariant() != "www.instagram.com")
        {
            message = "فقط URL معتبر instagram.com پذیرفته می‌شود.";
            return false;
        }

        var part = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(part) || !Regex.IsMatch(part, "^[A-Za-z0-9._]{1,30}$"))
        {
            message = "Username معتبر در URL پیدا نشد.";
            return false;
        }

        username = part;
        return true;
    }
}
