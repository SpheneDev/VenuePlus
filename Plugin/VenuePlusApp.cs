using System;
using VenuePlus.Services;
using System.Linq;
using VenuePlus.State;
using VenuePlus.Configuration;
using VenuePlus.Helpers;
using Dalamud.Plugin.Services;

namespace VenuePlus.Plugin;

public sealed class VenuePlusApp : IDisposable
{
    private readonly VipService _vipService;
    private readonly AutoPurgeService _autoPurge;
    private readonly PluginConfigService _pluginConfigService;
    private readonly RemoteSyncService _remote;
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
    private const string RemoteBaseUrlConst = "https://venueplus.sphene.online";
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
    private VenuePlus.State.DjEntry[] _djEntries = Array.Empty<VenuePlus.State.DjEntry>();
    private readonly System.Threading.SemaphoreSlim _connectGate = new(1, 1);
    private readonly System.Threading.SemaphoreSlim _autoLoginGate = new(1, 1);
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private System.Threading.CancellationTokenSource? _autoLoginMonitorCts;
    private System.Threading.Tasks.Task? _autoLoginMonitorTask;
    private bool _isDisposing;
    public event Action<VenuePlus.State.StaffUser[]>? UsersDetailsChanged;
    public event Action<string, string>? UserJobUpdatedEvt;
    public event Action<string[]>? JobsChanged;
    public event Action<System.Collections.Generic.Dictionary<string, JobRightsInfo>?>? JobRightsChanged;
    public event Action<bool, bool>? AutoLoginResultEvt;
    public event Action? RememberStaffNeedsPasswordEvt;
    public event Action? OpenSettingsRequested;
    public event Action<string?>? ClubLogoChanged;
    public event Action<string>? Notification;

