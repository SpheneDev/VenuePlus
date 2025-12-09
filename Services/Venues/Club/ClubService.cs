using System;
using System.Collections.Generic;

namespace VenuePlus.Services;

internal sealed class ClubService
{
    private readonly Dictionary<string, string?> _clubLogosByClub = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _clubOwnersByClub = new(StringComparer.Ordinal);
    private string? _currentClubLogoBase64;

    public string? CurrentLogoBase64 => _currentClubLogoBase64;

    public void SetCurrentClubLogo(string? currentClubId, string? logo)
    {
        _currentClubLogoBase64 = logo;
        if (!string.IsNullOrWhiteSpace(currentClubId)) _clubLogosByClub[currentClubId!] = logo;
    }

    public void SetClubLogoForClub(string clubId, string? logo)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return;
        _clubLogosByClub[clubId] = logo;
    }

    public string? GetLogoForClub(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return null;
        return _clubLogosByClub.TryGetValue(clubId, out var v) ? v : null;
    }

    public void RemoveClub(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return;
        _clubLogosByClub.Remove(clubId);
        _clubOwnersByClub.Remove(clubId);
    }

    public void Clear()
    {
        _clubLogosByClub.Clear();
        _clubOwnersByClub.Clear();
        _currentClubLogoBase64 = null;
    }

    public void SetOwnerForClub(string clubId, string? ownerUsername)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return;
        _clubOwnersByClub[clubId] = ownerUsername;
    }

    public string? GetOwnerForClub(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return null;
        return _clubOwnersByClub.TryGetValue(clubId, out var v) ? v : null;
    }
}
