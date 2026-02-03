using System;
using VenuePlus.Configuration;
using VenuePlus.Helpers;
using VenuePlus.State;
using VenuePlus.Services;

namespace VenuePlus.Services;

internal sealed class AccessService
{
    private readonly PluginConfigService _pluginConfigService;
    private RemoteSyncService? _remote;

    public AccessService(PluginConfigService pluginConfigService)
    {
        _pluginConfigService = pluginConfigService;
    }

    public void SetRemote(RemoteSyncService remote)
    {
        _remote = remote;
    }

    public void EnsureSecretsKey()
    {
        if (string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
        {
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            _pluginConfigService.Current.SecretsKey = Convert.ToBase64String(key);
        }
    }

    public (string key, Configuration.CharacterProfile? profile) TryGetCurrentProfile(string currentCharacterKey)
    {
        var key = currentCharacterKey;
        Configuration.CharacterProfile? prof = null;
        if (!string.IsNullOrWhiteSpace(key)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out prof);
        return (key, prof);
    }

    public (string key, Configuration.CharacterProfile? profile) GetOrCreateCurrentProfile(string currentCharacterKey)
    {
        var key = currentCharacterKey;
        Configuration.CharacterProfile? prof = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (!_pluginConfigService.Current.ProfilesByCharacter.TryGetValue(key, out prof))
            {
                prof = new Configuration.CharacterProfile();
                _pluginConfigService.Current.ProfilesByCharacter[key] = prof;
            }
        }
        return (key, prof);
    }

