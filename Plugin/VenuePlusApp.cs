using System;
using System.Reflection;
using VenuePlus.Services;
using System.Linq;
using VenuePlus.State;
using VenuePlus.Helpers;
using VenuePlus.Configuration;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace VenuePlus.Plugin;

public sealed class VenuePlusApp : IDisposable, IEventListener
{
    private readonly VipService _vipService;
    private readonly AutoPurgeService _autoPurge;
    private readonly PluginConfigService _pluginConfigService;
    private readonly RemoteSyncService _remote;
    private readonly AccessService _accessService;
    private readonly ClubService _clubService = new();
    private readonly EventService _eventService;
    private readonly IPluginLog? _log;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private string _currentCharName = string.Empty;
    private string _currentCharWorld = string.Empty;
    private bool _wasLoggedIn;
    private bool _isPowerStaff;
    private string? _staffToken;
    private string? _staffUsername;
    private System.Collections.Generic.Dictionary<string, bool> _selfRights = new();
    private System.DateTimeOffset _selfRightsLastFetch;
    private string _selfJob = string.Empty;
    private string? _selfUid;
    private bool _disposed;
    private VenuePlus.Services.NotificationService? _notifier;
    private static readonly string RemoteBaseUrlConst = GetRemoteBaseUrl();
    private string[]? _jobsCache;
    private System.Collections.Generic.Dictionary<string, JobRightsInfo>? _jobRightsCache;
    private string[]? _usersCache;
    private VenuePlus.State.StaffUser[]? _usersDetailsCache;
    private string _jobsFingerprint = string.Empty;
    private string _usersDetailsFingerprint = string.Empty;
    private string[]? _myClubs;
    private string[]? _myCreatedClubs;
    private bool _accessLoading;
    private bool _autoLoginAttempted;
    private readonly DjService _djService = new();
    private readonly ShiftService _shiftService = new();
    private readonly System.Threading.SemaphoreSlim _connectGate = new(1, 1);
    private readonly System.Threading.SemaphoreSlim _autoLoginGate = new(1, 1);
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private System.Threading.CancellationTokenSource? _autoLoginMonitorCts;
    private System.Threading.Tasks.Task? _autoLoginMonitorTask;
    private bool _isDisposing;
    private string? _desiredClubId;
    private bool _clubListsLoaded;
    public event Action<VenuePlus.State.StaffUser[]>? UsersDetailsChanged;
    public event Action<string, string>? UserJobUpdatedEvt;
    public event Action<string[]>? JobsChanged;
    public event Action<System.Collections.Generic.Dictionary<string, JobRightsInfo>?>? JobRightsChanged;
    public event Action<bool, bool>? AutoLoginResultEvt;
    public event Action? RememberStaffNeedsPasswordEvt;
    public event Action? OpenSettingsRequested;
    public event Action? OpenVipListRequested;
    public event Action<string?>? ClubLogoChanged;
    
    public event Action? OpenVenuesListRequested;
    public event Action? OpenChangelogRequested;
    public event Action? OpenQolToolsRequested;
    public event Action? OpenWhisperRequested;
    public event Action? OpenMacroHotbarRequested;