    public VenuePlusApp(string? vipDataPath = null, string? pluginConfigPath = null, IPluginLog? log = null, IClientState? clientState = null, IObjectTable? objectTable = null)
    {
        _log = log;
        _clientState = clientState!;
        _objectTable = objectTable!;
        var config = new ConfigurationService(vipDataPath);
        _vipService = new VipService(config);
        _autoPurge = new AutoPurgeService(_vipService);
        _pluginConfigService = new PluginConfigService(pluginConfigPath ?? string.Empty);
        _remote = new RemoteSyncService(_log);
        _remote.SnapshotReceived += OnSnapshotReceived;
        _remote.EntryAdded += OnEntryAdded;
        _remote.EntryRemoved += OnEntryRemoved;
        _remote.DjSnapshotReceived += OnDjSnapshotReceived;
        _remote.DjEntryAdded += OnDjEntryAdded;
        _remote.DjEntryRemoved += OnDjEntryRemoved;
        _remote.JobsListReceived += arr =>
        {
            var incoming = arr ?? Array.Empty<string>();
            var fp = string.Join("|", incoming);
            if (!string.Equals(_jobsFingerprint, fp, System.StringComparison.Ordinal))
            {
                _jobsFingerprint = fp;
                _jobsCache = incoming;
                JobsChanged?.Invoke(incoming);
            }
        };
        _remote.JobRightsReceived += dict => { _jobRightsCache = dict; JobRightsChanged?.Invoke(dict); };
        _remote.UsersListReceived += arr => { _usersCache = arr; };
        _remote.UsersDetailsReceived += det =>
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
        };
        _remote.UserJobUpdated += (username, job) => { if (!string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, username, System.StringComparison.Ordinal)) { _selfJob = job; if (_jobRightsCache != null && _jobRightsCache.TryGetValue(job, out var r)) { _selfRights = new System.Collections.Generic.Dictionary<string, bool>{{"addVip", r.AddVip},{"removeVip", r.RemoveVip},{"manageUsers", r.ManageUsers},{"manageJobs", r.ManageJobs}}; } } UserJobUpdatedEvt?.Invoke(username, job); };
        _remote.MembershipRemoved += (username, clubIdEvt) =>
        {
            var removedClub = clubIdEvt ?? string.Empty;
            var isSelf = !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(_staffUsername, username, System.StringComparison.Ordinal);
            var wasPresent = (_myClubs != null && System.Array.IndexOf(_myClubs, removedClub) >= 0) || (_myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, removedClub) >= 0);
            if (string.IsNullOrWhiteSpace(username) || isSelf || wasPresent)
            {
                if (!string.IsNullOrWhiteSpace(removedClub))
                {
                    if (_myClubs != null && _myClubs.Length > 0)
                    {
                        var list = new System.Collections.Generic.List<string>(_myClubs.Length);
                        foreach (var c in _myClubs) { if (!string.Equals(c, removedClub, System.StringComparison.Ordinal)) list.Add(c); }
                        _myClubs = list.ToArray();
                    }
                    if (_myCreatedClubs != null && _myCreatedClubs.Length > 0)
                    {
                        var list2 = new System.Collections.Generic.List<string>(_myCreatedClubs.Length);
                        foreach (var c in _myCreatedClubs) { if (!string.Equals(c, removedClub, System.StringComparison.Ordinal)) list2.Add(c); }
                        _myCreatedClubs = list2.ToArray();
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
                        if (!inClubs)
                        {
                            var next = (_myClubs != null && _myClubs.Length > 0) ? _myClubs[0] : null;
                            SetClubId(next);
                        }
                    }
                }
                catch { }
            });
        };
        _remote.MembershipAdded += (username, clubId) =>
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
                    try { Notification?.Invoke($"Joined venue: {clubId}"); } catch { }
                }
                var ct = _cts.Token;
                System.Threading.Tasks.Task.Run(async () => { try { if (!ct.IsCancellationRequested && !string.IsNullOrWhiteSpace(_staffToken)) _myClubs = await _remote.ListUserClubsAsync(_staffToken!); } catch { } });
            }
        };
        _remote.ClubLogoReceived += base64 => { _currentClubLogoBase64 = base64; try { ClubLogoChanged?.Invoke(base64); } catch { } if (!string.IsNullOrWhiteSpace(CurrentClubId)) _clubLogosByClub[CurrentClubId!] = base64; };
        _remote.ConnectionChanged += connected =>
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
                            if (!string.IsNullOrWhiteSpace(clubId)) { try { await _remote.SwitchClubAsync(clubId!); } catch { } }
                            try { _jobRightsCache = await _remote.ListJobRightsAsync(_staffToken!) ?? _jobRightsCache; } catch { }
                            try { _usersDetailsCache = await _remote.ListUsersDetailedAsync(_staffToken!); } catch { }
                            try
                            {
                                var logo = await _remote.GetClubLogoAsync(_staffToken!);
                                _currentClubLogoBase64 = logo;
                                try { ClubLogoChanged?.Invoke(logo); } catch { }
                                if (!string.IsNullOrWhiteSpace(CurrentClubId)) _clubLogosByClub[CurrentClubId] = logo;
                            }
                            catch { }
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
        };
        
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
        return _djEntries;
    }

    public async System.Threading.Tasks.Task<bool> AddDjAsync(string djName, string twitchLink)
    {
        var canManage = IsOwnerCurrentClub || (HasStaffSession && StaffCanManageUsers);
        if (!canManage) return false;
        var entry = new VenuePlus.State.DjEntry { DjName = djName ?? string.Empty, TwitchLink = twitchLink ?? string.Empty, CreatedAt = System.DateTimeOffset.UtcNow };
        var cur = new System.Collections.Generic.List<VenuePlus.State.DjEntry>(_djEntries);
        var idx = cur.FindIndex(x => string.Equals(x.DjName, entry.DjName, System.StringComparison.Ordinal));
        if (idx >= 0) cur[idx] = entry; else cur.Add(entry);
        _djEntries = cur.OrderBy(x => x.DjName, System.StringComparer.Ordinal).ToArray();
        if (HasStaffSession)
        {
            try { return await _remote.PublishAddDjAsync(entry, _staffToken!); } catch { return false; }
        }
        return true;
    }

    public async System.Threading.Tasks.Task<bool> RemoveDjAsync(string djName)
    {
        var canManage = IsOwnerCurrentClub || (HasStaffSession && StaffCanManageUsers);
        if (!canManage) return false;
        var cur = new System.Collections.Generic.List<VenuePlus.State.DjEntry>(_djEntries);
        cur.RemoveAll(x => string.Equals(x.DjName, djName ?? string.Empty, System.StringComparison.Ordinal));
        _djEntries = cur.OrderBy(x => x.DjName, System.StringComparer.Ordinal).ToArray();
        if (HasStaffSession)
        {
            var entry = new VenuePlus.State.DjEntry { DjName = djName ?? string.Empty, TwitchLink = string.Empty, CreatedAt = System.DateTimeOffset.UtcNow };
            try { return await _remote.PublishRemoveDjAsync(entry, _staffToken!); } catch { return false; }
        }
        return true;
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

    public bool ShowVipOverlay => _pluginConfigService.Current.ShowVipOverlay;
    public bool ShowVipNameplateHook => _pluginConfigService.Current.ShowVipNameplateHook;
    public ushort VipStarColorKey => _pluginConfigService.Current.VipStarColorKey;

    public System.Threading.Tasks.Task SetShowVipOverlayAsync(bool enable)
    {
        _pluginConfigService.Current.ShowVipOverlay = enable;
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

    public bool IsOwnerCurrentClub => _myCreatedClubs != null && System.Array.IndexOf(_myCreatedClubs, CurrentClubId) >= 0;
    private string? _currentClubLogoBase64;
    public string? CurrentClubLogoBase64 => _currentClubLogoBase64;
    private readonly System.Collections.Generic.Dictionary<string, string?> _clubLogosByClub = new(System.StringComparer.Ordinal);

    

    public async System.Threading.Tasks.Task<bool> StaffLoginAsync(string username, string password)
    {
        if (!_clientState.IsLoggedIn) return false;
        var usernameFinal = !string.IsNullOrWhiteSpace(username)
            ? username
            : ((!string.IsNullOrWhiteSpace(_currentCharName) && !string.IsNullOrWhiteSpace(_currentCharWorld)) ? (_currentCharName + "@" + _currentCharWorld) : string.Empty);
        if (string.IsNullOrWhiteSpace(usernameFinal)) return false;
        var token = await _remote.StaffLoginAsync(usernameFinal, password);
        if (string.IsNullOrWhiteSpace(token)) return false;
        _isPowerStaff = true;
        _staffToken = token;
        _staffUsername = usernameFinal;
        var keyAutoPre = GetCurrentCharacterKey();
        Configuration.CharacterProfile? profAutoPre = null;
        if (!string.IsNullOrWhiteSpace(keyAutoPre)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAutoPre, out profAutoPre);
        var clubPrefPre = (!string.IsNullOrWhiteSpace(keyAutoPre) && profAutoPre != null) ? profAutoPre.RemoteClubId : _pluginConfigService.Current.RemoteClubId;
        SetClubId(clubPrefPre);
        if (!RemoteConnected) await ConnectRemoteAsync();
        if (_remote.RemoteUseWebSocket && !string.IsNullOrWhiteSpace(CurrentClubId))
        {
            try { await _remote.SwitchClubAsync(CurrentClubId!); } catch { }
        }
        if (_remote.RemoteUseWebSocket && !string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var prof = await _remote.GetSelfProfileAsync(token);
                if (prof.HasValue && string.Equals(prof.Value.Username, _staffUsername, System.StringComparison.Ordinal))
                {
                    _selfUid = string.IsNullOrWhiteSpace(prof.Value.Uid) ? null : prof.Value.Uid;
                }
            }
            catch { }
        }
        var keyAuto = GetCurrentCharacterKey();
        Configuration.CharacterProfile? profAuto = null;
        if (!string.IsNullOrWhiteSpace(keyAuto)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAuto, out profAuto);
        var clubPref = (!string.IsNullOrWhiteSpace(keyAuto) && profAuto != null) ? profAuto.RemoteClubId : _pluginConfigService.Current.RemoteClubId;
        if (!string.IsNullOrWhiteSpace(clubPref)) SetClubId(clubPref);
        _accessLoading = true;
        var ct = _cts.Token;
        
        await System.Threading.Tasks.Task.Run(async () =>
        {
            if (ct.IsCancellationRequested) { _accessLoading = false; return; }
            try 
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
                    var prof = await _remote.GetSelfProfileAsync(_staffToken!);
                    if (prof.HasValue)
                    {
                        if (string.Equals(prof.Value.Username, _staffUsername, System.StringComparison.Ordinal))
                            _selfUid = string.IsNullOrWhiteSpace(prof.Value.Uid) ? null : prof.Value.Uid;
                    }
                }
                if (_remote.RemoteUseWebSocket)
                {
                    try { _jobRightsCache = await _remote.ListJobRightsAsync(_staffToken!) ?? _jobRightsCache; } catch { }
                    try { _usersDetailsCache = await _remote.ListUsersDetailedAsync(_staffToken!); } catch { }
                    EnsureSelfRights();
                }
                if (!ct.IsCancellationRequested) _myClubs = await _remote.ListUserClubsAsync(_staffToken!);
                if (!ct.IsCancellationRequested) _myCreatedClubs = await _remote.ListCreatedClubsAsync(_staffToken!);
                if (!ct.IsCancellationRequested)
                {
                    var logo = await _remote.GetClubLogoAsync(_staffToken!);
                    _currentClubLogoBase64 = logo;
                    try { ClubLogoChanged?.Invoke(logo); } catch { }
                    if (!string.IsNullOrWhiteSpace(CurrentClubId)) _clubLogosByClub[CurrentClubId] = logo;
                }
                var firstClub = (_myClubs != null && _myClubs.Length > 0) ? _myClubs[0] : ((_myCreatedClubs != null && _myCreatedClubs.Length > 0) ? _myCreatedClubs[0] : null);
                if (!string.IsNullOrWhiteSpace(firstClub)) SetClubId(firstClub!);
            } 
            catch { }
            finally { _accessLoading = false; }
        });
        var keySave = GetCurrentCharacterKey();
        Configuration.CharacterProfile? profSave = null;
        if (!string.IsNullOrWhiteSpace(keySave)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keySave, out profSave);
        var rememberThis = profSave?.RememberStaffLogin ?? _pluginConfigService.Current.RememberStaffLogin;
        if (rememberThis)
        {
            EnsureSecretsKey();
            var savedUser2 = profSave?.SavedStaffUsername ?? _pluginConfigService.Current.SavedStaffUsername;
            var savedEnc2 = profSave?.SavedStaffPasswordEnc ?? _pluginConfigService.Current.SavedStaffPasswordEnc;
            var existingPlain = (!string.IsNullOrWhiteSpace(savedEnc2) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
                ? Helpers.SecureStoreUtil.UnprotectToStringWithKey(savedEnc2!, _pluginConfigService.Current.SecretsKey!)
                : null;
            if (!string.Equals(savedUser2, usernameFinal, StringComparison.Ordinal) || !string.Equals(existingPlain, password, StringComparison.Ordinal))
            {
                var enc = Helpers.SecureStoreUtil.ProtectStringWithKey(password, _pluginConfigService.Current.SecretsKey!);
                if (!string.IsNullOrWhiteSpace(keySave))
                {
                    if (profSave == null)
                    {
                        profSave = new Configuration.CharacterProfile();
                        _pluginConfigService.Current.ProfilesByCharacter[keySave] = profSave;
                    }
                    profSave.SavedStaffUsername = usernameFinal;
                    profSave.SavedStaffPasswordEnc = enc;
                }
                else
                {
                    _pluginConfigService.Current.SavedStaffUsername = usernameFinal;
                    _pluginConfigService.Current.SavedStaffPasswordEnc = enc;
                }
                _pluginConfigService.Save();
            }
        }
        return true;
    }

    public async System.Threading.Tasks.Task<bool> StaffSetOwnPasswordAsync(string newPassword)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        return await _remote.StaffSetPasswordAsync(newPassword, _staffToken!);
    }

    public void StaffLogout()
    {
        var staff = _staffToken;
        if (!string.IsNullOrWhiteSpace(staff)) { _ = _remote.LogoutSessionAsync(staff); }
        var keyAuto = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(keyAuto))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAuto, out var profAuto))
            {
                profAuto = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[keyAuto] = profAuto;
            }
            profAuto.AutoLoginEnabled = false;
            profAuto.RememberStaffLogin = false;
            profAuto.SavedStaffUsername = null;
            profAuto.SavedStaffPasswordEnc = null;
        }
        _pluginConfigService.Current.AutoLoginEnabled = false;
        _pluginConfigService.Current.RememberStaffLogin = false;
        _pluginConfigService.Current.SavedStaffUsername = null;
        _pluginConfigService.Current.SavedStaffPasswordEnc = null;
        _pluginConfigService.Save();
        ClearAccessState();
    }

    public void LogoutAll()
    {
        var staff = _staffToken;
        if (!string.IsNullOrWhiteSpace(staff)) { _ = _remote.LogoutSessionAsync(staff); }
        var keyAuto = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(keyAuto))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAuto, out var profAuto))
            {
                profAuto = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[keyAuto] = profAuto;
            }
            profAuto.AutoLoginEnabled = false;
            profAuto.RememberStaffLogin = false;
            profAuto.SavedStaffUsername = null;
            profAuto.SavedStaffPasswordEnc = null;
        }
        _pluginConfigService.Current.AutoLoginEnabled = false;
        _pluginConfigService.Current.RememberStaffLogin = false;
        _pluginConfigService.Current.SavedStaffUsername = null;
        _pluginConfigService.Current.SavedStaffPasswordEnc = null;
        _pluginConfigService.Save();
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
        _djEntries = Array.Empty<VenuePlus.State.DjEntry>();
        
    }

    private void OnRemoteDisconnected()
    {
        ClearAccessState();
    }

    public System.Threading.Tasks.Task<bool> SetRememberStaffLoginAsync(bool remember)
    {
        var keyRem = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(keyRem))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyRem, out var profRem))
            {
                profRem = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[keyRem] = profRem;
            }
            profRem.RememberStaffLogin = remember;
            if (!remember)
            {
                profRem.SavedStaffUsername = null;
                profRem.SavedStaffPasswordEnc = null;
            }
        }
        _pluginConfigService.Current.RememberStaffLogin = remember;
        if (!remember)
        {
            _pluginConfigService.Current.SavedStaffUsername = null;
            _pluginConfigService.Current.SavedStaffPasswordEnc = null;
        }
        else
        {
            EnsureSecretsKey();
            if (_isPowerStaff && string.IsNullOrWhiteSpace(_pluginConfigService.Current.SavedStaffPasswordEnc))
            {
                try { RememberStaffNeedsPasswordEvt?.Invoke(); } catch { }
            }
        }
        _pluginConfigService.Save();
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<bool> SetAutoLoginEnabledAsync(bool enabled)
    {
        var keyAuto = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(keyAuto))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAuto, out var profAuto))
            {
                profAuto = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[keyAuto] = profAuto;
            }
            profAuto.AutoLoginEnabled = enabled;
        }
        _pluginConfigService.Current.AutoLoginEnabled = enabled;
        _pluginConfigService.Save();
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
            var connected = RemoteConnected || await ConnectRemoteAsync();
            if (!connected) { AutoLoginResultEvt?.Invoke(false, false); return; }
            if (HasStaffSession) { AutoLoginResultEvt?.Invoke(IsOwnerCurrentClub, true); return; }
            var staffOk = false;
            var keyAuto = GetCurrentCharacterKey();
            Configuration.CharacterProfile? profAuto = null;
            if (!string.IsNullOrWhiteSpace(keyAuto)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(keyAuto, out profAuto);
            var autoEnabled = profAuto?.AutoLoginEnabled ?? _pluginConfigService.Current.AutoLoginEnabled;
            var rememberEnabled = profAuto?.RememberStaffLogin ?? _pluginConfigService.Current.RememberStaffLogin;
            var savedUserAuto = profAuto?.SavedStaffUsername ?? _pluginConfigService.Current.SavedStaffUsername;
            var savedEncAuto = profAuto?.SavedStaffPasswordEnc ?? _pluginConfigService.Current.SavedStaffPasswordEnc;
            if (profAuto != null && string.IsNullOrWhiteSpace(savedUserAuto) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SavedStaffUsername))
            {
                profAuto.SavedStaffUsername = _pluginConfigService.Current.SavedStaffUsername;
                profAuto.SavedStaffPasswordEnc = _pluginConfigService.Current.SavedStaffPasswordEnc;
                if (!profAuto.RememberStaffLogin && _pluginConfigService.Current.RememberStaffLogin) profAuto.RememberStaffLogin = true;
                _pluginConfigService.Save();
                savedUserAuto = profAuto.SavedStaffUsername;
                savedEncAuto = profAuto.SavedStaffPasswordEnc;
                rememberEnabled = profAuto.RememberStaffLogin;
            }
            if (autoEnabled && rememberEnabled && !string.IsNullOrWhiteSpace(savedUserAuto) && !string.IsNullOrWhiteSpace(savedEncAuto) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
            {
                var pass = Helpers.SecureStoreUtil.UnprotectToStringWithKey(savedEncAuto!, _pluginConfigService.Current.SecretsKey!);
                if (!string.IsNullOrWhiteSpace(pass))
                {
                    try
                    {
                        var clubPref = (!string.IsNullOrWhiteSpace(keyAuto) && profAuto != null) ? profAuto.RemoteClubId : _pluginConfigService.Current.RemoteClubId;
                        _log?.Debug($"[AutoLogin] attempt club={clubPref ?? "default"}");
                    }
                    catch { }
                    var uname = (!string.IsNullOrWhiteSpace(_currentCharName) && !string.IsNullOrWhiteSpace(_currentCharWorld))
                        ? (_currentCharName + "@" + _currentCharWorld)
                        : savedUserAuto!;
                    staffOk = await StaffLoginAsync(uname, pass);
                    // Admin session acquisition is handled inside StaffLogin background task to avoid duplication
                }
            }
            AutoLoginResultEvt?.Invoke(false, staffOk);
            try { _log?.Debug($"[AutoLogin] result adminOk=false staffOk={staffOk}"); } catch { }
            _autoLoginAttempted = staffOk;
        }
        catch { }
        finally { _accessLoading = false; try { _autoLoginGate.Release(); } catch { } }
    }

    private void EnsureSecretsKey()
    {
        if (string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
        {
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            _pluginConfigService.Current.SecretsKey = Convert.ToBase64String(key);
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

    

    public void EnsureSelfRights()
    {
        if (!HasStaffSession) return;
        // WebSocket-only: derive rights from cached job rights whenever available.
        if (!string.IsNullOrWhiteSpace(_selfJob) && _jobRightsCache != null && _jobRightsCache.TryGetValue(_selfJob, out var r))
        {
            _selfRights = new System.Collections.Generic.Dictionary<string, bool>
            {
                ["addVip"] = r.AddVip,
                ["removeVip"] = r.RemoveVip,
                ["manageUsers"] = r.ManageUsers,
                ["manageJobs"] = r.ManageJobs,
                ["editVipDuration"] = r.EditVipDuration
            };
            return;
        }
        // If cache not yet filled, wait for WS broadcasts (jobs.rights/user.update) — no HTTP fallback.
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

    public async System.Threading.Tasks.Task<bool> UpdateJobRightsAsync(string name, bool addVip, bool removeVip, bool manageUsers, bool manageJobs, bool editVipDuration, string colorHex = "#FFFFFF", string iconKey = "User")
    {
        var sess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sess)) return false;
        var info = new JobRightsInfo { AddVip = addVip, RemoveVip = removeVip, ManageUsers = manageUsers, ManageJobs = manageJobs, EditVipDuration = editVipDuration, ColorHex = colorHex ?? "#FFFFFF", IconKey = iconKey ?? "User" };
        return await _remote.UpdateJobRightsAsync(name, info, sess);
    }

    public async System.Threading.Tasks.Task<bool> UpdateStaffUserJobAsync(string username, string job)
    {
        var staffSess = _staffToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(staffSess)) return false;
        return await _remote.UpdateUserJobAsync(username, job, staffSess);
    }

    public async System.Threading.Tasks.Task<bool> DeleteStaffUserAsync(string username)
    {
        var staffSess = _staffToken ?? string.Empty;
        if (IsOwnerCurrentClub && !string.IsNullOrWhiteSpace(_staffUsername) && string.Equals(username, _staffUsername, System.StringComparison.Ordinal)) return false;
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

    private void OnSnapshotReceived(System.Collections.Generic.IReadOnlyCollection<VipEntry> entries)
    {
        _vipService.ReplaceAllForActiveClub(entries);
    }

    private void OnEntryAdded(VipEntry entry)
    {
        _vipService.SetFromRemote(entry);
    }

    private void OnEntryRemoved(VipEntry entry)
    {
        _vipService.Remove(entry.CharacterName, entry.HomeWorld);
    }

    private void OnDjSnapshotReceived(VenuePlus.State.DjEntry[]? entries)
    {
        _djEntries = (entries ?? Array.Empty<VenuePlus.State.DjEntry>()).OrderBy(e => e.DjName, System.StringComparer.Ordinal).ToArray();
    }

    private void OnDjEntryAdded(VenuePlus.State.DjEntry entry)
    {
        var list = new System.Collections.Generic.List<VenuePlus.State.DjEntry>(_djEntries);
        var idx = list.FindIndex(x => string.Equals(x.DjName, entry.DjName, System.StringComparison.Ordinal));
        if (idx >= 0) list[idx] = entry; else list.Add(entry);
        _djEntries = list.OrderBy(x => x.DjName, System.StringComparer.Ordinal).ToArray();
    }

    private void OnDjEntryRemoved(VenuePlus.State.DjEntry entry)
    {
        _djEntries = _djEntries.Where(x => !string.Equals(x.DjName, entry.DjName, System.StringComparison.Ordinal)).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isDisposing = true;
        try { _cts.Cancel(); } catch { }
        try { _autoLoginMonitorCts?.Cancel(); } catch { }
        try { _autoPurge.Dispose(); } catch { }
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
            var key = GetCurrentCharacterKey();
            if (!string.IsNullOrWhiteSpace(key) && _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out var prof))
            {
                if (string.IsNullOrWhiteSpace(prof.SavedStaffUsername) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SavedStaffUsername))
                {
                    prof.SavedStaffUsername = _pluginConfigService.Current.SavedStaffUsername;
                    prof.SavedStaffPasswordEnc = _pluginConfigService.Current.SavedStaffPasswordEnc;
                    if (!prof.RememberStaffLogin && _pluginConfigService.Current.RememberStaffLogin)
                    {
                        prof.RememberStaffLogin = true;
                    }
                    _pluginConfigService.Save();
                }
            }
        });
    }

    public void SetClubId(string? clubId)
    {
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
                            _currentClubLogoBase64 = logo;
                            try { ClubLogoChanged?.Invoke(logo); } catch { }
                            _clubLogosByClub[clubId] = logo;
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
        if (_myCreatedClubs != null && _myCreatedClubs.Length > 0)
        {
            var listCreated = new System.Collections.Generic.List<string>(_myCreatedClubs.Length);
            foreach (var c in _myCreatedClubs) { if (!string.Equals(c, clubId, System.StringComparison.Ordinal)) listCreated.Add(c); }
            _myCreatedClubs = listCreated.ToArray();
        }
        if (_myClubs != null && _myClubs.Length > 0)
        {
            var listClubs = new System.Collections.Generic.List<string>(_myClubs.Length);
            foreach (var c in _myClubs) { if (!string.Equals(c, clubId, System.StringComparison.Ordinal)) listClubs.Add(c); }
            _myClubs = listClubs.ToArray();
        }
        if (!string.IsNullOrWhiteSpace(clubId))
        {
            _clubLogosByClub.Remove(clubId);
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
        if (!IsOwnerCurrentClub) return false;
        return await _remote.InviteStaffByUidAsync(uid, job, _staffToken!);
    }

    public async System.Threading.Tasks.Task<bool> UpdateClubLogoBase64Async(string logoBase64)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return false;
        if (!IsOwnerCurrentClub) return false;
        var ok = await _remote.UpdateClubLogoAsync(logoBase64 ?? string.Empty, _staffToken!);
        if (ok)
        {
            _currentClubLogoBase64 = logoBase64;
            try { ClubLogoChanged?.Invoke(logoBase64); } catch { }
            if (!string.IsNullOrWhiteSpace(CurrentClubId)) _clubLogosByClub[CurrentClubId] = logoBase64;
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
            _currentClubLogoBase64 = null;
            try { ClubLogoChanged?.Invoke(null); } catch { }
            if (!string.IsNullOrWhiteSpace(CurrentClubId)) _clubLogosByClub[CurrentClubId] = null;
        }
        return ok;
    }

    public string? GetClubLogoForClub(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return null;
        return _clubLogosByClub.TryGetValue(clubId, out var v) ? v : null;
    }

    public async System.Threading.Tasks.Task<string?> FetchClubLogoForClubAsync(string clubId)
    {
        if (!_isPowerStaff || string.IsNullOrWhiteSpace(_staffToken)) return null;
        var logo = await _remote.GetClubLogoForAsync(_staffToken!, clubId);
        if (!string.IsNullOrWhiteSpace(logo)) _clubLogosByClub[clubId] = logo!;
        else _clubLogosByClub[clubId] = null;
        return logo;
    }
}