    public void EnsureProfileHasSavedCredentials(string currentCharacterKey)
    {
        var (key, prof) = TryGetCurrentProfile(currentCharacterKey);
        if (prof != null && string.IsNullOrWhiteSpace(prof.SavedStaffUsername) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SavedStaffUsername))
        {
            prof.SavedStaffUsername = _pluginConfigService.Current.SavedStaffUsername;
            prof.SavedStaffPasswordEnc = _pluginConfigService.Current.SavedStaffPasswordEnc;
            if (!prof.RememberStaffLogin && _pluginConfigService.Current.RememberStaffLogin) prof.RememberStaffLogin = true;
            _pluginConfigService.Save();
        }
    }

    public void PersistSavedStaffCredentials(string currentCharacterKey, string username, string password)
    {
        EnsureSecretsKey();
        var (key, prof) = GetOrCreateCurrentProfile(currentCharacterKey);
        var existingEnc = prof?.SavedStaffPasswordEnc ?? _pluginConfigService.Current.SavedStaffPasswordEnc;
        var existingPlain = (!string.IsNullOrWhiteSpace(existingEnc) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
            ? SecureStoreUtil.UnprotectToStringWithKey(existingEnc!, _pluginConfigService.Current.SecretsKey!)
            : null;
        var existingUser = (prof?.SavedStaffUsername ?? _pluginConfigService.Current.SavedStaffUsername) ?? string.Empty;
        if (!string.Equals(existingUser, username, StringComparison.Ordinal) || !string.Equals(existingPlain ?? string.Empty, password, StringComparison.Ordinal))
        {
            var enc = SecureStoreUtil.ProtectStringWithKey(password, _pluginConfigService.Current.SecretsKey!);
            if (!string.IsNullOrWhiteSpace(key) && prof != null)
            {
                prof.SavedStaffUsername = username;
                prof.SavedStaffPasswordEnc = enc;
            }
            else
            {
                _pluginConfigService.Current.SavedStaffUsername = username;
                _pluginConfigService.Current.SavedStaffPasswordEnc = enc;
            }
            _pluginConfigService.Save();
        }
    }

    public void ResetSavedLoginData(string currentCharacterKey)
    {
        var (key, prof) = GetOrCreateCurrentProfile(currentCharacterKey);
        if (!string.IsNullOrWhiteSpace(key) && prof != null)
        {
            prof.AutoLoginEnabled = false;
            prof.RememberStaffLogin = false;
            prof.SavedStaffUsername = null;
            prof.SavedStaffPasswordEnc = null;
        }
        _pluginConfigService.Current.AutoLoginEnabled = false;
        _pluginConfigService.Current.RememberStaffLogin = false;
        _pluginConfigService.Current.SavedStaffUsername = null;
        _pluginConfigService.Current.SavedStaffPasswordEnc = null;
        _pluginConfigService.Save();
    }

    public void LogoutStaffAndReset(string currentCharacterKey, string? staffToken)
    {
        var token = staffToken ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try { _ = _remote?.LogoutSessionAsync(token); } catch { }
        }
        ResetSavedLoginData(currentCharacterKey);
    }

    public void LogoutAllAndReset(string currentCharacterKey, string? staffToken)
    {
        var token = staffToken ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try { _ = _remote?.LogoutSessionAsync(token); } catch { }
        }
        ResetSavedLoginData(currentCharacterKey);
    }

    public async System.Threading.Tasks.Task<bool> StaffSetOwnPasswordAsync(string newPassword, string staffToken)
    {
        if (_remote is null) return false;
        if (string.IsNullOrWhiteSpace(staffToken)) return false;
        return await _remote.StaffSetPasswordAsync(newPassword, staffToken);
    }

    public async System.Threading.Tasks.Task<string?> GenerateRecoveryCodeAsync(string staffToken)
    {
        if (_remote is null) return null;
        if (string.IsNullOrWhiteSpace(staffToken)) return null;
        return await _remote.GenerateRecoveryCodeAsync(staffToken);
    }

    public async System.Threading.Tasks.Task<bool> ResetPasswordByRecoveryCodeAsync(string username, string recoveryCode, string newPassword)
    {
        if (_remote is null) return false;
        return await _remote.ResetPasswordByRecoveryCodeAsync(username, recoveryCode, newPassword);
    }

    public bool SetRememberStaffLogin(string currentCharacterKey, bool remember, bool isPowerStaff)
    {
        var keyRem = currentCharacterKey;
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
        }
        _pluginConfigService.Save();
        return remember && isPowerStaff && string.IsNullOrWhiteSpace(_pluginConfigService.Current.SavedStaffPasswordEnc);
    }

    public void SetAutoLoginEnabled(string currentCharacterKey, bool enabled)
    {
        var keyAuto = currentCharacterKey;
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
    }
    public sealed class AutoLoginInfo
    {
        public bool Enabled { get; init; }
        public bool Remembered { get; init; }
        public string? SavedUsername { get; init; }
        public string? DecryptedPassword { get; init; }
        public string? PreferredClubId { get; init; }
    }

    public AutoLoginInfo GetAutoLoginInfo(string currentCharacterKey)
    {
        EnsureProfileHasSavedCredentials(currentCharacterKey);
        Configuration.CharacterProfile? prof = null;
        if (!string.IsNullOrWhiteSpace(currentCharacterKey))
            _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(currentCharacterKey, out prof);
        var enabled = prof?.AutoLoginEnabled ?? _pluginConfigService.Current.AutoLoginEnabled;
        var remember = prof?.RememberStaffLogin ?? _pluginConfigService.Current.RememberStaffLogin;
        var savedUser = prof?.SavedStaffUsername ?? _pluginConfigService.Current.SavedStaffUsername;
        var savedEnc = prof?.SavedStaffPasswordEnc ?? _pluginConfigService.Current.SavedStaffPasswordEnc;
        string? pass = null;
        if (!string.IsNullOrWhiteSpace(savedEnc) && !string.IsNullOrWhiteSpace(_pluginConfigService.Current.SecretsKey))
        {
            pass = SecureStoreUtil.UnprotectToStringWithKey(savedEnc!, _pluginConfigService.Current.SecretsKey!);
        }
        var clubPref = (!string.IsNullOrWhiteSpace(currentCharacterKey) && prof != null) ? prof.RemoteClubId : _pluginConfigService.Current.RemoteClubId;
        return new AutoLoginInfo
        {
            Enabled = enabled,
            Remembered = remember,
            SavedUsername = savedUser,
            DecryptedPassword = pass,
            PreferredClubId = clubPref
        };
    }

    public sealed class StaffLoginResult
    {
        public string Token { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string? SelfUid { get; init; }
        public DateTimeOffset? SelfBirthday { get; init; }
        public bool IsServerAdmin { get; init; }
        public string[]? MyClubs { get; init; }
        public string[]? MyCreatedClubs { get; init; }
        public System.Collections.Generic.Dictionary<string, JobRightsInfo>? JobRightsCache { get; init; }
        public VenuePlus.State.StaffUser[]? UsersDetailsCache { get; init; }
        public string? CurrentClubLogoBase64 { get; init; }
        public string? PreferredClubId { get; init; }
        public string? FirstClubCandidate { get; init; }
        public string? SelfJob { get; init; }
        public System.Collections.Generic.Dictionary<string, bool>? SelfRights { get; init; }
        public bool RemoteUseWebSocket { get; init; }
    }

    public async System.Threading.Tasks.Task<StaffLoginResult?> StaffLoginAsync(string usernameFinal, string password, string currentCharacterKey, string? currentClubId, bool fastMode = false)
    {
        if (_remote is null) return null;
        var token = await _remote.StaffLoginAsync(usernameFinal, password);
        if (string.IsNullOrWhiteSpace(token)) return null;
        Configuration.CharacterProfile? profAuto = null;
        if (!string.IsNullOrWhiteSpace(currentCharacterKey)) _pluginConfigService.Current.ProfilesByCharacter.TryGetValue(currentCharacterKey, out profAuto);
        var clubPref = (!string.IsNullOrWhiteSpace(currentCharacterKey) && profAuto != null) ? profAuto.RemoteClubId : _pluginConfigService.Current.RemoteClubId;
        var useWs = _remote.RemoteUseWebSocket;
        if (fastMode && useWs)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(currentClubId))
                {
                    try { await _remote.SwitchClubAsync(currentClubId!); } catch { }
                    try { await _remote.RequestShiftSnapshotAsync(token); } catch { }
                }
            }
            catch { }
            return new StaffLoginResult
            {
                Token = token,
                Username = usernameFinal,
                PreferredClubId = clubPref,
                RemoteUseWebSocket = useWs
            };
        }
        string? selfUid = null;
        DateTimeOffset? selfBirthday = null;
        string[]? myClubs = null;
        string[]? myCreated = null;
        System.Collections.Generic.Dictionary<string, JobRightsInfo>? jobRights = null;
        VenuePlus.State.StaffUser[]? usersDet = null;
        string? logo = null;
        string? selfJob = null;
        System.Collections.Generic.Dictionary<string, bool>? selfRights = null;
        var isServerAdmin = false;
        try
        {
            if (useWs && !string.IsNullOrWhiteSpace(currentClubId))
            {
                try { await _remote.SwitchClubAsync(currentClubId!); } catch { }
                try { await _remote.RequestShiftSnapshotAsync(token); } catch { }
            }

            if (useWs)
            {
                var tRights = _remote.GetSelfRightsAsync(token);
                var tProfile = _remote.GetSelfProfileAsync(token);
                var tBirthday = _remote.GetSelfBirthdayAsync(token);
                var tJobRights = _remote.ListJobRightsAsync(token);
                var tUsersDet = _remote.ListUsersDetailedAsync(token);
                var tMyClubs = _remote.ListUserClubsAsync(token);
                var tMyCreated = _remote.ListCreatedClubsAsync(token);
                try { await System.Threading.Tasks.Task.WhenAll(new System.Threading.Tasks.Task[] { tRights, tProfile, tBirthday, tJobRights, tUsersDet, tMyClubs, tMyCreated }); } catch { }
                try
                {
                    var r = tRights.IsCompleted ? tRights.Result : (System.ValueTuple<string, System.Collections.Generic.Dictionary<string, bool>>?)null;
                    if (r.HasValue)
                    {
                        selfJob = r.Value.Item1;
                        selfRights = r.Value.Item2 ?? new System.Collections.Generic.Dictionary<string, bool>();
                    }
                }
                catch { }
                try
                {
                    var p = tProfile.IsCompleted ? tProfile.Result : (System.ValueTuple<string, string, bool>?)null;
                    if (p.HasValue && string.Equals(p.Value.Item1, usernameFinal, StringComparison.Ordinal))
                    {
                        selfUid = string.IsNullOrWhiteSpace(p.Value.Item2) ? null : p.Value.Item2;
                        isServerAdmin = p.Value.Item3;
                    }
                }
                catch { }
                try { selfBirthday = tBirthday.IsCompleted ? tBirthday.Result : selfBirthday; } catch { }
                try { jobRights = tJobRights.IsCompleted ? (tJobRights.Result ?? jobRights) : jobRights; } catch { }
                try { usersDet = tUsersDet.IsCompleted ? tUsersDet.Result : usersDet; } catch { }
                try { myClubs = tMyClubs.IsCompleted ? tMyClubs.Result : myClubs; } catch { }
                try { myCreated = tMyCreated.IsCompleted ? tMyCreated.Result : myCreated; } catch { }
            }
            else
            {
                try
                {
                    var rights = await _remote.GetSelfRightsAsync(token);
                    if (rights.HasValue)
                    {
                        selfJob = rights.Value.Job;
                        selfRights = rights.Value.Rights ?? new System.Collections.Generic.Dictionary<string, bool>();
                    }
                }
                catch { }
                try
                {
                    var prof = await _remote.GetSelfProfileAsync(token);
                    if (prof.HasValue && string.Equals(prof.Value.Username, usernameFinal, StringComparison.Ordinal))
                    {
                        selfUid = string.IsNullOrWhiteSpace(prof.Value.Uid) ? null : prof.Value.Uid;
                        isServerAdmin = prof.Value.IsServerAdmin;
                    }
                }
                catch { }
            }
            try { myClubs ??= await _remote.ListUserClubsAsync(token); } catch { }
            try { myCreated ??= await _remote.ListCreatedClubsAsync(token); } catch { }
            try { logo = await _remote.GetClubLogoAsync(token); } catch { }
        }
        catch { }
        var firstClub = (myClubs != null && myClubs.Length > 0) ? myClubs[0] : ((myCreated != null && myCreated.Length > 0) ? myCreated[0] : null);
        return new StaffLoginResult
        {
            Token = token,
            Username = usernameFinal,
            SelfUid = selfUid,
            SelfBirthday = selfBirthday,
            IsServerAdmin = isServerAdmin,
            MyClubs = myClubs,
            MyCreatedClubs = myCreated,
            JobRightsCache = jobRights,
            UsersDetailsCache = usersDet,
            CurrentClubLogoBase64 = logo,
            PreferredClubId = clubPref,
            FirstClubCandidate = firstClub,
            SelfJob = selfJob,
            SelfRights = selfRights,
            RemoteUseWebSocket = useWs
        };
    }
}
