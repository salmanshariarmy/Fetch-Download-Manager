using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Fetch.Core;

namespace Fetch.Desktop.ViewModels;

public sealed class DownloadJobViewModel(DownloadItem item) : INotifyPropertyChanged
{
    public DownloadItem Item { get; } = item;
    public event PropertyChangedEventHandler? PropertyChanged;
    private double _speed;

    public string Name => Item.FileName;
    public string Status => Item.Status;
    public string Error => Item.Error ?? "";
    public string Url => Item.Url;
    public string Location => Path.Combine(Item.SaveDirectory, Item.FileName);
    public string Host
    {
        get
        {
            return Uri.TryCreate(Item.Url, UriKind.Absolute, out var uri) ? uri.Host : "";
        }
    }
    public string DateText => Item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string Category
    {
        get
        {
            string extension = Path.GetExtension(Item.FileName).ToLowerInvariant();
            return extension switch
            {
                ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".wmv" or ".flv" => "Video",
                ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" => "Music",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "Compressed",
                ".exe" or ".msi" or ".msix" or ".appx" or ".iso" or ".apk" => "Programs",
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".csv" or ".epub" => "Documents",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "Images",
                _ => "Other"
            };
        }
    }

    public string SizeText => Item.TotalBytes is long size ? FormatBytes(size) : "Unknown";
    public string ProgressText => Item.TotalBytes is > 0
        ? $"{(double)Item.DownloadedBytes / Item.TotalBytes.Value:P0}"
        : FormatBytes(Item.DownloadedBytes);
    public double ProgressPercent => Item.TotalBytes is > 0
        ? Math.Clamp(Item.DownloadedBytes * 100.0 / Item.TotalBytes.Value, 0, 100) : 0;
    public string SpeedText => Item.Status == JobStatus.Downloading ? FormatBytes((long)_speed) + "/s" : "—";
    public string RemainingText
    {
        get
        {
            if (Item.Status != JobStatus.Downloading || _speed <= 1 || Item.TotalBytes is not long total) return "—";
            double seconds = Math.Max(0, total - Item.DownloadedBytes) / _speed;
            if (double.IsInfinity(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds) return "—";
            var remaining = TimeSpan.FromSeconds(seconds);
            return remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d {remaining.Hours}h" :
                remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m" :
                $"{remaining.Minutes}m {remaining.Seconds}s";
        }
    }
    public string Description => Item.Error is { Length: > 0 } error ? error : Host;

    public void Update(TransferProgress progress)
    {
        Item.DownloadedBytes = progress.DownloadedBytes;
        Item.TotalBytes = progress.TotalBytes;
        _speed = progress.BytesPerSecond;
        Refresh();
    }

    public void Refresh()
    {
        foreach (string name in new[] { nameof(Name), nameof(Status), nameof(Error), nameof(SizeText),
            nameof(ProgressText), nameof(ProgressPercent), nameof(SpeedText), nameof(RemainingText),
            nameof(Category), nameof(Location), nameof(Description) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static string FormatBytes(long amount)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = Math.Max(0, amount);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
