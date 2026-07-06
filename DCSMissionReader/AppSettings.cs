using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DCSMissionReader;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DCSMissionReader", "settings.json");

    public string LastFolderPath { get; set; } = "";
    public List<string> SearchHistory { get; set; } = new();
    public bool IncludeSubfolders { get; set; } = true;

    private static volatile AppSettings? _instance;
    private static readonly object Lock = new();
    private static DateTime _lastSaveTime = DateTime.MinValue;

    public static AppSettings Load()
    {
        if (_instance != null) return _instance;
        lock (Lock)
        {
            if (_instance != null) return _instance;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _instance = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    _instance = new AppSettings();
                }
            }
            catch
            {
                _instance = new AppSettings();
            }
            return _instance;
        }
    }

    public void Save()
    {
        lock (Lock)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }

    public void AddSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        SearchHistory.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, query);
        if (SearchHistory.Count > 20) SearchHistory.RemoveRange(20, SearchHistory.Count - 20);
        // Throttle saves to at most once per 5 seconds to reduce I/O churn during rapid searches
        var now = DateTime.UtcNow;
        if ((now - _lastSaveTime).TotalSeconds >= 5)
        {
            _lastSaveTime = now;
            Save();
        }
    }
}
