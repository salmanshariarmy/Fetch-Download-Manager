using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Fetch.Core;

/// <summary>
/// Optional, user-installed yt-dlp/FFmpeg integration. Runs without a shell,
/// passes no browser cookies or credentials, and never bypasses DRM.
/// </summary>
public sealed class MediaDownloader
{
    public static bool IsKnownMediaUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
        string host = uri.Host.ToLowerInvariant();
        if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) ||
            host == "youtu.be" || host == "vimeo.com" || host.EndsWith(".vimeo.com", StringComparison.Ordinal))
            return true;
        return uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatForQuality(string quality) => quality switch
    {
        "1080p" => "bv*[height<=1080]+ba/b[height<=1080]/b",
        "720p" => "bv*[height<=720]+ba/b[height<=720]/b",
        "480p" => "bv*[height<=480]+ba/b[height<=480]/b",
        "Audio only" => "ba/b",
        _ => "bv*+ba/b"
    };

    public static string? LocateTool(string executable)
    {
        if (executable is not ("yt-dlp.exe" or "ffmpeg.exe"))
            throw new ArgumentException("Unsupported helper.", nameof(executable));
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools", executable),
            Path.Combine(AppContext.BaseDirectory, executable),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Fetch", "tools", executable)
        };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string value = directory.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(value)) candidates.Add(Path.Combine(value, executable));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Parses the structured output from --progress-template.</summary>
    public static bool TryParseProgress(string line, out TransferProgress current)
    {
        current = new TransferProgress(0, null, 0);
        const string prefix = "FETCH_PROGRESS:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string[] fields = line[prefix.Length..].Split('|');
        if (fields.Length < 3 || !long.TryParse(fields[0], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long done)) return false;
        long? total = long.TryParse(fields[1], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long count) && count > 0 ? count : null;
        double speed = double.TryParse(fields[2], NumberStyles.Float,
            CultureInfo.InvariantCulture, out double rate) && rate >= 0 ? rate : 0;
        current = new TransferProgress(Math.Max(0, done), total, speed);
        return true;
    }

    public async Task<TransferResult> DownloadAsync(DownloadItem item,
        IProgress<TransferProgress>? progress, CancellationToken cancellation)
    {
        string executable = LocateTool("yt-dlp.exe") ?? throw new FileNotFoundException(
            "yt-dlp.exe was not found. Install yt-dlp and FFmpeg from their official releases, " +
            "or run scripts\\Install-MediaTools.ps1, then restart Fetch.");
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrWhiteSpace(address.UserInfo))
            throw new ArgumentException("Only normal HTTP(S) media links are supported.");
        Directory.CreateDirectory(item.SaveDirectory);
        string prefix = "fetch-" + item.Id.ToString("N") + "-";
        var processInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        void Add(params string[] args)
        {
            foreach (string arg in args) processInfo.ArgumentList.Add(arg);
        }
        Add("--ignore-config", "--no-playlist", "--no-simulate", "--no-overwrites",
            "--continue", "--newline", "--no-color", "--windows-filenames",
            "--retries", "5", "--fragment-retries", "5", "--concurrent-fragments", "4",
            "--progress-template", "download:FETCH_PROGRESS:%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.speed)s",
            "--print", "after_move:FETCH_FINAL:%(filepath)s",
            "--format", FormatForQuality(item.MediaQuality),
            "--paths", item.SaveDirectory,
            "--output", prefix + "%(title).150B.%(ext)s");
        string? ffmpeg = LocateTool("ffmpeg.exe");
        if (ffmpeg is not null)
        {
            Add("--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!);
            if (item.MediaQuality != "Audio only") Add("--merge-output-format", "mp4");
        }
        else if (item.MediaQuality != "Audio only")
        {
            // Avoid selecting video-only and audio-only tracks without a muxer.
            int formatAt = processInfo.ArgumentList.IndexOf("--format");
            processInfo.ArgumentList[formatAt + 1] = item.MediaQuality switch
            {
                "1080p" => "b[height<=1080]/b",
                "720p" => "b[height<=720]/b",
                "480p" => "b[height<=480]/b",
                _ => "b"
            };
        }
        Add(item.Url);

        using var process = new Process { StartInfo = processInfo };
        var errors = new Queue<string>();
        string? finalPath = null;
        if (!process.Start()) throw new IOException("Could not start yt-dlp.");
        async Task ReadOutputAsync()
        {
            while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line)
            {
                if (TryParseProgress(line, out var sample)) progress?.Report(sample);
                else if (line.StartsWith("FETCH_FINAL:", StringComparison.Ordinal))
                    finalPath = line["FETCH_FINAL:".Length..].Trim();
            }
        }
        async Task ReadErrorsAsync()
        {
            while (await process.StandardError.ReadLineAsync(cancellation) is { } line)
            {
                if (errors.Count == 12) errors.Dequeue();
                errors.Enqueue(line);
            }
        }
        try
        {
            await Task.WhenAll(ReadOutputAsync(), ReadErrorsAsync(),
                process.WaitForExitAsync(cancellation)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
            throw new IOException("Media download failed: " +
                string.Join(" | ", errors.TakeLast(4)));
        // No shell expansion or trusting a filepath printed by the helper.
        // Only recognize outputs that belong to this job's private prefix.
        var saved = Directory.EnumerateFiles(item.SaveDirectory, prefix + "*")
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (saved is null || !File.Exists(saved))
            throw new IOException("yt-dlp reported success, but the finished media file was not found.");
        if (finalPath is not null && !string.Equals(Path.GetFullPath(saved),
                Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
        {
            // The output directory is authoritative: do not follow an arbitrary path.
            throw new IOException("Media output path did not match the job's expected directory.");
        }
        long size = new FileInfo(saved).Length;
        progress?.Report(new TransferProgress(size, size, 0));
        return new TransferResult(saved, size);
    }
}
