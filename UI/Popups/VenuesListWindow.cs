using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using VenuePlus.Plugin;
using VenuePlus.Helpers;

namespace VenuePlus.UI;

public sealed class VenuesListWindow : Window, IDisposable
{
    private readonly VenuePlusApp _app;
    private readonly ITextureProvider _textureProvider;
    private bool _disposed;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDalamudTextureWrap> _logoCache = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _fetchPending = new(System.StringComparer.Ordinal);
    private IDalamudTextureWrap? _fallbackLogoTex;

    public VenuesListWindow(VenuePlusApp app, ITextureProvider textureProvider) : base("My Venues")
    {
        _app = app;
        _textureProvider = textureProvider;
        Size = new Vector2(320f, 240f);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        var canUse = _app.HasStaffSession;
        if (!canUse)
        {
            ImGui.TextUnformatted("Login to view your venues");
            return;
        }

        var labelCur = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
        ImGui.TextUnformatted($"Current Venue: {labelCur}");
        ImGui.Separator();

        var clubsOwned = _app.GetMyCreatedClubs() ?? Array.Empty<string>();
        var clubsMember = _app.GetMyClubs() ?? Array.Empty<string>();

        foreach (var c in clubsOwned)
        {
            var isActive = !string.IsNullOrWhiteSpace(_app.CurrentClubId) && string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal);
            var label = $"{c} (owned)";
            var tex = GetClubLogoTexture(c);
            if (tex != null)
            {
                var h = ImGui.GetFrameHeight();
                ImGui.Image(tex.Handle, new Vector2(h, h));
                ImGui.SameLine(0f, 6f);
            }
            if (isActive)
            {
                var baseCol = new Vector4(0.21f, 0.42f, 0.72f, 1f);
                var hoverCol = new Vector4(0.24f, 0.46f, 0.76f, 1f);
                var activeCol = new Vector4(0.18f, 0.36f, 0.66f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            }
            var clickedOwned = ImGui.Button(label);
            if (isActive) ImGui.PopStyleColor(4);
            if (clickedOwned)
            {
                _app.SetClubId(c);
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Switch to this Venue  "); ImGui.EndTooltip(); }
        }

        if (clubsOwned.Length > 0 && clubsMember.Length > 0) ImGui.Separator();

        foreach (var c in clubsMember)
        {
            var isOwned = Array.IndexOf(clubsOwned, c) >= 0;
            if (isOwned) continue;
            var isActive = !string.IsNullOrWhiteSpace(_app.CurrentClubId) && string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal);
            var label = $"{c} (member)";
            var tex = GetClubLogoTexture(c);
            if (tex != null)
            {
                var h = ImGui.GetFrameHeight();
                ImGui.Image(tex.Handle, new Vector2(h, h));
                ImGui.SameLine(0f, 6f);
            }
            if (isActive)
            {
                var baseCol = new Vector4(0.21f, 0.42f, 0.72f, 1f);
                var hoverCol = new Vector4(0.24f, 0.46f, 0.76f, 1f);
                var activeCol = new Vector4(0.18f, 0.36f, 0.66f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            }
            var clickedMember = ImGui.Button(label);
            if (isActive) ImGui.PopStyleColor(4);
            if (clickedMember)
            {
                _app.SetClubId(c);
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Switch to this Venue"); ImGui.EndTooltip(); }
        }

        if (clubsOwned.Length == 0 && clubsMember.Length == 0) ImGui.TextUnformatted("No Venues yet.");
    }

    private void EnsureFallbackLogoTexture()
    {
        if (_fallbackLogoTex != null) return;
        var base64 = VImages.GetDefaultVenueLogoBase64Raw();
        if (string.IsNullOrWhiteSpace(base64)) return;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var tex = await _textureProvider.CreateFromImageAsync(bytes);
                    _fallbackLogoTex = tex;
                }
                catch { }
            });
        }
        catch { }
    }

    private IDalamudTextureWrap? GetClubLogoTexture(string clubId)
    {
        EnsureFallbackLogoTexture();
        if (_logoCache.TryGetValue(clubId, out var cached))
        {
            try { var _ = cached.Handle; return cached; } catch { try { cached.Dispose(); } catch { } _logoCache.TryRemove(clubId, out _); }
        }
        if (_fetchPending.ContainsKey(clubId))
        {
            if (_fallbackLogoTex != null)
            {
                try { var _ = _fallbackLogoTex.Handle; return _fallbackLogoTex; } catch { try { _fallbackLogoTex?.Dispose(); } catch { } _fallbackLogoTex = null; }
            }
            return null;
        }
        if (!_fetchPending.TryAdd(clubId, 1)) return null;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var base64 = _app.GetClubLogoForClub(clubId) ?? await _app.FetchClubLogoForClubAsync(clubId);
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    var bytes = Convert.FromBase64String(base64!);
                    try
                    {
                        var tex = await _textureProvider.CreateFromImageAsync(bytes);
                        if (_logoCache.TryGetValue(clubId, out var old)) { try { old.Dispose(); } catch { } }
                        _logoCache[clubId] = tex;
                    }
                    catch { }
                }
                else
                {
                    if (_logoCache.TryGetValue(clubId, out var old)) { try { old.Dispose(); } catch { } _logoCache.TryRemove(clubId, out _); }
                }
            }
            finally
            {
                _fetchPending.TryRemove(clubId, out _);
            }
        });
        if (_fallbackLogoTex != null)
        {
            try { var _ = _fallbackLogoTex.Handle; return _fallbackLogoTex; } catch { try { _fallbackLogoTex?.Dispose(); } catch { } _fallbackLogoTex = null; }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kv in _logoCache)
        {
            try { kv.Value.Dispose(); } catch { }
        }
        _logoCache.Clear();
        try { _fallbackLogoTex?.Dispose(); } catch { }
        _fallbackLogoTex = null;
    }
}
