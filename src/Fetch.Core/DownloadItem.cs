namespace Fetch.Core;

public static class JobStatus
{
    public const string Queued = "Queued";
    public const string Downloading = "Downloading";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public sealed class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "download.bin";
    public string SaveDirectory { get; set; } = "";
    public string Status { get; set; } = JobStatus.Queued;
    public long? TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public int SegmentCount { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    // Media jobs are handled by optional yt-dlp; legacy items default to direct HTTP.
    public bool IsMedia { get; set; }
    public string MediaQuality { get; set; } = "Best";
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record TransferProgress(long DownloadedBytes, long? TotalBytes, double BytesPerSecond);
public sealed record TransferResult(string SavedPath, long Bytes);
