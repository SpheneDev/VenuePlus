using System;
using System.Linq;
using VenuePlus.State;

namespace VenuePlus.Services;

internal sealed class DjService
{
    private DjEntry[] _entries = Array.Empty<DjEntry>();

    public DjEntry[] GetAll()
    {
        return _entries;
    }

    public void ReplaceAll(DjEntry[]? entries)
    {
        _entries = (entries ?? Array.Empty<DjEntry>()).OrderBy(e => e.DjName, StringComparer.Ordinal).ToArray();
    }

    public void SetOrAdd(DjEntry entry)
    {
        var list = new System.Collections.Generic.List<DjEntry>(_entries);
        var idx = list.FindIndex(x => string.Equals(x.DjName, entry.DjName, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = entry; else list.Add(entry);
        _entries = list.OrderBy(x => x.DjName, StringComparer.Ordinal).ToArray();
    }

    public void RemoveByName(string name)
    {
        _entries = _entries.Where(x => !string.Equals(x.DjName, name ?? string.Empty, StringComparison.Ordinal)).ToArray();
    }

    public static string NormalizeTwitchLink(string twitchLink)
    {
        var s = twitchLink?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        if (lower.StartsWith("http://")) s = "https://" + s.Substring(7);
        lower = s.ToLowerInvariant();
        if (lower.StartsWith("https://www.twitch.tv/")) s = "https://twitch.tv/" + s.Substring("https://www.twitch.tv/".Length);
        else if (lower.StartsWith("www.twitch.tv/")) s = "https://twitch.tv/" + s.Substring("www.twitch.tv/".Length);
        else if (lower.StartsWith("twitch.tv/")) s = "https://" + s;
        else if (!lower.Contains("twitch.tv"))
        {
            var channel = s.TrimStart('@').Trim('/');
            if (string.IsNullOrWhiteSpace(channel)) return string.Empty;
            channel = channel.ToLowerInvariant();
            s = "https://twitch.tv/" + channel;
        }
        if (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
        return s;
    }

    public void Clear()
    {
        _entries = Array.Empty<DjEntry>();
    }
}

