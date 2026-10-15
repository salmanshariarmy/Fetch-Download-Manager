using System.ComponentModel;
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
    public string SizeText => Item.TotalBytes is long size ? FormatBytes(size) : "Unknown";
    public string ProgressText => Item.TotalBytes is > 0
        ? $"{(double)Item.DownloadedBytes / Item.TotalBytes.Value:P0}"
        : FormatBytes(Item.DownloadedBytes);
    public double ProgressPercent => Item.TotalBytes is > 0
        ? Math.Clamp(Item.DownloadedBytes * 100.0 / Item.TotalBytes.Value, 0, 100) : 0;
    public string SpeedText => Item.Status == JobStatus.Downloading ? FormatBytes((long)_speed) + "/s" : "—";

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
            nameof(ProgressText), nameof(ProgressPercent), nameof(SpeedText) })
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
