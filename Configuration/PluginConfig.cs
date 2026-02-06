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
    public bool ShowVipNameplateHook { get; set; } = true;
    public ushort VipStarColorKey { get; set; } = 43;
    public string VipStarChar { get; set; } = "★";
    public VipStarPosition VipStarPosition { get; set; } = VipStarPosition.Left;
    public bool VipTextEnabled { get; set; } = false;
    public string VipLabelText { get; set; } = "[VIP]";
    public VipLabelOrder VipLabelOrder { get; set; } = VipLabelOrder.SymbolThenText;
    public string? LastInstalledVersion { get; set; }
    public System.Collections.Generic.Dictionary<string, CharacterProfile> ProfilesByCharacter { get; set; } = new(System.StringComparer.Ordinal);
    public bool KeepWhisperMessage { get; set; } = false;
    public System.Collections.Generic.List<WhisperPreset> WhisperPresets { get; set; } = new();
    public bool ShowShiftTimesInLocalTime { get; set; } = false;
    public NotificationPreferences Notifications { get; set; } = new NotificationPreferences();
    public System.Collections.Generic.List<MacroHotbar> MacroHotbars { get; set; } = new System.Collections.Generic.List<MacroHotbar>();
    public int CurrentMacroHotbarIndex { get; set; } = 0;
    public System.Collections.Generic.List<MacroHotbarSlot> MacroHotbarSlots { get; set; } = new System.Collections.Generic.List<MacroHotbarSlot>();
    public bool MacroHotbarLocked { get; set; } = false;
    public System.Collections.Generic.List<int> OpenMacroHotbarIndices { get; set; } = new System.Collections.Generic.List<int>();
}

public sealed class WhisperPreset
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class NotificationPreferences
{
    public NotificationDisplayMode DisplayMode { get; set; } = NotificationDisplayMode.Both;
    public bool ShowLoginSuccess { get; set; } = true;
    public bool ShowLoginFailed { get; set; } = true;
    public bool ShowPasswordRequired { get; set; } = true;
    public bool ShowRoleChangedSelf { get; set; } = true;
    public bool ShowOwnershipGranted { get; set; } = true;
    public bool ShowOwnershipTransferred { get; set; } = true;
    public bool ShowMembershipJoined { get; set; } = true;
    public bool ShowMembershipRemoved { get; set; } = true;
    public bool ShowVipAdded { get; set; } = true;
    public bool ShowVipRemoved { get; set; } = false;
    public bool ShowDjAdded { get; set; } = true;
    public bool ShowDjRemoved { get; set; } = false;
    public bool ShowShiftCreated { get; set; } = true;
    public bool ShowShiftUpdated { get; set; } = true;
    public bool ShowShiftRemoved { get; set; } = true;
}

public enum NotificationDisplayMode
{
    None = 0,
    Chat = 1,
    Toast = 2,
    Both = 3
}

public enum VipStarPosition
{
    Left = 0,
    Right = 1,
    Both = 2
}

public enum VipLabelOrder
{
    SymbolThenText = 0,
    TextThenSymbol = 1
}
