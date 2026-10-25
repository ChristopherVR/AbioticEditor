using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Metadata-only fingerprint for an explicit catalog operation. It detects file replacement,
/// additions, removal, size and write-time changes without reading multi-gigabyte containers.
/// It is not a cryptographic content verification of files whose metadata was deliberately preserved.
/// </summary>
public static class GameDataFingerprint
{
    public static string Capture(string? paksDirectory, IEnumerable<string> observedFiles, string context)
    {
        ArgumentNullException.ThrowIfNull(observedFiles);
        var stamps = new List<string> { context, paksDirectory ?? string.Empty };
        foreach (var path in observedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            stamps.Add(FileStamp(path));
        if (!string.IsNullOrWhiteSpace(paksDirectory))
        {
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                };
                foreach (var path in Directory.EnumerateFiles(paksDirectory, "*", options)
                             .Where(IsContainer).Order(StringComparer.OrdinalIgnoreCase))
                    stamps.Add(FileStamp(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stamps.Add("containers-unreadable");
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', stamps))));
    }

    private static bool IsContainer(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".pak" or ".utoc" or ".ucas" or ".sig";

    private static string FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? string.Join('|', path, info.Length.ToString(CultureInfo.InvariantCulture), info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                : path + "|missing";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path + "|unreadable";
        }
    }
}
