using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VenuePlus.Services;

public sealed class ChangelogService
{
    private readonly HttpClient _httpClient = new();
    private IReadOnlyList<ChangelogEntry>? _cache;
    private readonly object _lock = new();

    public async Task<IReadOnlyList<ChangelogEntry>> FetchAsync(string url)
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
        }
        var json = await _httpClient.GetStringAsync(url);
        var list = Parse(json);
        lock (_lock) { _cache = list; }
        return list;
    }

    private static IReadOnlyList<ChangelogEntry> Parse(string json)
    {
        var result = new List<ChangelogEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var e = ReadEntry(el);
                    if (e != null) result.Add(e);
                }
            }
            else if (doc.RootElement.TryGetProperty("changelogs", out var logs))
            {
                foreach (var el in logs.EnumerateArray())
                {
                    var e = ReadEntry(el);
                    if (e != null) result.Add(e);
                }
            }
            else if (doc.RootElement.TryGetProperty("entries", out var entries))
            {
                foreach (var el in entries.EnumerateArray())
                {
                    var e = ReadEntry(el);
                    if (e != null) result.Add(e);
                }
            }
            else if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                foreach (var el in versions.EnumerateArray())
                {
                    var e = ReadEntry(el);
                    if (e != null) result.Add(e);
                }
            }
        }
        catch { }
        return result;
    }

    private static ChangelogEntry? ReadEntry(JsonElement el)
    {
        try
        {
            var ver = el.GetProperty("version").GetString();
            var title = el.TryGetProperty("title", out var t) ? t.GetString() : null;
            var desc = el.TryGetProperty("description", out var d0) ? d0.GetString() : null;
            var date = el.TryGetProperty("date", out var d) ? d.GetString() : null;
            List<ChangelogSection> sections = new();
            if (el.TryGetProperty("changes", out var c) && c.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in c.EnumerateArray())
                {
                    var sDesc = i.TryGetProperty("description", out var sd) ? sd.GetString() : null;
                    var items = new List<string>();
                    if (i.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var si in sub.EnumerateArray()) items.Add(si.GetString() ?? string.Empty);
                    }
                    sections.Add(new ChangelogSection { Description = sDesc ?? string.Empty, Items = items });
                }
            }
            else if (el.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var i in n.EnumerateArray()) items.Add(i.GetString() ?? string.Empty);
                sections.Add(new ChangelogSection { Description = string.Empty, Items = items });
            }
            if (string.IsNullOrWhiteSpace(ver)) return null;
            return new ChangelogEntry { Version = ver!, Title = title, Description = desc, Date = date, Sections = sections };
        }
        catch { return null; }
    }
}

public sealed class ChangelogEntry
{
    public string Version { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Date { get; set; }
    public List<ChangelogSection> Sections { get; set; } = new();
}

public sealed class ChangelogSection
{
    public string Description { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
}

