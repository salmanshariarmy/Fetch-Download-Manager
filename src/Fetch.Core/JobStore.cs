using System.Text.Json;

namespace Fetch.Core;

public sealed class JobStore
{
    private readonly object _lock = new();
    public string DataFolder { get; }
    public string PartsFolder => Path.Combine(DataFolder, "parts");
    public string JobsPath => Path.Combine(DataFolder, "jobs.json");
    public string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public JobStore(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fetch");
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(PartsFolder);
    }

    public List<DownloadItem> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(JobsPath)) return new List<DownloadItem>();
            var jobs = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(JobsPath))
                ?? new List<DownloadItem>();
            foreach (var job in jobs)
                if (job.Status is JobStatus.Downloading or JobStatus.Queued)
                    job.Status = JobStatus.Paused;
            return jobs;
        }
    }

    public void Save(IEnumerable<DownloadItem> jobs)
    {
        lock (_lock)
            SaveAtomic(JobsPath, JsonSerializer.Serialize(jobs.ToArray(), JsonOptions));
    }

    public string LoadDownloadDirectory()
    {
        lock (_lock)
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!File.Exists(SettingsPath)) return fallback;
            try
            {
                string? value = JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath));
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch (JsonException) { return fallback; }
        }
    }

    public void SaveDownloadDirectory(string folder)
    {
        lock (_lock) SaveAtomic(SettingsPath, JsonSerializer.Serialize(folder));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static void SaveAtomic(string file, string text)
    {
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, file, true);
    }
}
