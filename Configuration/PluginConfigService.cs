using System;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;

namespace VenuePlus.Configuration;

public sealed class PluginConfigService
{
    private readonly string _path;
    private PluginConfig _cache = new();
    private static readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);
    private static object GetLock(string path) => _locks.GetOrAdd(path, _ => new object());

    public PluginConfigService(string path)
    {
        _path = path;
        _cache = LoadInternal();
    }

    public PluginConfig Current => _cache;

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        var tmp = _path + ".tmp";
        lock (GetLock(_path))
        {
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
    }

    private PluginConfig LoadInternal()
    {
        if (!File.Exists(_path))
        {
            return new PluginConfig { };
        }
        string json;
        lock (GetLock(_path))
        {
            json = File.ReadAllText(_path);
        }
        var cfg = JsonSerializer.Deserialize<PluginConfig>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        cfg ??= new PluginConfig { };
        if (cfg.ProfilesByCharacter == null)
        {
            cfg.ProfilesByCharacter = new System.Collections.Generic.Dictionary<string, CharacterProfile>(StringComparer.Ordinal);
        }
        return cfg;
    }
}