    public VenuePlusApp(string? vipDataPath = null, string? pluginConfigPath = null, IPluginLog? log = null, IClientState? clientState = null, IObjectTable? objectTable = null)
    {
        _log = log;
        _clientState = clientState!;
        _objectTable = objectTable!;
        var config = new ConfigurationService(vipDataPath);
        _vipService = new VipService(config);
        _autoPurge = new AutoPurgeService(_vipService);
        _pluginConfigService = new PluginConfigService(pluginConfigPath ?? string.Empty);
        _accessService = new AccessService(_pluginConfigService);
        _remote = new RemoteSyncService(_log);
        _accessService.SetRemote(_remote);
        _eventService = new EventService(_remote);
        _eventService.Register(this);
        
        
        if (!_pluginConfigService.Current.RemoteUseWebSocket)
        {
            _pluginConfigService.Current.RemoteUseWebSocket = true;
            _pluginConfigService.Save();
        }
        _remote.SetClubId(null);

        _autoLoginMonitorCts = new System.Threading.CancellationTokenSource();
        var ctMon = _autoLoginMonitorCts.Token;
        _autoLoginMonitorTask = System.Threading.Tasks.Task.Run(async () =>
        {
            while (!ctMon.IsCancellationRequested)
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(5000, ctMon);
                    if (ctMon.IsCancellationRequested) break;
                    if (AutoLoginEnabled && RemoteConnected && !HasStaffSession)
                    {
                        try { _log?.Debug($"[AutoLogin] monitor tick: enabled={AutoLoginEnabled} connected={RemoteConnected} hasSession={HasStaffSession} attempted={_autoLoginAttempted}"); } catch { }
                        await TryAutoLoginAsync();
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException) { break; }
                catch { }
            }
        }, ctMon);
    }
    public void OpenSettingsWindow()
    {
        try { OpenSettingsRequested?.Invoke(); } catch { }
    }

    public void OpenVipListWindow()
    {
        try { OpenVipListRequested?.Invoke(); } catch { }
    }

    public void OpenVenuesListWindow()
    {
        try { OpenVenuesListRequested?.Invoke(); } catch { }
    }

    public void OpenChangelogWindow()
    {
        try { OpenChangelogRequested?.Invoke(); } catch { }
    }

    public void OpenWhisperWindow()
    {
        try { OpenWhisperRequested?.Invoke(); } catch { }
    }

    public void OpenQolToolsWindow()
    {
        try { OpenQolToolsRequested?.Invoke(); } catch { }
    }

    public event System.Action? OpenMacroHotbarManagerRequested;
    public void OpenMacroHotbarManagerWindow()
    {
        try { OpenMacroHotbarManagerRequested?.Invoke(); } catch { }
    }

    public void OpenMacroHotbarWindow()
    {
        try { OpenMacroHotbarRequested?.Invoke(); } catch { }
    }

    public event System.Action<int>? OpenMacroHotbarIndexRequested;
    public event System.Action<int>? CloseMacroHotbarIndexRequested;
    public event System.Func<int, Vector2?>? QueryMacroHotbarPositionRequested;
    public event System.Action<int, Vector2>? SetMacroHotbarPositionRequested;
    public event System.Action<int>? ResetMacroHotbarPositionRequested;

    public void OpenMacroHotbarWindowAt(int index)
    {
        try { OpenMacroHotbarIndexRequested?.Invoke(index); } catch { }
    }

    public void CloseMacroHotbarWindowAt(int index)
    {
        try { CloseMacroHotbarIndexRequested?.Invoke(index); } catch { }
    }

    public Vector2? GetMacroHotbarWindowPositionAt(int index)
    {
        try { return QueryMacroHotbarPositionRequested?.Invoke(index); } catch { }
        return null;
    }

    public void SetMacroHotbarPositionAt(int index, Vector2 position)
    {
        try { SetMacroHotbarPositionRequested?.Invoke(index, position); } catch { }
    }

    public void ResetMacroHotbarPositionAt(int index)
    {
        try { ResetMacroHotbarPositionRequested?.Invoke(index); } catch { }
    }

    public string? GetLastInstalledVersion()
    {
        return _pluginConfigService.Current.LastInstalledVersion;
    }

    public void SetLastInstalledVersion(string version)
    {
        _pluginConfigService.Current.LastInstalledVersion = version;
        _pluginConfigService.Save();
    }

    private string? _requestedSettingsTab;
    public string? ConsumeRequestedSettingsTab()
    {
        var v = _requestedSettingsTab;
        _requestedSettingsTab = null;
        return v;
    }

    public void OpenSettingsWindowAccount()
    {
        _requestedSettingsTab = "Account";
        try { OpenSettingsRequested?.Invoke(); } catch { }
    }

    public VipEntry AddVip(string characterName, string homeWorld, VipDuration duration)
    {
        var exists = _vipService.GetAll(true).Any(e => string.Equals(e.CharacterName, characterName, System.StringComparison.Ordinal)
                                                     && string.Equals(e.HomeWorld, homeWorld, System.StringComparison.Ordinal));
        var canOwner = IsOwnerCurrentClub;
        var canEdit = HasStaffSession && StaffCanEditVipDuration;
        var canAdd = HasStaffSession && StaffCanAddVip;
        if (exists)
        {
            if (!(canOwner || canEdit)) return _vipService.GetAll(true).First(e => string.Equals(e.CharacterName, characterName, System.StringComparison.Ordinal)
                                                                               && string.Equals(e.HomeWorld, homeWorld, System.StringComparison.Ordinal));
        }
        else
        {
            if (!(canOwner || canAdd)) return new VipEntry { CharacterName = characterName, HomeWorld = homeWorld, Duration = duration };
        }
        var entry = _vipService.AddOrUpdate(characterName, homeWorld, duration);
        TryPublishAdd(entry);
        return entry;
    }

    public bool RemoveVip(string characterName, string homeWorld)
    {
        var ok = _vipService.Remove(characterName, homeWorld);
        if (ok)
        {
            TryPublishRemove(new VipEntry { CharacterName = characterName, HomeWorld = homeWorld, Duration = VipDuration.FourWeeks, CreatedAt = System.DateTimeOffset.UtcNow });
        }
        return ok;
    }

    public void PurgeExpired()
    {
        _vipService.PurgeExpired();
    }

    public System.Collections.Generic.IReadOnlyCollection<VipEntry> GetActive()
    {
        return _vipService.GetAll();
    }

    public System.Collections.Generic.IReadOnlyCollection<VenuePlus.State.DjEntry> GetDjEntries()
    {
        return _djService.GetAll();
    }

    public VenuePlus.State.ShiftEntry[] GetShiftEntries()
    {
        return _shiftService.GetAll();
    }

    public async System.Threading.Tasks.Task<bool> AddDjAsync(string djName, string twitchLink)
    {
        var canAddDj = IsOwnerCurrentClub || (HasStaffSession && StaffCanAddDj);
        if (!canAddDj) return false;
        var link = DjService.NormalizeTwitchLink(twitchLink);
        var entry = new VenuePlus.State.DjEntry { DjName = djName ?? string.Empty, TwitchLink = link, CreatedAt = System.DateTimeOffset.UtcNow };
        _djService.SetOrAdd(entry);
        if (HasStaffSession)
        {
            try { return await _remote.PublishAddDjAsync(entry, _staffToken!); } catch { return false; }
        }
        return true;
    }

    

    public async System.Threading.Tasks.Task<bool> RemoveDjAsync(string djName)
    {
        var canRemoveDj = IsOwnerCurrentClub || (HasStaffSession && StaffCanRemoveDj);
        if (!canRemoveDj) return false;
        _djService.RemoveByName(djName ?? string.Empty);
        if (HasStaffSession)
        {
            var entry = new VenuePlus.State.DjEntry { DjName = djName ?? string.Empty, TwitchLink = string.Empty, CreatedAt = System.DateTimeOffset.UtcNow };
            try { var ok = await _remote.PublishRemoveDjAsync(entry, _staffToken!); if (ok) { var _np = GetNotificationPreferences(); if (_np.ShowDjRemoved) { try { _notifier?.ShowInfo("DJ removed: " + (djName ?? string.Empty)); } catch { } } } return ok; } catch { return false; }
        }
        return true;
    }

    public async System.Threading.Tasks.Task<bool> AddShiftAsync(string title, System.DateTimeOffset startAt, System.DateTimeOffset endAt, string? assignedUid = null, string? job = null)
    {
        var canEdit = CanEditShiftPlanInternal();
        if (!canEdit) return false;
        var entry = new VenuePlus.State.ShiftEntry { Id = Guid.Empty, Title = title ?? string.Empty, AssignedUid = string.IsNullOrWhiteSpace(assignedUid) ? null : assignedUid, Job = string.IsNullOrWhiteSpace(job) ? null : job, StartAt = startAt, EndAt = endAt };
        if (HasStaffSession)
        {
            try { var ok = await _remote.PublishAddShiftAsync(entry, _staffToken!); if (ok) { TryNotifyShiftCreated(entry); } return ok; } catch { return false; }
        }
        return false;
    }

    public async System.Threading.Tasks.Task<bool> UpdateShiftAsync(Guid id, string title, System.DateTimeOffset startAt, System.DateTimeOffset endAt, string? assignedUid = null, string? job = null)
    {
        var canEdit = CanEditShiftPlanInternal();
        if (!canEdit) return false;
        var entry = new VenuePlus.State.ShiftEntry { Id = id, Title = title ?? string.Empty, AssignedUid = string.IsNullOrWhiteSpace(assignedUid) ? null : assignedUid, Job = string.IsNullOrWhiteSpace(job) ? null : job, StartAt = startAt, EndAt = endAt };
        if (HasStaffSession)
        {
            try { var ok = await _remote.PublishUpdateShiftAsync(entry, _staffToken!); if (ok) { TryNotifyShiftUpdated(entry); } return ok; } catch { return false; }
        }
        return false;
    }

    public async System.Threading.Tasks.Task<bool> RemoveShiftAsync(Guid id)
    {
        var canEdit = CanEditShiftPlanInternal();
        if (!canEdit) return false;
        if (HasStaffSession)
        {
            try { var ok = await _remote.PublishRemoveShiftAsync(id, _staffToken!); if (ok) { TryNotifyShiftRemoved(); } return ok; } catch { return false; }
        }
        return false;
    }

    public bool IsVip(string characterName, string homeWorld)
    {
        return _vipService.ExistsActive(characterName, homeWorld);
    }

    public bool RemoteConnected => _remote.IsConnected;

    
    public bool IsPowerStaff => _isPowerStaff;
    public bool HasStaffSession => _isPowerStaff && !string.IsNullOrWhiteSpace(_staffToken);
    public string CurrentStaffUsername => _staffUsername ?? string.Empty;
    public bool StaffCanAddVip => _selfRights.TryGetValue("addVip", out var b) && b;
    public bool StaffCanRemoveVip => _selfRights.TryGetValue("removeVip", out var b) && b;
    public bool StaffCanManageUsers => _selfRights.TryGetValue("manageUsers", out var b) && b;
    public bool StaffCanManageJobs => _selfRights.TryGetValue("manageJobs", out var b) && b;
    public bool StaffCanEditVipDuration => _selfRights.TryGetValue("editVipDuration", out var b) && b;
    public bool StaffCanAddDj => _selfRights.TryGetValue("addDj", out var b) && b;
    public bool StaffCanRemoveDj => _selfRights.TryGetValue("removeDj", out var b) && b;
    public bool StaffCanEditShiftPlan => _selfRights.TryGetValue("editShiftPlan", out var b) && b;
    public string CurrentStaffJob => _selfJob;
    public bool AccessLoading => _accessLoading;
    public bool RememberStaffLogin
    {
        get
        {
            var key = GetCurrentCharacterKey();
            if (!string.IsNullOrWhiteSpace(key) && _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out var p))
                return p.RememberStaffLogin;
            return _pluginConfigService.Current.RememberStaffLogin;
        }
    }
    public bool AutoLoginEnabled
    {
        get
        {
            var key = GetCurrentCharacterKey();
            if (!string.IsNullOrWhiteSpace(key) && _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out var p))
                return p.AutoLoginEnabled;
            return _pluginConfigService.Current.AutoLoginEnabled;
        }
    }

    public bool ShowVipNameplateHook => _pluginConfigService.Current.ShowVipNameplateHook;
    public ushort VipStarColorKey => _pluginConfigService.Current.VipStarColorKey;
    public string VipStarChar => _pluginConfigService.Current.VipStarChar ?? "★";
    public VenuePlus.Configuration.VipStarPosition VipStarPosition => _pluginConfigService.Current.VipStarPosition;
    public bool VipTextEnabled => _pluginConfigService.Current.VipTextEnabled;
    public string VipLabelText => _pluginConfigService.Current.VipLabelText ?? string.Empty;
    public VenuePlus.Configuration.VipLabelOrder VipLabelOrder => _pluginConfigService.Current.VipLabelOrder;
    public bool KeepWhisperMessage => _pluginConfigService.Current.KeepWhisperMessage;
    public VenuePlus.Configuration.WhisperPreset[] GetWhisperPresets() => _pluginConfigService.Current.WhisperPresets?.ToArray() ?? System.Array.Empty<VenuePlus.Configuration.WhisperPreset>();

    private void EnsureMacroHotbars()
    {
        var cfg = _pluginConfigService.Current;
        if (cfg.MacroHotbars == null) cfg.MacroHotbars = new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbar>();
        if (cfg.CurrentMacroHotbarIndex < 0 || (cfg.MacroHotbars.Count > 0 && cfg.CurrentMacroHotbarIndex >= cfg.MacroHotbars.Count))
            cfg.CurrentMacroHotbarIndex = 0;
    }

    public VenuePlus.Configuration.MacroHotbarSlot[] GetMacroHotbarSlots()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (cfg.MacroHotbars.Count == 0) return System.Array.Empty<VenuePlus.Configuration.MacroHotbarSlot>();
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        return bar.Slots?.ToArray() ?? System.Array.Empty<VenuePlus.Configuration.MacroHotbarSlot>();
    }

    public VenuePlus.Configuration.MacroHotbarSlot[] GetMacroHotbarSlotsFor(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Array.Empty<VenuePlus.Configuration.MacroHotbarSlot>();
        var bar = cfg.MacroHotbars[barIndex];
        return bar.Slots?.ToArray() ?? System.Array.Empty<VenuePlus.Configuration.MacroHotbarSlot>();
    }

    public System.Threading.Tasks.Task SetMacroHotbarAssignmentAsync(int slot, string? presetName)
    {
        return SetMacroHotbarSlotAsync(slot, presetName, null);
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotAsync(int slot, string? presetName, VenuePlus.Configuration.ChatChannel? channel, string? iconKey = null, bool? useGameIcon = null, int? gameIconId = null)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        try { _log?.Debug($"SetMacroHotbarSlot: bar={cfg.CurrentMacroHotbarIndex} slot={slot} beforeCount={list.Count} preset={(presetName ?? "<null>")} channel={(channel.HasValue ? channel.Value.ToString() : "<nochange>")} iconKey={(iconKey ?? "<null>")} useGameIcon={(useGameIcon.HasValue ? useGameIcon.Value.ToString() : "<nochange>")} gameIconId={(gameIconId.HasValue ? gameIconId.Value.ToString() : "<nochange>")}"); } catch { }
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        if (presetName != null) item.PresetName = presetName.Trim();
        if (channel.HasValue) item.Channel = channel.Value;
        if (iconKey != null) item.IconKey = iconKey.Trim();
        if (useGameIcon.HasValue) item.UseGameIcon = useGameIcon.Value;
        if (gameIconId.HasValue) item.GameIconId = System.Math.Max(0, gameIconId.Value);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        try { _log?.Debug($"SetMacroHotbarSlot: bar={cfg.CurrentMacroHotbarIndex} slot={slot} afterCount={list.Count} preset={(item.PresetName ?? "" )} channel={item.Channel} iconKey={(item.IconKey ?? "" )} useGameIcon={item.UseGameIcon} gameIconId={item.GameIconId}"); } catch { }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotAsyncAtBar(int barIndex, int slot, string? presetName, VenuePlus.Configuration.ChatChannel? channel, string? iconKey = null, bool? useGameIcon = null, int? gameIconId = null)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        try { _log?.Debug($"SetMacroHotbarSlotAt: bar={barIndex} slot={slot} beforeCount={list.Count} preset={(presetName ?? "<null>")} channel={(channel.HasValue ? channel.Value.ToString() : "<nochange>")} iconKey={(iconKey ?? "<null>")} useGameIcon={(useGameIcon.HasValue ? useGameIcon.Value.ToString() : "<nochange>")} gameIconId={(gameIconId.HasValue ? gameIconId.Value.ToString() : "<nochange>")}"); } catch { }
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        if (presetName != null) item.PresetName = presetName.Trim();
        if (channel.HasValue) item.Channel = channel.Value;
        if (iconKey != null) item.IconKey = iconKey.Trim();
        if (useGameIcon.HasValue) item.UseGameIcon = useGameIcon.Value;
        if (gameIconId.HasValue) item.GameIconId = System.Math.Max(0, gameIconId.Value);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        try { _log?.Debug($"SetMacroHotbarSlotAt: bar={barIndex} slot={slot} afterCount={list.Count} preset={(item.PresetName ?? "" )} channel={item.Channel} iconKey={(item.IconKey ?? "" )} useGameIcon={item.UseGameIcon} gameIconId={item.GameIconId}"); } catch { }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotNoBackgroundAsync(int slot, bool noBackground)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.NoBackground = noBackground;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotNoBackgroundAtAsync(int barIndex, int slot, bool noBackground)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.NoBackground = noBackground;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotTooltipAsync(int slot, string? text)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.ToolTipText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotTooltipAtAsync(int barIndex, int slot, string? text)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.ToolTipText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconScaleAsync(int slot, float scale)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconScale = System.Math.Clamp(scale, 0.3f, 2.0f);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconScaleAtAsync(int barIndex, int slot, float scale)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconScale = System.Math.Clamp(scale, 0.3f, 2.0f);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconOffsetAsync(int slot, float offsetX, float offsetY)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconOffsetX = offsetX;
        item.IconOffsetY = offsetY;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconOffsetAtAsync(int barIndex, int slot, float offsetX, float offsetY)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconOffsetX = offsetX;
        item.IconOffsetY = offsetY;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconZoomOffsetAsync(int slot, float zoomX, float zoomY)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconZoomOffsetX = System.Math.Clamp(zoomX, -1.0f, 1.0f);
        item.IconZoomOffsetY = System.Math.Clamp(zoomY, -1.0f, 1.0f);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconZoomOffsetAtAsync(int barIndex, int slot, float zoomX, float zoomY)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconZoomOffsetX = System.Math.Clamp(zoomX, -1.0f, 1.0f);
        item.IconZoomOffsetY = System.Math.Clamp(zoomY, -1.0f, 1.0f);
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotShowFrameAsync(int slot, bool show)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.ShowFrame = show;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotShowFrameAtAsync(int barIndex, int slot, bool show)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.ShowFrame = show;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotFrameColorAsync(int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.FrameColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotFrameColorAtAsync(int barIndex, int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.FrameColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconColorAsync(int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotIconColorAtAsync(int barIndex, int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.IconColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotHoverBackgroundColorAsync(int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.HoverBackgroundColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarSlotHoverBackgroundColorAtAsync(int barIndex, int slot, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        while (list.Count <= slot) list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        var item = list[slot] ?? new VenuePlus.Configuration.MacroHotbarSlot();
        item.HoverBackgroundColor = color;
        list[slot] = item;
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public uint? GetMacroHotbarFrameColorDefault()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].FrameColorDefault;
    }

    public uint? GetMacroHotbarFrameColorDefaultAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return null;
        return cfg.MacroHotbars[barIndex].FrameColorDefault;
    }

    public System.Threading.Tasks.Task SetMacroHotbarFrameColorDefaultAsync(uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].FrameColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarFrameColorDefaultAtAsync(int barIndex, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].FrameColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public uint? GetMacroHotbarIconColorDefault()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].IconColorDefault;
    }

    public uint? GetMacroHotbarIconColorDefaultAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return null;
        return cfg.MacroHotbars[barIndex].IconColorDefault;
    }

    public System.Threading.Tasks.Task SetMacroHotbarIconColorDefaultAsync(uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].IconColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarIconColorDefaultAtAsync(int barIndex, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].IconColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public uint? GetMacroHotbarHoverBackgroundColorDefault()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].HoverBackgroundColorDefault;
    }

    public uint? GetMacroHotbarHoverBackgroundColorDefaultAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return null;
        return cfg.MacroHotbars[barIndex].HoverBackgroundColorDefault;
    }

    public System.Threading.Tasks.Task SetMacroHotbarHoverBackgroundColorDefaultAsync(uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].HoverBackgroundColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarHoverBackgroundColorDefaultAtAsync(int barIndex, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].HoverBackgroundColorDefault = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public uint? GetMacroHotbarBackgroundColor()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].BackgroundColor;
    }

    public uint? GetMacroHotbarBackgroundColorAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return null;
        return cfg.MacroHotbars[barIndex].BackgroundColor;
    }

    public System.Threading.Tasks.Task SetMacroHotbarBackgroundColorAsync(uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].BackgroundColor = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarBackgroundColorAtAsync(int barIndex, uint? color)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].BackgroundColor = color;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public bool GetMacroHotbarShowFrameDefault()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ShowFrameDefault;
    }

    public bool GetMacroHotbarShowFrameDefaultAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return false;
        return cfg.MacroHotbars[barIndex].ShowFrameDefault;
    }

    public System.Threading.Tasks.Task SetMacroHotbarShowFrameDefaultAsync(bool show)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ShowFrameDefault = show;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarShowFrameDefaultAtAsync(int barIndex, bool show)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].ShowFrameDefault = show;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public float GetMacroHotbarIconScaleDefault()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].IconScaleDefault;
    }

    public float GetMacroHotbarIconScaleDefaultAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return 1.0f;
        return cfg.MacroHotbars[barIndex].IconScaleDefault;
    }

    public System.Threading.Tasks.Task SetMacroHotbarIconScaleDefaultAsync(float scale)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].IconScaleDefault = System.Math.Clamp(scale, 0.3f, 2.0f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarIconScaleDefaultAtAsync(int barIndex, float scale)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].IconScaleDefault = System.Math.Clamp(scale, 0.3f, 2.0f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public float GetMacroHotbarItemSpacingX()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ItemSpacingX;
    }

    public float GetMacroHotbarItemSpacingXAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return 6f;
        return cfg.MacroHotbars[barIndex].ItemSpacingX;
    }

    public System.Threading.Tasks.Task SetMacroHotbarItemSpacingXAsync(float spacing)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ItemSpacingX = System.Math.Clamp(spacing, 0f, 24f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarItemSpacingXAtAsync(int barIndex, float spacing)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].ItemSpacingX = System.Math.Clamp(spacing, 0f, 24f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public float GetMacroHotbarItemSpacingY()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ItemSpacingY;
    }

    public float GetMacroHotbarItemSpacingYAt(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return 6f;
        return cfg.MacroHotbars[barIndex].ItemSpacingY;
    }

    public System.Threading.Tasks.Task SetMacroHotbarItemSpacingYAsync(float spacing)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ItemSpacingY = System.Math.Clamp(spacing, 0f, 24f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetMacroHotbarItemSpacingYAtAsync(int barIndex, float spacing)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[barIndex].ItemSpacingY = System.Math.Clamp(spacing, 0f, 24f);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task ApplyMacroHotbarDefaultsToAllSlotsAtAsync(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        for (int s = 0; s < list.Count; s++)
        {
            var item = list[s] ?? new VenuePlus.Configuration.MacroHotbarSlot();
            item.IconScale = bar.IconScaleDefault;
            item.ShowFrame = bar.ShowFrameDefault;
            if (bar.FrameColorDefault.HasValue) item.FrameColor = bar.FrameColorDefault.Value; else item.FrameColor = null;
            if (bar.IconColorDefault.HasValue) item.IconColor = bar.IconColorDefault.Value; else item.IconColor = null;
            if (bar.HoverBackgroundColorDefault.HasValue) item.HoverBackgroundColor = bar.HoverBackgroundColorDefault.Value; else item.HoverBackgroundColor = null;
            list[s] = item;
        }
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task AddMacroHotbarSlotAsync()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task AddMacroHotbarSlotAtAsync(int barIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        list.Add(new VenuePlus.Configuration.MacroHotbarSlot());
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task RemoveLastMacroHotbarSlotAsync()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        if (list.Count > 1) list.RemoveAt(list.Count - 1);
        bar.Slots = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task RemoveMacroHotbarSlotAtAsync(int barIndex, int slotIndex)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        try { _log?.Debug($"RemoveSlotAt: bar={barIndex} slot={slotIndex} count={list.Count}"); } catch { }
        if (slotIndex >= 0 && slotIndex < list.Count && list.Count > 1)
        {
            list.RemoveAt(slotIndex);
            bar.Slots = list;
            _pluginConfigService.Save();
            try { _log?.Debug($"RemoveSlotAt: bar={barIndex} done newCount={list.Count}"); } catch { }
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SwapMacroHotbarSlotsAsync(int a, int b)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        if (a >= 0 && a < list.Count && b >= 0 && b < list.Count && a != b)
        {
            var tmp = list[a];
            list[a] = list[b];
            list[b] = tmp;
            bar.Slots = list;
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SwapMacroHotbarSlotsAtAsync(int barIndex, int a, int b)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (barIndex < 0 || barIndex >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var bar = cfg.MacroHotbars[barIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        if (a >= 0 && a < list.Count && b >= 0 && b < list.Count && a != b)
        {
            var tmp = list[a];
            list[a] = list[b];
            list[b] = tmp;
            bar.Slots = list;
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public int GetMacroHotbarCount()
    {
        EnsureMacroHotbars();
        return _pluginConfigService.Current.MacroHotbars.Count;
    }

    public int GetMacroHotbarCountUnsafe()
    {
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars?.Count ?? 0;
    }

    public int GetCurrentMacroHotbarIndex()
    {
        EnsureMacroHotbars();
        return _pluginConfigService.Current.CurrentMacroHotbarIndex;
    }

    public string GetMacroHotbarName(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return (index >= 0 && index < cfg.MacroHotbars.Count) ? (cfg.MacroHotbars[index].Name ?? "Bar") : "Bar";
    }

    public string GetMacroHotbarId(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return string.Empty;
        var id = cfg.MacroHotbars[index].Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = System.Guid.NewGuid().ToString("N");
            cfg.MacroHotbars[index].Id = id;
            _pluginConfigService.Save();
        }
        return id;
    }

    public System.Threading.Tasks.Task SetCurrentMacroHotbarIndexAsync(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index >= 0 && index < cfg.MacroHotbars.Count)
        {
            cfg.CurrentMacroHotbarIndex = index;
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task AddMacroHotbarAsync(string? name = null)
    {
        var cfg = _pluginConfigService.Current;
        if (cfg.MacroHotbars == null) cfg.MacroHotbars = new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbar>();
        var nextIndex = cfg.MacroHotbars.Count + 1;
        var bar = new VenuePlus.Configuration.MacroHotbar { Name = string.IsNullOrWhiteSpace(name) ? ($"Bar {nextIndex}") : name!.Trim() };
        cfg.MacroHotbars.Add(bar);
        var newIndex = cfg.MacroHotbars.Count - 1;
        var list = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
        list.RemoveAll(i => i == newIndex);
        list.Add(newIndex);
        cfg.OpenMacroHotbarIndices = list;
        cfg.CurrentMacroHotbarIndex = newIndex;
        _pluginConfigService.Save();
        try { OpenMacroHotbarIndexRequested?.Invoke(newIndex); } catch { }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task RemoveMacroHotbarAtAsync(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index >= 0 && index < cfg.MacroHotbars.Count)
        {
            var openBefore = (cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>()).ToArray();
            try { CloseMacroHotbarIndexRequested?.Invoke(index); } catch { }
            cfg.MacroHotbars.RemoveAt(index);
            var open = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
            var updated = new System.Collections.Generic.List<int>(open.Count);
            for (int i = 0; i < open.Count; i++)
            {
                var v = open[i];
                if (v == index) continue;
                if (v > index) v--;
                updated.Add(v);
            }
            cfg.OpenMacroHotbarIndices = updated;
            for (int i = 0; i < openBefore.Length; i++)
            {
                var v = openBefore[i];
                if (v > index)
                {
                    try { CloseMacroHotbarIndexRequested?.Invoke(v); } catch { }
                    try { OpenMacroHotbarIndexRequested?.Invoke(v - 1); } catch { }
                }
            }
            if (cfg.CurrentMacroHotbarIndex >= cfg.MacroHotbars.Count) cfg.CurrentMacroHotbarIndex = System.Math.Max(0, cfg.MacroHotbars.Count - 1);
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task RenameMacroHotbarAsync(int index, string name)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index >= 0 && index < cfg.MacroHotbars.Count)
        {
            cfg.MacroHotbars[index].Name = string.IsNullOrWhiteSpace(name) ? cfg.MacroHotbars[index].Name : name.Trim();
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public bool IsMacroHotbarLocked
    {
        get
        {
            EnsureMacroHotbars();
            var cfg = _pluginConfigService.Current;
            return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Locked;
        }
    }

    public System.Threading.Tasks.Task SetMacroHotbarLockedAsync(bool locked)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Locked = locked;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public bool IsMacroHotbarLockedAt(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return false;
        return cfg.MacroHotbars[index].Locked;
    }

    public System.Threading.Tasks.Task SetMacroHotbarLockedAtAsync(int index, bool locked)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[index].Locked = locked;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public int GetMacroHotbarColumns()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Columns;
    }

    public System.Threading.Tasks.Task SetMacroHotbarColumnsAsync(int columns)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Columns = System.Math.Clamp(columns, 1, 12);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public int GetMacroHotbarColumnsAt(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return 1;
        return cfg.MacroHotbars[index].Columns;
    }

    public System.Threading.Tasks.Task SetMacroHotbarColumnsAtAsync(int index, int columns)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[index].Columns = System.Math.Clamp(columns, 1, 12);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public int GetMacroHotbarRows()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Rows;
    }

    public System.Threading.Tasks.Task SetMacroHotbarRowsAsync(int rows)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].Rows = System.Math.Clamp(rows, 1, 12);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public int GetMacroHotbarRowsAt(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return 1;
        return cfg.MacroHotbars[index].Rows;
    }

    public System.Threading.Tasks.Task SetMacroHotbarRowsAtAsync(int index, int rows)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[index].Rows = System.Math.Clamp(rows, 1, 12);
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public float GetMacroHotbarButtonSide()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ButtonSide;
    }

    public System.Threading.Tasks.Task SetMacroHotbarButtonSideAsync(float side)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var clamped = side;
        if (clamped < 16f) clamped = 16f;
        if (clamped > 128f) clamped = 128f;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].ButtonSide = clamped;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public float GetMacroHotbarButtonSideAt(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return 70f;
        return cfg.MacroHotbars[index].ButtonSide;
    }

    public System.Threading.Tasks.Task SetMacroHotbarButtonSideAtAsync(int index, float side)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        var clamped = side;
        if (clamped < 16f) clamped = 16f;
        if (clamped > 128f) clamped = 128f;
        cfg.MacroHotbars[index].ButtonSide = clamped;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public bool IsMacroHotbarNoBackground
    {
        get
        {
            EnsureMacroHotbars();
            var cfg = _pluginConfigService.Current;
            return cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].NoBackground;
        }
    }

    public System.Threading.Tasks.Task SetMacroHotbarNoBackgroundAsync(bool noBackground)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex].NoBackground = noBackground;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public bool IsMacroHotbarNoBackgroundAt(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return false;
        return cfg.MacroHotbars[index].NoBackground;
    }

    public System.Threading.Tasks.Task SetMacroHotbarNoBackgroundAtAsync(int index, bool noBackground)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        if (index < 0 || index >= cfg.MacroHotbars.Count) return System.Threading.Tasks.Task.CompletedTask;
        cfg.MacroHotbars[index].NoBackground = noBackground;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    

    public int[] GetOpenMacroHotbarIndices()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var count = cfg.MacroHotbars.Count;
        var list = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
        var filtered = new System.Collections.Generic.List<int>();
        for (int i = 0; i < list.Count; i++) if (list[i] >= 0 && list[i] < count) filtered.Add(list[i]);
        cfg.OpenMacroHotbarIndices = filtered;
        _pluginConfigService.Save();
        return filtered.ToArray();
    }

    public int[] GetOpenMacroHotbarIndicesUnsafe()
    {
        var cfg = _pluginConfigService.Current;
        var count = cfg.MacroHotbars?.Count ?? 0;
        var list = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
        var filtered = new System.Collections.Generic.List<int>();
        for (int i = 0; i < list.Count; i++) if (list[i] >= 0 && list[i] < count) filtered.Add(list[i]);
        return filtered.ToArray();
    }

    public System.Threading.Tasks.Task SetMacroHotbarOpenStateAsync(int index, bool open)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var list = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
        list.RemoveAll(i => i == index);
        if (open) list.Add(index);
        cfg.OpenMacroHotbarIndices = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetShowVipNameplateHookAsync(bool enable)
    {
        _pluginConfigService.Current.ShowVipNameplateHook = enable;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }
    public System.Threading.Tasks.Task SetVipStarColorKeyAsync(ushort colorKey)
    {
        _pluginConfigService.Current.VipStarColorKey = colorKey;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetVipStarCharAsync(string ch)
    {
        _pluginConfigService.Current.VipStarChar = ch ?? string.Empty;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetVipStarPositionAsync(VenuePlus.Configuration.VipStarPosition pos)
    {
        _pluginConfigService.Current.VipStarPosition = pos;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetVipTextEnabledAsync(bool enable)
    {
        _pluginConfigService.Current.VipTextEnabled = enable;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetVipLabelTextAsync(string text)
    {
        _pluginConfigService.Current.VipLabelText = text ?? string.Empty;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetVipLabelOrderAsync(VenuePlus.Configuration.VipLabelOrder order)
    {
        _pluginConfigService.Current.VipLabelOrder = order;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task SetKeepWhisperMessageAsync(bool enable)
    {
        _pluginConfigService.Current.KeepWhisperMessage = enable;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> AddWhisperPresetAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return System.Threading.Tasks.Task.FromResult(false);
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return System.Threading.Tasks.Task.FromResult(false);
        if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > 500) return System.Threading.Tasks.Task.FromResult(false);
        var list = _pluginConfigService.Current.WhisperPresets ?? new System.Collections.Generic.List<VenuePlus.Configuration.WhisperPreset>();
        if (!list.Exists(p => string.Equals(p.Text, trimmed, System.StringComparison.Ordinal)))
        {
            var name = GeneratePresetName(trimmed, list.Count + 1);
            list.Add(new VenuePlus.Configuration.WhisperPreset { Name = name, Text = trimmed });
            list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            _pluginConfigService.Current.WhisperPresets = list;
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> RemoveWhisperPresetAtAsync(int index)
    {
        var list = _pluginConfigService.Current.WhisperPresets ?? new System.Collections.Generic.List<VenuePlus.Configuration.WhisperPreset>();
        if (index < 0 || index >= list.Count) return System.Threading.Tasks.Task.FromResult(false);
        list.RemoveAt(index);
        _pluginConfigService.Current.WhisperPresets = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> UpdateWhisperPresetAtAsync(int index, string text)
    {
        var list = _pluginConfigService.Current.WhisperPresets ?? new System.Collections.Generic.List<VenuePlus.Configuration.WhisperPreset>();
        if (index < 0 || index >= list.Count) return System.Threading.Tasks.Task.FromResult(false);
        var trimmed = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed)) return System.Threading.Tasks.Task.FromResult(false);
        if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > 500) return System.Threading.Tasks.Task.FromResult(false);
        var item = list[index];
        item.Text = trimmed;
        list[index] = item;
        list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
        _pluginConfigService.Current.WhisperPresets = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> UpdateWhisperPresetAtAsync(int index, string name, string text)
    {
        var list = _pluginConfigService.Current.WhisperPresets ?? new System.Collections.Generic.List<VenuePlus.Configuration.WhisperPreset>();
        if (index < 0 || index >= list.Count) return System.Threading.Tasks.Task.FromResult(false);
        var t = text?.Trim() ?? string.Empty;
        var n = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(t)) return System.Threading.Tasks.Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(n)) n = GeneratePresetName(t, index + 1);
        if (System.Text.Encoding.UTF8.GetByteCount(t) > 500) return System.Threading.Tasks.Task.FromResult(false);
        list[index] = new VenuePlus.Configuration.WhisperPreset { Name = n, Text = t };
        list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
        _pluginConfigService.Current.WhisperPresets = list;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> CreateWhisperPresetAsync(string name, string text)
    {
        var t = text?.Trim() ?? string.Empty;
        var n = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(t)) return System.Threading.Tasks.Task.FromResult(false);
        if (System.Text.Encoding.UTF8.GetByteCount(t) > 500) return System.Threading.Tasks.Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(n)) n = GeneratePresetName(t, 0);
        var list = _pluginConfigService.Current.WhisperPresets ?? new System.Collections.Generic.List<VenuePlus.Configuration.WhisperPreset>();
        if (!list.Exists(p => string.Equals(p.Name, n, System.StringComparison.Ordinal)))
        {
            list.Add(new VenuePlus.Configuration.WhisperPreset { Name = n, Text = t });
            list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            _pluginConfigService.Current.WhisperPresets = list;
            _pluginConfigService.Save();
        }
        return System.Threading.Tasks.Task.FromResult(true);
    }

    private static string GeneratePresetName(string text, int index)
    {
        var trimmed = (text ?? string.Empty).Trim();
        var max = 24;
        var baseName = trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        baseName = baseName.Replace("\n", " ").Replace("\r", " ");
        if (baseName.Length == 0) baseName = "Preset";
        return baseName;
    }

    public bool IsOwnerCurrentClub => string.Equals(_selfJob, "Owner", System.StringComparison.Ordinal);
    public string? CurrentClubLogoBase64 => _clubService.CurrentLogoBase64;
    

    

    public async System.Threading.Tasks.Task<bool> StaffLoginAsync(string username, string password)
    {
        if (!_clientState.IsLoggedIn) return false;
        var usernameFinal = !string.IsNullOrWhiteSpace(username)
            ? username
            : ((!string.IsNullOrWhiteSpace(_currentCharName) && !string.IsNullOrWhiteSpace(_currentCharWorld)) ? (_currentCharName + "@" + _currentCharWorld) : string.Empty);
        if (string.IsNullOrWhiteSpace(usernameFinal)) return false;
        var clubBeforeLogin = CurrentClubId;
        if (!string.IsNullOrWhiteSpace(clubBeforeLogin))
        {
            SetClubId(clubBeforeLogin);
        }
        var result = await _accessService.StaffLoginAsync(usernameFinal, password, GetCurrentCharacterKey(), CurrentClubId);
        if (result is null || string.IsNullOrWhiteSpace(result.Token)) return false;
        _isPowerStaff = true;
        _staffToken = result.Token;
        _staffUsername = result.Username;
        SetClubId(result.PreferredClubId);
        if (!RemoteConnected) await ConnectRemoteAsync();
        if (_remote.RemoteUseWebSocket && !string.IsNullOrWhiteSpace(CurrentClubId))
        {
            try { await _remote.SwitchClubAsync(CurrentClubId!); } catch { }
            try { await _remote.RequestShiftSnapshotAsync(_staffToken); } catch { }
        }
        _selfUid = result.SelfUid ?? _selfUid;
        _myClubs = result.MyClubs ?? _myClubs;
        _myCreatedClubs = result.MyCreatedClubs ?? _myCreatedClubs;
        _clubListsLoaded = (_myClubs != null) || (_myCreatedClubs != null);
        _jobRightsCache = result.JobRightsCache ?? _jobRightsCache;
        _usersDetailsCache = result.UsersDetailsCache ?? _usersDetailsCache;
        if (!string.IsNullOrWhiteSpace(result.CurrentClubLogoBase64))
        {
            _clubService.SetCurrentClubLogo(CurrentClubId, result.CurrentClubLogoBase64);
            try { ClubLogoChanged?.Invoke(result.CurrentClubLogoBase64); } catch { }
        }
        if (!string.IsNullOrWhiteSpace(result.FirstClubCandidate))
        {
            var pref = result.PreferredClubId ?? string.Empty;
            var cand = result.FirstClubCandidate ?? string.Empty;
            var hasPref = !string.IsNullOrWhiteSpace(pref);
            var prefInCreated = hasPref && (_myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, pref) >= 0);
            var prefInMember = hasPref && (_myClubs != null && System.Array.IndexOf(_myClubs, pref) >= 0);
            var prefValid = hasPref && (prefInCreated || prefInMember);
            if (!prefValid) SetClubId(cand);
        }
        _selfJob = result.SelfJob ?? _selfJob;
        if (result.SelfRights != null) _selfRights = result.SelfRights;
        EnsureSelfRights();
        EnsureValidClubAfterListFetch();
        _accessLoading = false;
        var keySave = GetCurrentCharacterKey();
        Configuration.CharacterProfile? profSave = null;
        if (!string.IsNullOrWhiteSpace(keySave)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keySave, out profSave);
        var rememberThis = profSave?.RememberStaffLogin ?? _pluginConfigService.Current.RememberStaffLogin;
        if (rememberThis)
        {
            _accessService.PersistSavedStaffCredentials(GetCurrentCharacterKey(), usernameFinal, password);
        }
        return true;
    }

    public async System.Threading.Tasks.Task<bool> StaffSetOwnPasswordAsync(string newPassword)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        return await _accessService.StaffSetOwnPasswordAsync(newPassword, _staffToken!);
    }

    public async System.Threading.Tasks.Task<string?> GenerateRecoveryCodeAsync()
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return null;
        return await _accessService.GenerateRecoveryCodeAsync(_staffToken!);
    }

    public async System.Threading.Tasks.Task<bool> ResetPasswordByRecoveryCodeAsync(string username, string recoveryCode, string newPassword)
    {
        if (!RemoteConnected) return false;
        return await _accessService.ResetPasswordByRecoveryCodeAsync(username, recoveryCode, newPassword);
    }

    public void StaffLogout()
    {
        _accessService.LogoutStaffAndReset(GetCurrentCharacterKey(), _staffToken);
        ClearAccessState();
    }

    public void LogoutAll()
    {
        _accessService.LogoutAllAndReset(GetCurrentCharacterKey(), _staffToken);
        ClearAccessState();
    }

    

    private void ClearAccessState()
    {
        _isPowerStaff = false;
        _staffToken = null;
        _staffUsername = null;
        _selfRights = new System.Collections.Generic.Dictionary<string, bool>();
        _selfRightsLastFetch = System.DateTimeOffset.MinValue;
        _selfJob = string.Empty;
        _jobsCache = null;
        _jobRightsCache = null;
        _usersCache = null;
        _usersDetailsCache = null;
        _myClubs = null;
        _myCreatedClubs = null;
        _accessLoading = false;
        _autoLoginAttempted = false;
        _selfUid = null;
        _djService.Clear();
        _shiftService.Clear();
        
    }

    private void OnRemoteDisconnected()
    {
        ClearAccessState();
    }

    public System.Threading.Tasks.Task<bool> SetRememberStaffLoginAsync(bool remember)
    {
        var needsPassword = _accessService.SetRememberStaffLogin(GetCurrentCharacterKey(), remember, _isPowerStaff);
        if (needsPassword)
        {
            try { RememberStaffNeedsPasswordEvt?.Invoke(); } catch { }
            var _np = GetNotificationPreferences();
            if (_np.ShowPasswordRequired)
            {
                try { _notifier?.ShowWarning("Password required to continue"); } catch { }
            }
        }
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> SetAutoLoginEnabledAsync(bool enabled)
    {
        _accessService.SetAutoLoginEnabled(GetCurrentCharacterKey(), enabled);
        return System.Threading.Tasks.Task.FromResult(true);
    }

    

    public async System.Threading.Tasks.Task TryAutoLoginAsync()
    {
        try
        {
            if (_isDisposing || !_clientState.IsLoggedIn) { return; }
            await _autoLoginGate.WaitAsync();
            if (_autoLoginAttempted) { return; }
            _accessLoading = true;
            var keyAuto = GetCurrentCharacterKey();
            var info = _accessService.GetAutoLoginInfo(keyAuto);
            if (!RemoteConnected)
            {
                SetClubId(info.PreferredClubId);
            }
            var connected = RemoteConnected || await ConnectRemoteAsync();
            if (!connected) { AutoLoginResultEvt?.Invoke(false, false); var _np = GetNotificationPreferences(); if (_np.ShowLoginFailed) { try { _notifier?.ShowInfo("Staff login failed"); } catch { } } return; }
            if (HasStaffSession) { AutoLoginResultEvt?.Invoke(IsOwnerCurrentClub, true); var _np = GetNotificationPreferences(); if (_np.ShowLoginSuccess) { try { _notifier?.ShowInfo("Logged in to staff session"); } catch { } } return; }
            var staffOk = false;
            var attempted = info.Enabled && info.Remembered && !string.IsNullOrWhiteSpace(info.SavedUsername) && !string.IsNullOrWhiteSpace(info.DecryptedPassword);
            if (attempted)
            {
                var uname = (!string.IsNullOrWhiteSpace(_currentCharName) && !string.IsNullOrWhiteSpace(_currentCharWorld))
                    ? (_currentCharName + "@" + _currentCharWorld)
                    : info.SavedUsername!;
                staffOk = await StaffLoginAsync(uname, info.DecryptedPassword!);
            }
            if (attempted)
            {
                AutoLoginResultEvt?.Invoke(false, staffOk);
                var _np5 = GetNotificationPreferences();
                if (staffOk)
                {
                    if (_np5.ShowLoginSuccess) { try { _notifier?.ShowInfo("Logged in"); } catch { } }
                }
                else
                {
                    if (_np5.ShowLoginFailed) { try { _notifier?.ShowInfo("Login failed"); } catch { } }
                }
            }
            _autoLoginAttempted = staffOk;
        }
        catch { }
        finally { _accessLoading = false; try { _autoLoginGate.Release(); } catch { } }
    }






    private void BuildSelfRightsFrom(JobRightsInfo r)
    {
        _selfRights = new System.Collections.Generic.Dictionary<string, bool>
        {
            ["addVip"] = r.AddVip,
            ["removeVip"] = r.RemoveVip,
            ["manageUsers"] = r.ManageUsers,
            ["manageJobs"] = r.ManageJobs,
            ["editVipDuration"] = r.EditVipDuration,
            ["addDj"] = r.AddDj,
            ["removeDj"] = r.RemoveDj,
            ["editShiftPlan"] = r.EditShiftPlan
        };
    }

    

    private void RemoveClubFromLists(string clubId)
    {
        if (!string.IsNullOrWhiteSpace(clubId))
        {
            if (_myClubs != null && _myClubs.Length > 0)
            {
                var list = new System.Collections.Generic.List<string>(_myClubs.Length);
                foreach (var c in _myClubs) { if (!string.Equals(c, clubId, System.StringComparison.Ordinal)) list.Add(c); }
                _myClubs = list.ToArray();
            }
            if (_myCreatedClubs != null && _myCreatedClubs.Length > 0)
            {
                var list2 = new System.Collections.Generic.List<string>(_myCreatedClubs.Length);
                foreach (var c in _myCreatedClubs) { if (!string.Equals(c, clubId, System.StringComparison.Ordinal)) list2.Add(c); }
                _myCreatedClubs = list2.ToArray();
            }
        }
    }


    private bool CanEditShiftPlanInternal()
    {
        return IsOwnerCurrentClub || (HasStaffSession && StaffCanEditShiftPlan);
    }

    private void TryNotifyShiftCreated(VenuePlus.State.ShiftEntry entry)
    {
        var _np = GetNotificationPreferences();
        if (_np.ShowShiftCreated)
        {
            try { _notifier?.ShowSuccess("Shift created: " + (entry.Title ?? string.Empty)); } catch { }
        }
    }

    private void TryNotifyShiftUpdated(VenuePlus.State.ShiftEntry entry)
    {
        var _np = GetNotificationPreferences();
        if (_np.ShowShiftUpdated)
        {
            try { _notifier?.ShowSuccess("Shift updated: " + (entry.Title ?? string.Empty)); } catch { }
        }
    }

    private void TryNotifyShiftRemoved()
    {
        var _np = GetNotificationPreferences();
        if (_np.ShowShiftRemoved)
        {
            try { _notifier?.ShowInfo("Shift removed"); } catch { }
        }
    }

    public async System.Threading.Tasks.Task<bool> ConnectRemoteAsync()
    {
        try
        {
            if (_isDisposing) return false;
            await _connectGate.WaitAsync();
            try
            {
                if (RemoteConnected) return true;
                var clubId = CurrentClubId;
                if (!string.IsNullOrWhiteSpace(clubId)) _remote.SetClubId(clubId);
                return await _remote.ConnectAsync(RemoteBaseUrlConst);
            }
            finally
            {
                _connectGate.Release();
            }
        }
        catch
        {
            return false;
        }
    }

    public async System.Threading.Tasks.Task DisconnectRemoteAsync()
    {
        await _remote.DisconnectAsync();
    }

    

    

    public string[]? GetMyClubs()
    {
        return _myClubs;
    }

    public string[]? GetMyCreatedClubs()
    {
        return _myCreatedClubs;
    }

    public async System.Threading.Tasks.Task<bool?> CheckUserExistsAsync(string username)
    {
        return await _remote.UserExistsAsync(username);
    }

    

    public bool RemoteUseWebSocket => _pluginConfigService.Current.RemoteUseWebSocket;

    public string RemoteBaseUrl => RemoteBaseUrlConst;

    private static string GetRemoteBaseUrl()
    {
        try
        {
            var asm = typeof(VenuePlusApp).Assembly;
            foreach (var attr in asm.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attr.Key, "RemoteBaseUrl", StringComparison.Ordinal))
                {
                    var v = attr.Value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
        }
        catch { }
        return "https://venueplus.sphene.online";
    }

    

    public void EnsureSelfRights()
    {
        if (!HasStaffSession) return;
        if (!string.IsNullOrWhiteSpace(_selfJob) && _jobRightsCache != null && _jobRightsCache.TryGetValue(_selfJob, out var r))
        {
            BuildSelfRightsFrom(r);
            return;
        }
    }

    public string? CurrentClubId
    {
        get
        {
            var key = GetCurrentCharacterKey();
            if (!string.IsNullOrWhiteSpace(key) && _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p.RemoteClubId))
                return p.RemoteClubId!;
            return string.IsNullOrWhiteSpace(_pluginConfigService.Current.RemoteClubId) ? null : _pluginConfigService.Current.RemoteClubId!;
        }
    }

    public async System.Threading.Tasks.Task<string[]?> ListStaffUsersAsync()
    {
        var staffSess = _staffToken ?? string.Empty;
        if (_pluginConfigService.Current.RemoteUseWebSocket) return _usersCache;
        if (string.IsNullOrWhiteSpace(staffSess)) return null;
        var arr = await _remote.ListUsersAsync(staffSess);
        return arr;
    }

    public async System.Threading.Tasks.Task<VenuePlus.State.StaffUser[]?> ListStaffUsersDetailedAsync()
    {
        var staffSess = _staffToken ?? string.Empty;
        if (_pluginConfigService.Current.RemoteUseWebSocket) return _usersDetailsCache;
        if (string.IsNullOrWhiteSpace(staffSess)) return null;
        var det = await _remote.ListUsersDetailedAsync(staffSess);
        return det;
    }

    public async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, JobRightsInfo>?> ListJobRightsAsync()
    {
        var sess = _staffToken ?? string.Empty;
        if (_pluginConfigService.Current.RemoteUseWebSocket) return _jobRightsCache;
        if (string.IsNullOrWhiteSpace(sess)) return null;
        var dict = await _remote.ListJobRightsAsync(sess);
        return dict;
    }

    public System.Collections.Generic.Dictionary<string, JobRightsInfo>? GetJobRightsCache()
    {
        return _jobRightsCache;
    }

    public string? CurrentStaffUid => _selfUid;

    public async System.Threading.Tasks.Task<bool> UpdateJobRightsAsync(string name, bool addVip, bool removeVip, bool manageUsers, bool manageJobs, bool editVipDuration, bool addDj, bool removeDj, bool editShiftPlan, string colorHex = "#FFFFFF", string iconKey = "User", int rank = 1)
    {
        var sess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sess)) return false;
        int r;
        if (string.Equals(name, "Owner", System.StringComparison.Ordinal)) r = 10;
        else if (string.Equals(name, "Unassigned", System.StringComparison.Ordinal)) r = 0;
        else r = rank <= 0 ? 1 : (rank > 9 ? 9 : rank);
        var info = new JobRightsInfo { AddVip = addVip, RemoveVip = removeVip, ManageUsers = manageUsers, ManageJobs = manageJobs, EditVipDuration = editVipDuration, AddDj = addDj, RemoveDj = removeDj, EditShiftPlan = editShiftPlan, Rank = r, ColorHex = colorHex ?? "#FFFFFF", IconKey = iconKey ?? "User" };
        return await _remote.UpdateJobRightsAsync(name, info, sess);
    }

    public async System.Threading.Tasks.Task<bool> UpdateStaffUserJobAsync(string username, string job)
    {
        var staffSess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(staffSess)) return false;
        if (!string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(username, _staffUsername, System.StringComparison.Ordinal) && !IsOwnerCurrentClub)
        {
            var rightsCache = _jobRightsCache;
            if (rightsCache == null || !rightsCache.TryGetValue(job, out var r) || !r.ManageJobs) return false;
        }
        if (string.Equals(job, "Owner", System.StringComparison.Ordinal) && !IsOwnerCurrentClub) return false;
        if (!string.Equals(job, "Owner", System.StringComparison.Ordinal))
        {
            var owners = 0;
            var isTargetOwner = false;
            var list = await ListStaffUsersDetailedAsync();
            var arr = list ?? System.Array.Empty<VenuePlus.State.StaffUser>();
            for (int i = 0; i < arr.Length; i++)
            {
                var u = arr[i];
                if (string.Equals(u.Job, "Owner", System.StringComparison.Ordinal)) owners++;
                if (string.Equals(u.Username, username, System.StringComparison.Ordinal) && string.Equals(u.Job, "Owner", System.StringComparison.Ordinal)) isTargetOwner = true;
            }
            if (isTargetOwner && owners <= 1 && !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(username, _staffUsername, System.StringComparison.Ordinal)) return false;
        }
        var ok = await _remote.UpdateUserJobAsync(username, job, staffSess);
        if (ok && !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(username, _staffUsername, System.StringComparison.Ordinal))
        {
            _selfJob = job ?? string.Empty;
        }
        return ok;
    }

    public async System.Threading.Tasks.Task<bool> DeleteStaffUserAsync(string username)
    {
        var staffSess = _staffToken ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(username, _staffUsername, System.StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(staffSess)) return false;
        return await _remote.DeleteUserAsync(username, staffSess);
    }

    public async System.Threading.Tasks.Task<string[]?> ListJobsAsync()
    {
        var sess = _staffToken ?? string.Empty;
        if (_pluginConfigService.Current.RemoteUseWebSocket) return _jobsCache;
        if (string.IsNullOrWhiteSpace(sess)) return null;
        var arr = await _remote.ListJobsAsync(sess);
        return arr;
    }

    public async System.Threading.Tasks.Task<bool> AddJobAsync(string name)
    {
        var sess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sess)) return false;
        return await _remote.AddJobAsync(name, sess);
    }

    public async System.Threading.Tasks.Task<bool> DeleteJobAsync(string name)
    {
        var sess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sess)) return false;
        return await _remote.DeleteJobAsync(name, sess);
    }

    

    public async System.Threading.Tasks.Task<bool> CreateUserAsync(string username, string password)
    {
        var staffSess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(staffSess))
        {
            _log?.Debug("Create user failed: missing authorization");
            return false;
        }
        _log?.Debug($"Create user attempt name={username}");
        var ok = await _remote.CreateUserAsync(username, password, string.IsNullOrWhiteSpace(staffSess) ? string.Empty : staffSess);
        _log?.Debug($"Create user result ok={ok}");
        return ok;
    }

    private async void TryPublishAdd(VipEntry entry)
    {
        if (!HasStaffSession) { _log?.Debug("Publish add denied: not authorized"); return; }
        try
        {
            var okSess = await _remote.PublishAddWithSessionAsync(entry, _staffToken!);
            _log?.Debug($"Publish add session ok={okSess}");
        }
        catch (Exception ex)
        {
            _log?.Error($"Publish add session failed: {ex.Message}");
        }
    }

    private async void TryPublishRemove(VipEntry entry)
    {
        if (!HasStaffSession) { _log?.Debug("Publish remove denied: not authorized"); return; }
        try
        {
            var okSess = await _remote.PublishRemoveWithSessionAsync(entry, _staffToken!);
            _log?.Debug($"Publish remove session ok={okSess}");
        }
        catch (Exception ex)
        {
            _log?.Error($"Publish remove session failed: {ex.Message}");
        }
    }

    public void OnSnapshotReceived(System.Collections.Generic.IReadOnlyCollection<VipEntry> entries)
    {
        var cnt = entries.Count;
        _vipService.ReplaceAllForActiveClub(entries ?? System.Array.Empty<VipEntry>());
    }

    public void OnEntryAdded(VipEntry entry)
    {
        _vipService.SetFromRemote(entry);
        var name = entry?.CharacterName ?? string.Empty;
        var world = entry?.HomeWorld ?? string.Empty;
        var dur = entry?.Duration.ToString() ?? string.Empty;
        var _np = GetNotificationPreferences();
        if (_np.ShowVipAdded)
        {
            try { _notifier?.ShowInfo("VIP added: " + name + " (" + world + ") — Duration: " + dur); } catch { }
        }
    }

    public void OnEntryRemoved(VipEntry entry)
    {
        _vipService.Remove(entry.CharacterName, entry.HomeWorld);
        var name = entry?.CharacterName ?? string.Empty;
        var world = entry?.HomeWorld ?? string.Empty;
        var _np2 = GetNotificationPreferences();
        if (_np2.ShowVipRemoved)
        {
            try { _notifier?.ShowInfo("VIP removed: " + name + " (" + world + ")"); } catch { }
        }
    }

    public void OnDjSnapshotReceived(VenuePlus.State.DjEntry[]? entries)
    {
        _djService.ReplaceAll(entries);
    }

    public void OnDjEntryAdded(VenuePlus.State.DjEntry entry)
    {
        _djService.SetOrAdd(entry);
        var djName = entry?.DjName ?? string.Empty;
        var _np3 = GetNotificationPreferences();
        if (_np3.ShowDjAdded)
        {
            try { _notifier?.ShowInfo("DJ added: " + djName); } catch { }
        }
    }

    public void OnDjEntryRemoved(VenuePlus.State.DjEntry entry)
    {
        _djService.RemoveByName(entry.DjName);
    }

    public void OnShiftSnapshotReceived(VenuePlus.State.ShiftEntry[]? entries)
    {
        _shiftService.ReplaceAll(entries);
    }

    public void OnShiftEntryAdded(VenuePlus.State.ShiftEntry entry)
    {
        _shiftService.AddOrUpdate(entry);
    }

    public void OnShiftEntryUpdated(VenuePlus.State.ShiftEntry entry)
    {
        _shiftService.AddOrUpdate(entry);
    }

    public void OnShiftEntryRemoved(Guid id)
    {
        _shiftService.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isDisposing = true;
        try { _cts.Cancel(); } catch { }
        try { _autoLoginMonitorCts?.Cancel(); } catch { }
        try { _autoPurge.Dispose(); } catch { }
        try { _eventService.Dispose(); } catch { }
        try { _remote.Dispose(); } catch { }
    }

    public async System.Threading.Tasks.Task<bool> RegisterCurrentCharacterAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(_currentCharName) || string.IsNullOrWhiteSpace(_currentCharWorld)) return false;
        var name = _currentCharName;
        var world = _currentCharWorld;
        var ok = await _remote.RegisterAsync(name, world, password);
        if (!ok) return false;
        var username = name + "@" + world;
        return await StaffLoginAsync(username, password);
    }

    public async System.Threading.Tasks.Task<bool> RegisterCharacterAsync(string name, string world, string password)
    {
        var ok = await _remote.RegisterAsync(name, world, password);
        if (!ok) return false;
        var username = name + "@" + world;
        return await StaffLoginAsync(username, password);
    }

    public (string name, string world)? GetCurrentCharacter()
    {
        var player = _objectTable.LocalPlayer;
        if (player == null) return null;
        var name = player.Name.TextValue;
        var world = player.HomeWorld.Value.Name.ToString();
        return (name, world);
    }

    private string GetCurrentCharacterKey()
    {
        if (string.IsNullOrWhiteSpace(_currentCharName) || string.IsNullOrWhiteSpace(_currentCharWorld)) return string.Empty;
        return _currentCharName + "@" + _currentCharWorld;
    }

    public void UpdateCurrentCharacterCache()
    {
        var prev = _wasLoggedIn;
        var isLoggedIn = _clientState.IsLoggedIn;
        if (prev && !isLoggedIn)
        {
            OnCharacterLoggedOut();
        }
        if (!isLoggedIn) { _currentCharName = string.Empty; _currentCharWorld = string.Empty; _wasLoggedIn = false; return; }
        var player = _objectTable.LocalPlayer;
        if (player == null) { _currentCharName = string.Empty; _currentCharWorld = string.Empty; return; }
        _currentCharName = player.Name.TextValue;
        _currentCharWorld = player.HomeWorld.Value.Name.ToString();
        if (!prev && isLoggedIn)
        {
            OnCharacterLoggedIn();
        }
        _wasLoggedIn = isLoggedIn;
    }

    private void OnCharacterLoggedOut()
    {
        if (_isDisposing) return;
        var staff = _staffToken;
        if (!string.IsNullOrWhiteSpace(staff)) { _ = _remote.LogoutSessionAsync(staff); }
        ClearAccessState();
        _ = _remote.DisconnectAsync();
    }

    private void OnCharacterLoggedIn()
    {
        if (_isDisposing) return;
        System.Threading.Tasks.Task.Run(async () =>
        {
            if (_pluginConfigService.Current.AutoLoginEnabled && !HasStaffSession)
            {
                await TryAutoLoginAsync();
            }
            _accessService.EnsureProfileHasSavedCredentials(GetCurrentCharacterKey());
        });
    }

    public void SetClubId(string? clubId)
    {
        _desiredClubId = clubId;
        _vipService.SetActiveClub(clubId);
        _remote.SetClubId(clubId);
        if (string.IsNullOrWhiteSpace(clubId))
        {
            _selfJob = string.Empty;
        }
        var keyClub = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(keyClub))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyClub, out var profClub))
            {
                profClub = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[keyClub] = profClub;
            }
            profClub.RemoteClubId = clubId;
        }
        else
        {
            _pluginConfigService.Current.RemoteClubId = clubId;
        }
        _pluginConfigService.Save();
        _selfRightsLastFetch = System.DateTimeOffset.MinValue;
        _accessLoading = true;
        var ct3 = _cts.Token;
        System.Threading.Tasks.Task.Run(async () =>
        {
            if (ct3.IsCancellationRequested) { _accessLoading = false; return; }
            try
            {
                if (!string.IsNullOrWhiteSpace(clubId))
                {
                    if (_remote.RemoteUseWebSocket)
                    {
                        var _ = await _remote.SwitchClubAsync(clubId);
                        try { await _remote.RequestShiftSnapshotAsync(_staffToken); } catch { }
                    }
                    else
                    {
                        await _remote.FetchSnapshotAsync();
                    }
                    if (!string.IsNullOrWhiteSpace(_staffToken))
                    {
                        if (!_remote.RemoteUseWebSocket)
                        {
                            var rights = await _remote.GetSelfRightsAsync(_staffToken!);
                            if (rights.HasValue)
                            {
                                _selfJob = rights.Value.Job;
                                _selfRights = rights.Value.Rights ?? new System.Collections.Generic.Dictionary<string, bool>();
                            }
                        }
                        if (!_remote.RemoteUseWebSocket)
                        {
                            var logo = await _remote.GetClubLogoAsync(_staffToken!);
                            _clubService.SetCurrentClubLogo(clubId, logo);
                            try { ClubLogoChanged?.Invoke(logo); } catch { }
                        }
                    }
                }
            }
            catch { }
            finally { _accessLoading = false; }
        });
    }

    public async System.Threading.Tasks.Task<bool> RegisterClubAsync(string clubId)
    {
        var ok = await _remote.RegisterClubAsync(
            clubId,
            null,
            null,
            _staffToken,
            _staffUsername
        );
        if (!ok) return false;
        SetClubId(clubId);
        if (string.IsNullOrWhiteSpace(_staffToken)) return true;
        if (_myCreatedClubs == null) _myCreatedClubs = new[] { clubId };
        else if (System.Array.IndexOf(_myCreatedClubs, clubId) < 0)
        {
            var listTmp = new System.Collections.Generic.List<string>(_myCreatedClubs);
            listTmp.Add(clubId);
            listTmp.Sort(System.StringComparer.Ordinal);
            _myCreatedClubs = listTmp.ToArray();
        }
        if (!_pluginConfigService.Current.RemoteUseWebSocket)
        {
            try { if (!string.IsNullOrWhiteSpace(_staffToken)) _myCreatedClubs = await _remote.ListCreatedClubsAsync(_staffToken!); } catch { }
            try { if (!string.IsNullOrWhiteSpace(_staffToken)) _myClubs = await _remote.ListUserClubsAsync(_staffToken!); } catch { }
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(_staffToken) && !_remote.RemoteUseWebSocket)
            {
                var rights = await _remote.GetSelfRightsAsync(_staffToken!);
                if (rights.HasValue)
                {
                    _selfJob = rights.Value.Job;
                    _selfRights = rights.Value.Rights ?? new System.Collections.Generic.Dictionary<string, bool>();
                }
            }
        }
        catch { }
        return true;
    }

    public async System.Threading.Tasks.Task<bool> JoinClubAsync(string clubId)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        var ok = await _remote.JoinClubAsync(clubId, _staffToken!, _pendingJoinPassword);
        if (!ok) return false;
        if (!_pluginConfigService.Current.RemoteUseWebSocket)
        {
            try { if (!string.IsNullOrWhiteSpace(_staffToken)) _myClubs = await _remote.ListUserClubsAsync(_staffToken!); } catch { }
        }
        return true;
    }

    private string? _pendingJoinPassword;
    public void SetPendingJoinPassword(string? password)
    {
        _pendingJoinPassword = password;
    }

    public string? GetLastServerMessage()
    {
        return _remote.LastErrorMessage;
    }

    private string? _currentAccessKey;
    public string? CurrentAccessKey => _currentAccessKey;
    public string GetServerBaseUrl() => _remote.GetBaseUrl();

    public async System.Threading.Tasks.Task<bool> DeleteCurrentClubAsync()
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        var clubId = CurrentClubId;
        if (string.IsNullOrWhiteSpace(clubId)) return false;
        var ok = await _remote.DeleteClubAsync(clubId, _staffToken!);
        if (!ok) return false;
        RemoveClubFromLists(clubId);
        if (!string.IsNullOrWhiteSpace(clubId))
        {
            _clubService.RemoveClub(clubId);
        }
        try { if (!string.IsNullOrWhiteSpace(_staffToken)) _myCreatedClubs = await _remote.ListCreatedClubsAsync(_staffToken!); } catch { }
        try { if (!string.IsNullOrWhiteSpace(_staffToken)) _myClubs = await _remote.ListUserClubsAsync(_staffToken!); } catch { }
        SetClubId(null);
        return true;
    }

    public async System.Threading.Tasks.Task RefreshAccessKeyAsync()
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return;
        var clubId = CurrentClubId; if (string.IsNullOrWhiteSpace(clubId)) return;
        var key = await _remote.GetAccessKeyAsync(clubId!, _staffToken!);
        _currentAccessKey = key;
    }

    public async System.Threading.Tasks.Task<bool> RegenerateAccessKeyAsync()
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        var clubId = CurrentClubId; if (string.IsNullOrWhiteSpace(clubId)) return false;
        var key = await _remote.RegenerateAccessKeyAsync(clubId!, _staffToken!);
        if (string.IsNullOrWhiteSpace(key)) return false;
        _currentAccessKey = key;
        return true;
    }

    public async System.Threading.Tasks.Task<bool> SetClubJoinPasswordAsync(string newPassword)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        if (!IsOwnerCurrentClub) return false;
        return await _remote.SetJoinPasswordAsync(newPassword ?? string.Empty, _staffToken!);
    }

    public async System.Threading.Tasks.Task<bool> InviteStaffByUidAsync(string uid, string? job)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        var canInvite = IsOwnerCurrentClub || (HasStaffSession && StaffCanManageUsers);
        if (!canInvite) return false;
        return await _remote.InviteStaffByUidAsync(uid, job, _staffToken!);
    }

    public async System.Threading.Tasks.Task<bool> UpdateClubLogoBase64Async(string logoBase64)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        if (!IsOwnerCurrentClub) return false;
        var ok = await _remote.UpdateClubLogoAsync(logoBase64 ?? string.Empty, _staffToken!);
        if (ok)
        {
            _clubService.SetCurrentClubLogo(CurrentClubId, logoBase64);
            try { ClubLogoChanged?.Invoke(logoBase64); } catch { }
        }
        return ok;
    }

    public async System.Threading.Tasks.Task<bool> UpdateClubLogoFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (!System.IO.File.Exists(filePath)) return false;
        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var b64 = Convert.ToBase64String(bytes);
        return await UpdateClubLogoBase64Async(b64);
    }

    public async System.Threading.Tasks.Task<bool> DeleteClubLogoAsync()
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        if (!IsOwnerCurrentClub) return false;
        var ok = await _remote.DeleteClubLogoAsync(_staffToken!);
        if (ok)
        {
            _clubService.SetCurrentClubLogo(CurrentClubId, null);
            try { ClubLogoChanged?.Invoke(null); } catch { }
        }
        return ok;
    }

    public string? GetClubLogoForClub(string clubId)
    {
        return _clubService.GetLogoForClub(clubId);
    }

    public async System.Threading.Tasks.Task<string?> FetchClubLogoForClubAsync(string clubId)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return null;
        var logo = await _remote.GetClubLogoForAsync(_staffToken!, clubId);
        _clubService.SetClubLogoForClub(clubId, string.IsNullOrWhiteSpace(logo) ? null : logo);
        return logo;
    }
    

    public VenuePlus.Configuration.NotificationPreferences GetNotificationPreferences()
    {
        return _pluginConfigService.Current.Notifications;
    }

    public void SavePluginConfig()
    {
        _pluginConfigService.Save();
    }

    public void SetNotifier(VenuePlus.Services.NotificationService notifier)
    {
        _notifier = notifier;
    }


    public void OnJobsListReceived(string[] arr)
    {
        var incoming = arr ?? Array.Empty<string>();
        var fp = string.Join("|", incoming);
        if (!string.Equals(_jobsFingerprint, fp, System.StringComparison.Ordinal))
        {
            _jobsFingerprint = fp;
            _jobsCache = incoming;
            JobsChanged?.Invoke(incoming);
        }
    }

    public void OnJobRightsReceived(System.Collections.Generic.Dictionary<string, JobRightsInfo> dict)
    {
        _jobRightsCache = dict;
        JobRightsChanged?.Invoke(dict);
    }

    public void OnUsersListReceived(string[] arr)
    {
        _usersCache = arr;
    }

    public void OnUsersDetailsReceived(VenuePlus.State.StaffUser[] det)
    {
        _usersDetailsCache = det;
        var uname = _staffUsername ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(uname) && det != null)
        {
            for (int i = 0; i < det.Length; i++)
            {
                if (string.Equals(det[i].Username, uname, System.StringComparison.Ordinal))
                {
                    _selfJob = det[i].Job ?? string.Empty;
                    var uidCandidate = det[i].Uid;
                    _selfUid = string.IsNullOrWhiteSpace(uidCandidate) ? _selfUid : uidCandidate;
                    EnsureSelfRights();
                    break;
                }
            }
        }
        var incoming = det ?? Array.Empty<VenuePlus.State.StaffUser>();
        var ordered = incoming.OrderBy(u => u.Username, System.StringComparer.Ordinal).Select(u => (u.Username ?? string.Empty) + "#" + (u.Job ?? string.Empty) + "#" + (u.Uid ?? string.Empty)).ToArray();
        var fp = string.Join("|", ordered);
        if (!string.Equals(_usersDetailsFingerprint, fp, System.StringComparison.Ordinal))
        {
            _usersDetailsFingerprint = fp;
            UsersDetailsChanged?.Invoke(incoming);
        }
    }

    public void OnUserJobUpdated(string username, string job)
    {
        var isSelf = !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, username, System.StringComparison.Ordinal);
        if (isSelf)
        {
            string jobKey = job ?? string.Empty;
            _selfJob = jobKey;
            if (_jobRightsCache != null && !string.IsNullOrWhiteSpace(jobKey) && _jobRightsCache.TryGetValue(jobKey, out var r))
            {
                BuildSelfRightsFrom(r);
            }
            var _np = GetNotificationPreferences();
            if (_np.ShowRoleChangedSelf)
            {
                try { _notifier?.ShowSuccess("Your role is now: " + jobKey); } catch { }
            }
        }
        UserJobUpdatedEvt?.Invoke(username ?? string.Empty, job ?? string.Empty);
    }

    public void OnOwnerAccessChanged(string owner, string clubIdEvt)
    {
        var isSelf = !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, owner, System.StringComparison.Ordinal);
        var clubIdMsg = clubIdEvt ?? string.Empty;
        var ownerName = owner ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clubIdMsg) || string.IsNullOrWhiteSpace(ownerName)) return;
        var previousOwner = _clubService.GetOwnerForClub(clubIdMsg);
        if (string.Equals(previousOwner, ownerName, System.StringComparison.Ordinal)) return;
        _clubService.SetOwnerForClub(clubIdMsg, ownerName);
        if (isSelf)
        {
            var alreadyOwned = _myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, clubIdMsg) >= 0;
            if (!alreadyOwned)
            {
                var list = (_myCreatedClubs ?? Array.Empty<string>()).ToList();
                list.Add(clubIdMsg);
                _myCreatedClubs = list.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var _np = GetNotificationPreferences();
                if (_np.ShowOwnershipGranted)
                {
                    try { _notifier?.ShowSuccess("Ownership granted for club: " + clubIdMsg); } catch { }
                }
            }
        }
        else
        {
            var wasOwned = _myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, clubIdMsg) >= 0;
            if (wasOwned)
            {
                var curCreated = _myCreatedClubs ?? System.Array.Empty<string>();
                var list2 = new System.Collections.Generic.List<string>(curCreated.Length);
                foreach (var c in curCreated) { if (!string.Equals(c, clubIdMsg, System.StringComparison.Ordinal)) list2.Add(c); }
                _myCreatedClubs = list2.ToArray();
                var _np2 = GetNotificationPreferences();
                if (_np2.ShowOwnershipTransferred)
                {
                    try { _notifier?.ShowInfo("Main Ownership transferred to: " + owner); } catch { }
                }
            }
        }
    }

    public void OnMembershipRemoved(string username, string clubIdEvt)
    {
        var removedClub = clubIdEvt ?? string.Empty;
        var isSelf = !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, username, System.StringComparison.Ordinal);
        var wasPresent = (_myClubs != null && System.Array.IndexOf(_myClubs, removedClub) >= 0) || (_myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, removedClub) >= 0);
        if (isSelf)
        {
            if (!string.IsNullOrWhiteSpace(removedClub))
            {
                RemoveClubFromLists(removedClub);
                var _np3 = GetNotificationPreferences();
                if (_np3.ShowMembershipRemoved)
                {
                    try { _notifier?.ShowInfo("Removed from venue: " + removedClub); } catch { }
                }
            }
            if (!string.IsNullOrWhiteSpace(CurrentClubId) && string.Equals(CurrentClubId, removedClub, System.StringComparison.Ordinal))
            {
                var next = (_myClubs != null && _myClubs.Length > 0) ? _myClubs[0] : null;
                SetClubId(next);
            }
        }
        var ct = _cts.Token;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (!ct.IsCancellationRequested && !string.IsNullOrWhiteSpace(_staffToken))
                {
                    _myClubs = await _remote.ListUserClubsAsync(_staffToken!);
                    _myCreatedClubs = await _remote.ListCreatedClubsAsync(_staffToken!);
                    var cur = CurrentClubId;
                    var inClubs = (!string.IsNullOrWhiteSpace(cur) && _myClubs != null && System.Array.IndexOf(_myClubs, cur) >= 0) || (!string.IsNullOrWhiteSpace(cur) && _myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, cur) >= 0);
                    if (isSelf && !inClubs)
                    {
                        var next = (_myClubs != null && _myClubs.Length > 0) ? _myClubs[0] : null;
                        SetClubId(next);
                    }
                }
            }
            catch { }
        });
    }

    public void OnMembershipAdded(string username, string clubId)
    {
        if (!string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, username, System.StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(clubId))
            {
                if (_myClubs == null) _myClubs = new[] { clubId };
                else if (System.Array.IndexOf(_myClubs, clubId) < 0)
                {
                    var list = new System.Collections.Generic.List<string>(_myClubs);
                    list.Add(clubId);
                    list.Sort(System.StringComparer.Ordinal);
                    _myClubs = list.ToArray();
                }
                SetClubId(clubId);
                var _np4 = GetNotificationPreferences();
                if (_np4.ShowMembershipJoined)
                {
                    try { _notifier?.ShowInfo($"Joined venue: {clubId}"); } catch { }
                }
            }
            var ct = _cts.Token;
            System.Threading.Tasks.Task.Run(async () => { try { if (!ct.IsCancellationRequested && !string.IsNullOrWhiteSpace(_staffToken)) _myClubs = await _remote.ListUserClubsAsync(_staffToken!); } catch { } });
        }
    }

    public void OnClubLogoReceived(string? base64)
    {
        _clubService.SetCurrentClubLogo(CurrentClubId, base64);
        try { ClubLogoChanged?.Invoke(base64); } catch { }
    }

    public void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            OnRemoteDisconnected();
        }
        else
        {
            if (HasStaffSession)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var clubId = CurrentClubId;
                        if (!string.IsNullOrWhiteSpace(clubId)) { try { await _remote.SwitchClubAsync(clubId!); } catch { } try { await _remote.RequestShiftSnapshotAsync(_staffToken); } catch { } }
                        var tRights = _remote.ListJobRightsAsync(_staffToken!);
                        var tUsers = _remote.ListUsersDetailedAsync(_staffToken!);
                        var tClubs = _remote.ListUserClubsAsync(_staffToken!);
                        var tCreated = _remote.ListCreatedClubsAsync(_staffToken!);
                        var tLogo = _remote.GetClubLogoAsync(_staffToken!);
                        try { await System.Threading.Tasks.Task.WhenAll(new System.Threading.Tasks.Task[] { tRights, tUsers, tClubs, tCreated, tLogo }); } catch { }
                        try { _jobRightsCache = tRights.IsCompleted ? (tRights.Result ?? _jobRightsCache) : _jobRightsCache; } catch { }
                        try { _usersDetailsCache = tUsers.IsCompleted ? tUsers.Result : _usersDetailsCache; } catch { }
                        try { _myClubs = tClubs.IsCompleted ? tClubs.Result : _myClubs; } catch { }
                        try { _myCreatedClubs = tCreated.IsCompleted ? tCreated.Result : _myCreatedClubs; } catch { }
                        _clubListsLoaded = (_myClubs != null) || (_myCreatedClubs != null);
                        try
                        {
                            var logo = tLogo.IsCompleted ? tLogo.Result : null;
                            _clubService.SetCurrentClubLogo(CurrentClubId, logo);
                            try { ClubLogoChanged?.Invoke(logo); } catch { }
                        }
                        catch { }
                        EnsureValidClubAfterListFetch();
                    }
                    finally { _accessLoading = false; }
                });
            }
            else if (AutoLoginEnabled && !_autoLoginAttempted)
            {
                try { _log?.Debug($"[AutoLogin] connected event: enabled={AutoLoginEnabled} hasSession={HasStaffSession} attempted={_autoLoginAttempted}"); } catch { }
                System.Threading.Tasks.Task.Run(async () => { await TryAutoLoginAsync(); });
            }
        }
    }

    private void EnsureValidClubAfterListFetch()
    {
        if (!_clubListsLoaded) return;
        var cur = CurrentClubId;
        var inCreated = (!string.IsNullOrWhiteSpace(cur) && _myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, cur) >= 0);
        var inMember = (!string.IsNullOrWhiteSpace(cur) && _myClubs != null && System.Array.IndexOf(_myClubs, cur) >= 0);
        var inClubs = inCreated || inMember;
        var desired = _desiredClubId;
        if (!string.IsNullOrWhiteSpace(desired))
        {
            var desiredInCreated = (_myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, desired) >= 0);
            var desiredInMember = (_myClubs != null && System.Array.IndexOf(_myClubs, desired) >= 0);
            if (desiredInCreated || desiredInMember)
            {
                if (!string.Equals(cur, desired, System.StringComparison.Ordinal)) SetClubId(desired);
                return;
            }
        }
        if (!inClubs)
        {
            var next = (_myCreatedClubs != null && _myCreatedClubs.Length > 0) ? _myCreatedClubs[0] : ((_myClubs != null && _myClubs.Length > 0) ? _myClubs[0] : null);
            if (!string.Equals(cur, next, System.StringComparison.Ordinal)) SetClubId(next);
        }
        else { }
    }

    public System.Threading.Tasks.Task RemoveMacroHotbarSlotAtAsync(int index)
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var bar = cfg.MacroHotbars[cfg.CurrentMacroHotbarIndex];
        var list = bar.Slots ?? new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbarSlot>();
        try { _log?.Debug($"RemoveSlot: bar={cfg.CurrentMacroHotbarIndex} slot={index} count={list.Count}"); } catch { }
        if (index >= 0 && index < list.Count && list.Count > 1)
        {
            list.RemoveAt(index);
            bar.Slots = list;
            _pluginConfigService.Save();
            try { _log?.Debug($"RemoveSlot: bar={cfg.CurrentMacroHotbarIndex} done newCount={list.Count}"); } catch { }
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void LogDebug(string text)
    {
        try { _log?.Debug(text); } catch { }
    }

    
    public System.Threading.Tasks.Task RemoveAllMacroHotbarsAsync()
    {
        EnsureMacroHotbars();
        var cfg = _pluginConfigService.Current;
        var open = cfg.OpenMacroHotbarIndices ?? new System.Collections.Generic.List<int>();
        foreach (var idx in open)
        {
            try { CloseMacroHotbarIndexRequested?.Invoke(idx); } catch { }
        }
        cfg.MacroHotbars = new System.Collections.Generic.List<VenuePlus.Configuration.MacroHotbar>();
        cfg.OpenMacroHotbarIndices = new System.Collections.Generic.List<int>();
        cfg.CurrentMacroHotbarIndex = 0;
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
