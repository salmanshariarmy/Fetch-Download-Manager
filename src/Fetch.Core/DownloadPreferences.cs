namespace Fetch.Core;

/// <summary>Persistent global limits, validated before use. Connections apply to new jobs.</summary>
public sealed class DownloadPreferences
{
    public int DesiredConnections { get; set; } = 8;
    public int MaxConcurrentJobs { get; set; } = 3;
    public int RateLimitKiB { get; set; } = 0;

    public void Validate()
    {
        if (DesiredConnections is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(DesiredConnections), "Connections must be between 1 and 32.");
        if (MaxConcurrentJobs is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentJobs), "Concurrent jobs must be between 1 and 8.");
        if (RateLimitKiB is < 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(RateLimitKiB), "Speed limit must be 0 to 1048576 KiB/s.");
    }
}
