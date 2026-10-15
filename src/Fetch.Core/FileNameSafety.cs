using System.Text;

namespace Fetch.Core;

public static class FileNameSafety
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "COM1", "COM2", "COM3", "COM4", "COM5",
        "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
        "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed)) return "download.bin";
        // Strip path components in either separator style; never trust server filenames.
        var input = proposed.Trim().Trim('"');
        input = input.Replace('\\', '/');
        input = input[(input.LastIndexOf('/') + 1)..];
        var clean = new StringBuilder(input.Length);
        foreach (char ch in input)
            clean.Append(ch < 32 || ch == 127 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        var safe = clean.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safe)) return "download.bin";
        string ext = Path.GetExtension(safe);
        // Windows device names remain reserved even when followed by multiple extensions.
        if (Reserved.Contains(safe.Split('.')[0])) safe = "_" + safe;
        if (safe.Length > 180)
        {
            ext = ext.Length > 30 ? ext[..30] : ext;
            safe = safe[..Math.Max(1, 180 - ext.Length)] + ext;
        }
        return safe;
    }

    public static string FromUrl(string rawUrl)
    {
        var uri = new Uri(rawUrl);
        string name;
        try { name = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? ""); }
        catch (UriFormatException) { name = "download.bin"; }
        return Sanitize(name);
    }

    public static string FreePath(string folder, string name, Func<string, bool>? taken = null)
    {
        name = Sanitize(name);
        taken ??= File.Exists;
        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        string choice = Path.Combine(folder, name);
        for (int n = 1; taken(choice); n++)
        {
            if (n > 10_000) throw new IOException("Too many name collisions.");
            choice = Path.Combine(folder, $"{stem} ({n}){ext}");
        }
        return choice;
    }
}
