namespace VenuePlus.Configuration;

public sealed class PluginConfig
{
    public string? RemoteClubId { get; set; }
    public bool RemoteUseWebSocket { get; set; }
    public bool AutoLoginEnabled { get; set; }
    public bool RememberStaffLogin { get; set; }
    public string? SavedStaffUsername { get; set; }
    public string? SavedStaffPasswordEnc { get; set; }
    public string? SecretsKey { get; set; }
    public bool ShowVipOverlay { get; set; } = false;
    public bool ShowVipNameplateHook { get; set; } = true;
    public ushort VipStarColorKey { get; set; } = 43;
    public string? LastInstalledVersion { get; set; }
    public System.Collections.Generic.Dictionary<string, CharacterProfile> ProfilesByCharacter { get; set; } = new(System.StringComparer.Ordinal);
    public bool KeepWhisperMessage { get; set; } = false;
    public System.Collections.Generic.List<WhisperPreset> WhisperPresets { get; set; } = new();
}

public sealed class WhisperPreset
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
