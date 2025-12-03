using System;
using System.Collections.Generic;
using System.Linq;
using VenuePlus.State;

namespace VenuePlus.Services;

public sealed class VipService
{
    private readonly ConfigurationService _config;
    private readonly Dictionary<string, Dictionary<string, VipEntry>> _entriesByClub = new(StringComparer.Ordinal);
    private string? _activeClub;
    private static readonly Dictionary<string, VipEntry> _empty = new(StringComparer.Ordinal);

    public VipService(ConfigurationService config)
    {
        _config = config;
        PurgeExpired();
    }

    public void SetActiveClub(string? clubId)
    {
        _activeClub = string.IsNullOrWhiteSpace(clubId) ? null : clubId!.Trim();
        if (_activeClub != null) EnsureClubInitialized(_activeClub);
    }

    public IReadOnlyCollection<VipEntry> GetAll(bool includeExpired = false)
    {
        var map = GetActiveMap();
        if (includeExpired) return map.Values.OrderBy(e => e.CharacterName).ToArray();
        var now = DateTimeOffset.UtcNow;
        return map.Values.Where(e => !e.IsExpired(now)).OrderBy(e => e.CharacterName).ToArray();
    }

    public bool ExistsActive(string characterName, string homeWorld)
    {
        var map = GetActiveMap();
        var key = characterName + "@" + homeWorld;
        if (map.TryGetValue(key, out var entry))
        {
            var now = DateTimeOffset.UtcNow;
            return !entry.IsExpired(now);
        }
        return false;
    }

    public VipEntry AddOrUpdate(string characterName, string homeWorld, VipDuration duration)
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return new VipEntry { CharacterName = characterName, HomeWorld = homeWorld, Duration = duration };
        var map = GetActiveMap();
        var key = characterName + "@" + homeWorld;
        var now = DateTimeOffset.UtcNow;
        if (map.TryGetValue(key, out var existing))
        {
            existing.Duration = duration;
            existing.ExpiresAt = CalculateExpiry(existing.CreatedAt, duration);
            _config.SaveClub(_activeClub!, map.Values.ToList());
            return existing;
        }

        var entry = new VipEntry
        {
            CharacterName = characterName,
            HomeWorld = homeWorld,
            CreatedAt = now,
            Duration = duration,
            ExpiresAt = CalculateExpiry(now, duration)
        };

        map[key] = entry;
        _config.SaveClub(_activeClub!, map.Values.ToList());
        return entry;
    }

    public VipEntry SetFromRemote(VipEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return entry;
        var map = GetActiveMap();
        var key = entry.CharacterName + "@" + entry.HomeWorld;
        map[key] = new VipEntry
        {
            CharacterName = entry.CharacterName,
            HomeWorld = entry.HomeWorld,
            CreatedAt = entry.CreatedAt,
            Duration = entry.Duration,
            ExpiresAt = entry.ExpiresAt
        };
        _config.SaveClub(_activeClub!, map.Values.ToList());
        return map[key];
    }

    public bool Remove(string characterName, string homeWorld)
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return false;
        var map = GetActiveMap();
        var key = characterName + "@" + homeWorld;
        var removed = map.Remove(key);
        if (removed)
        {
            _config.SaveClub(_activeClub!, map.Values.ToList());
        }

        return removed;
    }

    public int PurgeExpired()
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return 0;
        var map = GetActiveMap();
        var now = DateTimeOffset.UtcNow;
        var keysToRemove = new List<string>();
        foreach (var kvp in map)
        {
            var entry = kvp.Value;
            if (entry.IsExpired(now)) keysToRemove.Add(kvp.Key);
        }
        foreach (var key in keysToRemove) map.Remove(key);
        if (keysToRemove.Count > 0) _config.SaveClub(_activeClub!, map.Values.ToList());
        return keysToRemove.Count;
    }

    private static DateTimeOffset? CalculateExpiry(DateTimeOffset from, VipDuration duration)
    {
        return duration switch
        {
            VipDuration.FourWeeks => from.AddDays(28),
            VipDuration.TwelveWeeks => from.AddDays(84),
            VipDuration.Lifetime => null,
            _ => null
        };
    }

    public void ReplaceAllForActiveClub(IEnumerable<VipEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return;
        var map = GetActiveMap();
        map.Clear();
        foreach (var e in entries) map[e.Key] = e;
        _config.SaveClub(_activeClub!, map.Values.ToList());
    }

    private void EnsureClubInitialized(string clubId)
    {
        if (!_entriesByClub.TryGetValue(clubId, out var map))
        {
            var loaded = _config.LoadClub(clubId);
            map = loaded.ToDictionary(e => e.Key, e => e);
            _entriesByClub[clubId] = map;
        }
    }

    private Dictionary<string, VipEntry> GetActiveMap()
    {
        if (string.IsNullOrWhiteSpace(_activeClub)) return _empty;
        EnsureClubInitialized(_activeClub);
        return _entriesByClub[_activeClub];
    }
}
