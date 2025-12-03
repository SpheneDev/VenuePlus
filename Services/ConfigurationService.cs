using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VenuePlus.State;
using System.Collections.Concurrent;

namespace VenuePlus.Services;

public sealed class ConfigurationService
{
    private readonly string _configPath;
    private static readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);
    private static object GetLock(string path) { return _locks.GetOrAdd(path, _ => new object()); }

    public ConfigurationService(string? customPath = null)
    {
        _configPath = customPath ?? GetDefaultPath();
    }

    public List<VipEntry> Load()
    {
        if (!File.Exists(_configPath))
        {
            return new List<VipEntry>();
        }

        var json = File.ReadAllText(_configPath);
        var entries = JsonSerializer.Deserialize<List<VipEntry>>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        return entries ?? new List<VipEntry>();
    }

    public void Save(List<VipEntry> entries)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        File.WriteAllText(_configPath, json);
    }

    private static string GetDefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "VenuePlus");
        return Path.Combine(dir, "config.json");
    }

    public List<VipEntry> LoadClub(string clubId)
    {
        return new List<VipEntry>();
    }

    public void SaveClub(string clubId, List<VipEntry> entries)
    {
    }

    private string GetClubDataPath(string clubId)
    {
        var baseDir = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrEmpty(baseDir))
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            baseDir = Path.Combine(root, "VenuePlus");
        }
        var clubsDir = Path.Combine(baseDir!, "vips");
        var safe = Sanitize(clubId);
        return Path.Combine(clubsDir, $"{safe}.json");
    }

    private static string Sanitize(string input)
    {
        var chars = input ?? string.Empty;
        var arr = new System.Text.StringBuilder(chars.Length);
        foreach (var ch in chars)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' ) arr.Append(ch);
        }
        var s = arr.ToString();
        return string.IsNullOrWhiteSpace(s) ? "default" : s;
    }
}
