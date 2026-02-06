using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VenuePlus.State;
using VenuePlus.Helpers;
using Dalamud.Plugin.Services;

namespace VenuePlus.Services;

public sealed class RemoteSyncService : IDisposable
{
    private bool _disposed;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private Task? _wsTask;
    private string? _baseUrl;
    private const string DefaultBaseUrl = "http://192.168.178.46:8080";
    public bool IsConnected { get; private set; }
    private readonly IPluginLog? _log;
    private string? _clubId;
    private bool _useWebSocket;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private int _reconnectDelayMs = 1000;
    private int _reconnectAttempts;
    private int _reconnectMaxAttempts = 12;
    private DateTime _lastWsReceiveErrorAt = DateTime.MinValue;
    private bool _allowReconnect = true;
    public event Action<bool>? ConnectionChanged;
    public bool RemoteUseWebSocket => _useWebSocket;

    public event Action<IReadOnlyCollection<VipEntry>>? SnapshotReceived;
    public event Action<VipEntry>? EntryAdded;
    public event Action<VipEntry>? EntryRemoved;
    public event Action<string[]>? JobsListReceived;
    public event Action<System.Collections.Generic.Dictionary<string, JobRightsInfo>>? JobRightsReceived;
    public event Action<string[]>? UsersListReceived;
    public event Action<VenuePlus.State.StaffUser[]>? UsersDetailsReceived;
    public event Action<string, string, string[]>? UserJobUpdated;
    public event Action<string, string>? MembershipRemoved;
    public event Action<string, string>? MembershipAdded;
    public event Action<string, string>? OwnerAccessChanged;
    public event Action<string?>? ClubLogoReceived;
    public event Action<VenuePlus.State.DjEntry[]>? DjSnapshotReceived;
    public event Action<VenuePlus.State.DjEntry>? DjEntryAdded;
    public event Action<VenuePlus.State.DjEntry>? DjEntryRemoved;
    public event Action<VenuePlus.State.ShiftEntry[]>? ShiftSnapshotReceived;
    public event Action<VenuePlus.State.ShiftEntry>? ShiftEntryAdded;
    public event Action<VenuePlus.State.ShiftEntry>? ShiftEntryUpdated;
    public event Action<Guid>? ShiftEntryRemoved;
    public event Action<string>? ServerAnnouncementReceived;

    private TaskCompletionSource<string[]?>? _pendingUserClubsTcs;
    private TaskCompletionSource<string[]?>? _pendingCreatedClubsTcs;
    private TaskCompletionSource<bool>? _pendingDeleteClubTcs;
    private TaskCompletionSource<string[]?>? _pendingUsersListTcs;
    private TaskCompletionSource<VenuePlus.State.StaffUser[]?>? _pendingUsersDetailsTcs;
    private TaskCompletionSource<string[]?>? _pendingJobsListTcs;
    private TaskCompletionSource<bool>? _pendingUpdateJobRightsTcs;
    private TaskCompletionSource<bool>? _pendingAddJobTcs;
    private TaskCompletionSource<bool>? _pendingDeleteJobTcs;
    private TaskCompletionSource<string?>? _pendingClubLogoTcs;
    private TaskCompletionSource<bool>? _pendingClubLogoUpdateTcs;
    private TaskCompletionSource<bool>? _pendingClubLogoDeleteTcs;
    private TaskCompletionSource<bool>? _pendingVipUpdateTcs;
    private TaskCompletionSource<bool>? _pendingVipPurgeTcs;
    private TaskCompletionSource<string?>? _pendingRegenerateAccessKeyTcs;
    private TaskCompletionSource<bool>? _pendingRegisterClubTcs;
    private TaskCompletionSource<bool>? _pendingJoinClubTcs;
    private TaskCompletionSource<bool>? _pendingCreateUserTcs;
    private TaskCompletionSource<bool>? _pendingDeleteUserTcs;
    private TaskCompletionSource<bool>? _pendingUpdateUserJobTcs;
    private TaskCompletionSource<bool>? _pendingUpdateUserBirthdayTcs;
    private TaskCompletionSource<bool>? _pendingSetJoinPasswordTcs;
    private TaskCompletionSource<bool>? _pendingInviteStaffTcs;
    private TaskCompletionSource<bool>? _pendingManualStaffAddTcs;
    private TaskCompletionSource<bool>? _pendingManualStaffLinkTcs;
    private TaskCompletionSource<string?>? _pendingAccessKeyTcs;
    private TaskCompletionSource<bool>? _pendingDjUpdateTcs;
    private TaskCompletionSource<bool>? _pendingServerAnnouncementTcs;
    private TaskCompletionSource<bool>? _pendingServerShutdownTcs;
    private TaskCompletionSource<bool>? _pendingServerRestartTcs;
    private TaskCompletionSource<(bool Active, bool Pending)?>? _pendingMaintenanceStatusTcs;
    private TaskCompletionSource<bool>? _pendingMaintenanceSetTcs;
    
    private TaskCompletionSource<string?>? _pendingRegisterUserTcs;
    private TaskCompletionSource<bool>? _pendingLogoutTcs;
    private TaskCompletionSource<bool>? _pendingSelfPasswordTcs;
    private TaskCompletionSource<string?>? _pendingGenerateRecoveryCodeTcs;
    private TaskCompletionSource<bool>? _pendingResetRecoveryPasswordTcs;
    private TaskCompletionSource<System.Collections.Generic.Dictionary<string, JobRightsInfo>?>? _pendingJobRightsTcs;
    private TaskCompletionSource<(string Job, System.Collections.Generic.Dictionary<string, bool>)?>? _pendingSelfRightsTcs;
    private TaskCompletionSource<(string Username, string Uid, bool IsServerAdmin)?>? _pendingSelfProfileTcs;
    private TaskCompletionSource<DateTimeOffset?>? _pendingSelfBirthdayTcs;
    private TaskCompletionSource<bool>? _pendingSelfBirthdaySetTcs;
    private TaskCompletionSource<bool?>? _pendingUserExistsTcs;
    private TaskCompletionSource<string?>? _pendingLoginTcs;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingClubLogoForByClub = new(System.StringComparer.Ordinal);
    private TaskCompletionSource<bool>? _pendingShiftUpdateTcs;

    public RemoteSyncService(IPluginLog? log = null)
    {
        _log = log;
        _baseUrl = DefaultBaseUrl;
    }

    public string GetBaseUrl() => _baseUrl ?? DefaultBaseUrl;

    public void SetClubId(string? clubId)
    {
        _clubId = string.IsNullOrWhiteSpace(clubId) ? null : clubId.Trim();
        try { _log?.Debug($"WS clubId set to {(_clubId ?? "--")}"); } catch { }
    }

    

    public async Task<bool> SwitchClubAsync(string clubId)
    {
        SetClubId(clubId);
        if (_clubId == null) return false;
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                    Logger.LogDebug($"[VipListSync] ws.switch.club send club={_clubId}");
                var payload = JsonSerializer.Serialize(new { type = "switch.club", clubId = _clubId });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                    Logger.LogDebug($"[VipListSync] ws.switch.club sent club={_clubId}");
                return true;
            }
            catch (Exception ex) { _log?.Debug($"WS switch.club async failed: {ex.Message}"); return false; }
        }
        return false;
    }

    public async Task<bool> RequestShiftSnapshotAsync(string? staffSession = null)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                object payloadObj = string.IsNullOrWhiteSpace(staffSession)
                    ? new { type = "shift.snapshot.request" }
                    : new { type = "shift.snapshot.request", token = staffSession };
                var payload = JsonSerializer.Serialize(payloadObj);
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                return true;
            }
            catch (Exception ex) { _log?.Debug($"WS snapshot request failed: {ex.Message}"); return false; }
        }
        return false;
    }

    public async Task<bool> ConnectAsync(string baseUrl)
    {
        _baseUrl = baseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(_baseUrl)) return false;
        _useWebSocket = true;
        _allowReconnect = true;

        _log?.Debug($"Remote connect start baseUrl={_baseUrl}");
        

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _wsCts = new CancellationTokenSource();
        var baseWs = (_baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "wss://" + _baseUrl.Substring(8)
            : _baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? "ws://" + _baseUrl.Substring(7)
                : _baseUrl) + "/ws";
        var wsUrl = string.IsNullOrWhiteSpace(_clubId) ? baseWs : baseWs + "?clubId=" + Uri.EscapeDataString(_clubId!);

        try
        {
            _log?.Debug($"WS connect url={wsUrl}");
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_wsCts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(6));
            await _ws.ConnectAsync(new Uri(wsUrl), connectCts.Token);
            _log?.Debug($"WS connected state={_ws.State}");
        }
        catch (Exception ex)
        {
            _log?.Debug($"WS connect failed: {ex.Message}");
            IsConnected = false;
            StartReconnectLoop();
            ConnectionChanged?.Invoke(false);
            return false;
        }

        _wsTask = Task.Run(() => ReceiveLoopAsync(_ws, _wsCts.Token));
        IsConnected = true;
        _reconnectAttempts = 0;
        StopReconnect();
        StartHealthLoop();
        ConnectionChanged?.Invoke(true);
        return true;
    }


    public async Task<bool> LogoutSessionAsync(string token)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingLogoutTcs = tcs;
                var payloadWs = JsonSerializer.Serialize(new { type = "session.logout", token });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payloadWs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingLogoutTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS session logout failed: {ex.Message}"); _pendingLogoutTcs = null; return false; }
        }
        return false;
    }

    public async Task DisconnectAsync()
    {
        _allowReconnect = false;
        if (_wsCts != null)
        {
            _wsCts.Cancel();
        }
        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
        }
        _ws?.Dispose();
        _ws = null;
        _wsTask = null;
        _wsCts?.Dispose();
        _wsCts = null;
        IsConnected = false;
        StopReconnect();
        StopHealthLoop();
        ConnectionChanged?.Invoke(false);
    }

    public Task<bool> PublishAddAsync(VipEntry entry, string publicKey, byte[] signature)
    {
        return Task.FromResult(false);
    }

    public async Task<bool> PublishAddWithSessionAsync(VipEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingVipUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "vip.add", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingVipUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish add failed: {ex.Message}"); _pendingVipUpdateTcs = null; return false; }
        }
        return false;
    }

    public Task<bool> PublishRemoveAsync(VipEntry entry, string publicKey, byte[] signature)
    {
        return Task.FromResult(false);
    }

    public async Task<bool> PublishRemoveWithSessionAsync(VipEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingVipUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "vip.remove", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingVipUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish remove failed: {ex.Message}"); _pendingVipUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PurgeExpiredVipAsync(string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingVipPurgeTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "vip.purge.expired", token = session });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingVipPurgeTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS purge expired VIPs failed: {ex.Message}"); _pendingVipPurgeTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PublishAddDjAsync(VenuePlus.State.DjEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingDjUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "dj.add", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingDjUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish DJ add failed: {ex.Message}"); _pendingDjUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PublishAddShiftAsync(VenuePlus.State.ShiftEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingShiftUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "shift.add", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingShiftUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish shift add failed: {ex.Message}"); _pendingShiftUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PublishUpdateShiftAsync(VenuePlus.State.ShiftEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingShiftUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "shift.update", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingShiftUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish shift update failed: {ex.Message}"); _pendingShiftUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PublishRemoveShiftAsync(Guid id, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingShiftUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "shift.remove", id = id.ToString(), token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingShiftUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish shift remove failed: {ex.Message}"); _pendingShiftUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> PublishRemoveDjAsync(VenuePlus.State.DjEntry entry, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingDjUpdateTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "dj.remove", entry, token = session }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingDjUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS publish DJ remove failed: {ex.Message}"); _pendingDjUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<string?> StaffLoginAsync(string username, string password)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingLoginTcs = tcs;
                var msgLogin = JsonSerializer.Serialize(new { type = "login.request", username, password });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgLogin));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var token = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingLoginTcs = null;
                return token;
            }
            catch (Exception ex) { _log?.Debug($"WS login failed: {ex.Message}"); _pendingLoginTcs = null; }
        }
        return null;
    }

    public async Task<bool> StaffSetPasswordAsync(string newPassword, string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingSelfPasswordTcs = tcs;
                var payloadWs = JsonSerializer.Serialize(new { type = "user.self.password.set", newPassword, token = session });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payloadWs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingSelfPasswordTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS self password set failed: {ex.Message}"); _pendingSelfPasswordTcs = null; return false; }
        }
        return false;
    }

    public async Task<string?> GenerateRecoveryCodeAsync(string session)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingGenerateRecoveryCodeTcs = tcs;
                var payloadWs = JsonSerializer.Serialize(new { type = "user.recovery.generate.request", token = session });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payloadWs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var code = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingGenerateRecoveryCodeTcs = null;
                return code;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS generate recovery code failed: {ex.Message}");
                _pendingGenerateRecoveryCodeTcs = null;
                return null;
            }
        }
        return null;
    }

    public async Task<bool> ResetPasswordByRecoveryCodeAsync(string username, string recoveryCode, string newPassword)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingResetRecoveryPasswordTcs = tcs;
                var payloadWs = JsonSerializer.Serialize(new { type = "user.password.reset.recovery.request", username, recoveryCode, newPassword });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payloadWs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingResetRecoveryPasswordTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS reset password failed: {ex.Message}");
                _pendingResetRecoveryPasswordTcs = null;
                return false;
            }
        }
        return false;
    }


    public async Task<bool> CreateUserAsync(string username, string password, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingCreateUserTcs = tcs;
                var msgCreate = JsonSerializer.Serialize(new { type = "user.create", token = staffSession, username, password });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgCreate));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingCreateUserTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS create user failed: {ex.Message}");
                _pendingCreateUserTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<string?> RegisterAsync(string characterName, string homeWorld, string password)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingRegisterUserTcs = tcs;
                var payloadWs = JsonSerializer.Serialize(new { type = "register.request", characterName, homeWorld, password });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payloadWs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var recoveryCode = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingRegisterUserTcs = null;
                return string.IsNullOrWhiteSpace(recoveryCode) ? null : recoveryCode;
            }
            catch (Exception ex) { _log?.Debug($"WS register failed: {ex.Message}"); _pendingRegisterUserTcs = null; return null; }
        }
        return null;
    }

    public async Task<bool> RegisterClubAsync(string clubId, string? adminPin = null, string? defaultStaffPassword = null, string? staffSession = null, string? creatorUsername = null)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingRegisterClubTcs = tcs;
                var payloadObj = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "club.register",
                    ["clubId"] = clubId
                };
                if (!string.IsNullOrWhiteSpace(defaultStaffPassword)) payloadObj["defaultStaffPassword"] = defaultStaffPassword;
                if (!string.IsNullOrWhiteSpace(staffSession)) payloadObj["token"] = staffSession;
                if (!string.IsNullOrWhiteSpace(creatorUsername)) payloadObj["creatorUsername"] = creatorUsername;
                var msgRegister = JsonSerializer.Serialize(payloadObj);
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgRegister));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingRegisterClubTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS register club failed: {ex.Message}");
                _pendingRegisterClubTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> JoinClubAsync(string clubId, string staffSession, string? password = null)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingJoinClubTcs = tcs;
                var msgJoin = JsonSerializer.Serialize(new { type = "club.join", clubId, password, token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgJoin));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingJoinClubTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS join club failed: {ex.Message}");
                _pendingJoinClubTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> SetJoinPasswordAsync(string newPassword, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingSetJoinPasswordTcs = tcs;
                var msgSetJoinPass = JsonSerializer.Serialize(new { type = "club.join.password.set", token = staffSession, newPassword });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgSetJoinPass));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingSetJoinPasswordTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS set join password failed: {ex.Message}");
                _pendingSetJoinPasswordTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> InviteStaffByUidAsync(string targetUid, string[] jobs, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingInviteStaffTcs = tcs;
                var uidTrim = (targetUid ?? string.Empty).Trim();
                var jobsSend = NormalizeJobs(jobs, null);
                var msgInvite = JsonSerializer.Serialize(new { type = "club.invite", token = staffSession, targetUid = uidTrim, jobs = jobsSend });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgInvite));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingInviteStaffTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS invite staff failed: {ex.Message}");
                _pendingInviteStaffTcs = null;
                return false;
            }
        }
        return false;
    }

    public Task<bool> InviteStaffByUidAsync(string targetUid, string? job, string staffSession)
    {
        var jobs = NormalizeJobs(string.IsNullOrWhiteSpace(job) ? Array.Empty<string>() : new[] { job }, null);
        return InviteStaffByUidAsync(targetUid, jobs, staffSession);
    }

    public async Task<bool> CreateManualStaffEntryAsync(string displayName, string[] jobs, DateTimeOffset? birthday, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingManualStaffAddTcs = tcs;
                var name = (displayName ?? string.Empty).Trim();
                var jobsSend = NormalizeJobs(jobs, null);
                var msgAdd = JsonSerializer.Serialize(new { type = "user.manual.add", token = staffSession, displayName = name, jobs = jobsSend, birthday });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgAdd));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingManualStaffAddTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS manual staff add failed: {ex.Message}");
                _pendingManualStaffAddTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> LinkManualStaffEntryAsync(string manualUid, string targetUid, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingManualStaffLinkTcs = tcs;
                var manualUidTrim = (manualUid ?? string.Empty).Trim();
                var targetUidTrim = (targetUid ?? string.Empty).Trim();
                var msgLink = JsonSerializer.Serialize(new { type = "user.manual.link", token = staffSession, manualUid = manualUidTrim, targetUid = targetUidTrim });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgLink));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingManualStaffLinkTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS manual staff link failed: {ex.Message}");
                _pendingManualStaffLinkTcs = null;
                return false;
            }
        }
        return false;
    }

    private string? _lastErrorMessage;
    public string? LastErrorMessage => _lastErrorMessage;

    public async Task<string[]?> ListUserClubsAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string[]?>();
                _pendingUserClubsTcs = tcs;
                var msgUserClubs = JsonSerializer.Serialize(new { type = "user.clubs.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgUserClubs));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resArr = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingUserClubsTcs = null;
                return resArr;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS list user clubs failed: {ex.Message}");
                _pendingUserClubsTcs = null;
                return null;
            }
        }
        return null;
    }

    public async Task<string[]?> ListCreatedClubsAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string[]?>();
                _pendingCreatedClubsTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.clubs.created.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resArr = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingCreatedClubsTcs = null;
                return resArr;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS list created clubs failed: {ex.Message}");
                _pendingCreatedClubsTcs = null;
                return null;
            }
        }
        return null;
    }

    public async Task<bool?> UserExistsAsync(string username)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool?>();
                _pendingUserExistsTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.exists.request", username });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resVal = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingUserExistsTcs = null;
                return resVal;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS user exists failed: {ex.Message}");
                _pendingUserExistsTcs = null;
                return null;
            }
        }
        return null;
    }

    public async Task<bool> SendServerAnnouncementAsync(string message, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingServerAnnouncementTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "server.announcement", token = staffSession, message });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingServerAnnouncementTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS server announcement failed: {ex.Message}");
                _pendingServerAnnouncementTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> ScheduleServerShutdownAsync(string message, int[] minutes, string staffSession, bool restart)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                if (restart) _pendingServerRestartTcs = tcs;
                else _pendingServerShutdownTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = restart ? "server.restart" : "server.shutdown", token = staffSession, message, minutes });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                if (restart) _pendingServerRestartTcs = null;
                else _pendingServerShutdownTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS server shutdown schedule failed: {ex.Message}");
                if (restart) _pendingServerRestartTcs = null;
                else _pendingServerShutdownTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<(bool Active, bool Pending)?> GetMaintenanceModeAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<(bool Active, bool Pending)?>();
                _pendingMaintenanceStatusTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "maintenance.mode.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingMaintenanceStatusTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS maintenance mode status failed: {ex.Message}");
                _pendingMaintenanceStatusTcs = null;
                return null;
            }
        }
        return null;
    }

    public async Task<bool> SetMaintenanceModeAsync(bool enabled, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingMaintenanceSetTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "maintenance.mode.set", token = staffSession, enabled });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingMaintenanceSetTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS maintenance mode set failed: {ex.Message}");
                _pendingMaintenanceSetTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> DeleteUserAsync(string username, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingDeleteUserTcs = tcs;
                var msgDelete = JsonSerializer.Serialize(new { type = "user.delete", token = staffSession, username });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgDelete));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingDeleteUserTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS delete user failed: {ex.Message}");
                _pendingDeleteUserTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<string[]?> ListUsersAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string[]?>();
                _pendingUsersListTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "users.list.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resArr = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingUsersListTcs = null;
                return resArr;
            }
            catch (Exception ex) { _log?.Debug($"WS list users failed: {ex.Message}"); _pendingUsersListTcs = null; return null; }
        }
        return null;
    }

    public async Task<VenuePlus.State.StaffUser[]?> ListUsersDetailedAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<VenuePlus.State.StaffUser[]?>();
                _pendingUsersDetailsTcs = tcs;
                var msgUsersDetails = JsonSerializer.Serialize(new { type = "users.details.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgUsersDetails));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resArr = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingUsersDetailsTcs = null;
                return resArr;
            }
            catch (Exception ex) { _log?.Debug($"WS list users detailed failed: {ex.Message}"); _pendingUsersDetailsTcs = null; return null; }
        }
        return null;
    }

    

    private static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTimeOffset.TryParse(s, out var dto)) return dto;
        return null;
    }

    private static string[] NormalizeJobs(string[]? jobs, string? fallbackJob)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (jobs != null)
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                var j = jobs[i];
                if (string.IsNullOrWhiteSpace(j)) continue;
                set.Add(j);
            }
        }
        if (set.Count == 0 && !string.IsNullOrWhiteSpace(fallbackJob)) set.Add(fallbackJob);
        if (set.Count == 0) set.Add("Unassigned");
        var arr = new string[set.Count];
        int idx = 0;
        foreach (var j in set)
        {
            arr[idx] = j;
            idx++;
        }
        Array.Sort(arr, StringComparer.Ordinal);
        return arr;
    }

    private static void NormalizeStaffUser(StaffUser user)
    {
        var jobs = NormalizeJobs(user.Jobs, user.Job);
        user.Jobs = jobs;
        if (string.IsNullOrWhiteSpace(user.Job) && jobs.Length > 0) user.Job = jobs[0];
        if (string.Equals(user.Job, "Unassigned", StringComparison.Ordinal) && jobs.Length > 0)
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                if (!string.Equals(jobs[i], "Unassigned", StringComparison.Ordinal)) { user.Job = jobs[i]; break; }
            }
        }
    }

    public async Task<bool> UpdateUserJobAsync(string username, string[] jobs, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingUpdateUserJobTcs = tcs;
                var jobsSend = NormalizeJobs(jobs, null);
                var msgUpdate = JsonSerializer.Serialize(new { type = "user.update.request", token = staffSession, username, jobs = jobsSend });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgUpdate));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingUpdateUserJobTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS update user job failed: {ex.Message}");
                _pendingUpdateUserJobTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<bool> UpdateStaffBirthdayAsync(string username, DateTimeOffset? birthday, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingUpdateUserBirthdayTcs = tcs;
                var msgUpdate = JsonSerializer.Serialize(new { type = "user.update.request", token = staffSession, username, birthday });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgUpdate));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingUpdateUserBirthdayTcs = null;
                return ok;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS update staff birthday failed: {ex.Message}");
                _pendingUpdateUserBirthdayTcs = null;
                return false;
            }
        }
        return false;
    }

    public Task<bool> UpdateUserJobAsync(string username, string job, string staffSession)
    {
        var jobs = NormalizeJobs(string.IsNullOrWhiteSpace(job) ? Array.Empty<string>() : new[] { job }, null);
        return UpdateUserJobAsync(username, jobs, staffSession);
    }

    public async Task<System.Collections.Generic.Dictionary<string, JobRightsInfo>?> ListJobRightsAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<System.Collections.Generic.Dictionary<string, JobRightsInfo>?>();
                _pendingJobRightsTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "jobs.rights.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var dict = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingJobRightsTcs = null;
                return dict;
            }
            catch (Exception ex) { _log?.Debug($"WS list job rights failed: {ex.Message}"); _pendingJobRightsTcs = null; return null; }
        }
        return null;
    }

    public async Task<string?> GetAccessKeyAsync(string clubId, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingAccessKeyTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.accesskey.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var key = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingAccessKeyTcs = null;
                return key;
            }
            catch (Exception ex) { _log?.Debug($"WS access key failed: {ex.Message}"); _pendingAccessKeyTcs = null; return null; }
        }
        return null;
    }

    public async Task<string?> RegenerateAccessKeyAsync(string clubId, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingRegenerateAccessKeyTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.accesskey.regenerate", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var key = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingRegenerateAccessKeyTcs = null;
                return key;
            }
            catch (Exception ex) { _log?.Debug($"WS access key regenerate failed: {ex.Message}"); _pendingRegenerateAccessKeyTcs = null; return null; }
        }
        return null;
    }

    public async Task<bool> DeleteClubAsync(string clubId, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingDeleteClubTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.delete", clubId, token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingDeleteClubTcs = null;
                return ok;
            }
            catch (TaskCanceledException)
            {
                _log?.Debug("Delete club timed out");
                _pendingDeleteClubTcs = null;
                return false;
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS delete club failed: {ex.Message}");
                _pendingDeleteClubTcs = null;
                return false;
            }
        }
        return false;
    }

    public async Task<(string Job, System.Collections.Generic.Dictionary<string, bool> Rights)?> GetSelfRightsAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<(string Job, System.Collections.Generic.Dictionary<string, bool>)?>();
                _pendingSelfRightsTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.self.rights.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resRights = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingSelfRightsTcs = null;
                return resRights;
            }
            catch (Exception ex) { _log?.Debug($"WS self rights failed: {ex.Message}"); _pendingSelfRightsTcs = null; return null; }
        }
        return null;
    }

    public async Task<(string Username, string Uid, bool IsServerAdmin)?> GetSelfProfileAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<(string Username, string Uid, bool IsServerAdmin)?>();
                _pendingSelfProfileTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.self.profile.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resProfile = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingSelfProfileTcs = null;
                return resProfile;
            }
            catch (Exception ex) { _log?.Debug($"WS self profile failed: {ex.Message}"); _pendingSelfProfileTcs = null; return null; }
        }
        return null;
    }

    public async Task<DateTimeOffset?> GetSelfBirthdayAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<DateTimeOffset?>();
                _pendingSelfBirthdayTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.self.birthday.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var res = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingSelfBirthdayTcs = null;
                return res;
            }
            catch (Exception ex) { _log?.Debug($"WS self birthday failed: {ex.Message}"); _pendingSelfBirthdayTcs = null; return null; }
        }
        return null;
    }

    public async Task<bool> SetSelfBirthdayAsync(DateTimeOffset? birthday, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingSelfBirthdaySetTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "user.self.birthday.set", token = staffSession, birthday });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingSelfBirthdaySetTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS self birthday set failed: {ex.Message}"); _pendingSelfBirthdaySetTcs = null; return false; }
        }
        return false;
    }


    public async Task<bool> UpdateJobRightsAsync(string name, JobRightsInfo rights, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingUpdateJobRightsTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "jobs.rights.update", token = staffSession, name, rights }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingUpdateJobRightsTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS jobs.rights.update failed: {ex.Message}"); _pendingUpdateJobRightsTcs = null; return false; }
        }
        return false;
    }

    public async Task<string[]?> ListJobsAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string[]?>();
                _pendingJobsListTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "jobs.list.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var resArr = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingJobsListTcs = null;
                return resArr;
            }
            catch (Exception ex) { _log?.Debug($"WS list jobs failed: {ex.Message}"); _pendingJobsListTcs = null; return null; }
        }
        return null;
    }

    public async Task<bool> AddJobAsync(string name, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingAddJobTcs = tcs;
                var msgAddJob = JsonSerializer.Serialize(new { type = "job.add", name, token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgAddJob));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingAddJobTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS job add failed: {ex.Message}"); _pendingAddJobTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> DeleteJobAsync(string name, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingDeleteJobTcs = tcs;
                var msgDelJob = JsonSerializer.Serialize(new { type = "job.delete", name, token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgDelJob));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingDeleteJobTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS job delete failed: {ex.Message}"); _pendingDeleteJobTcs = null; return false; }
        }
        return false;
    }




    public Task FetchSnapshotAsync() { return Task.CompletedTask; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _allowReconnect = false;
        DisconnectAsync().GetAwaiter().GetResult();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var ms = new System.IO.MemoryStream(64 * 1024);
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
                        IsConnected = false;
                        ConnectionChanged?.Invoke(false);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
            }
            catch (Exception ex)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastWsReceiveErrorAt).TotalSeconds >= 10)
                {
                    _lastWsReceiveErrorAt = now;
                    _log?.Debug($"WS receive failed: {ex.Message}");
                }
                break;
            }
            var msg = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            try
            {
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeEl))
                {
                    var type = typeEl.GetString() ?? string.Empty;
                    if (type == "jobs.list")
                    {
                        var jobsEl = root.GetProperty("jobs");
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var el in jobsEl.EnumerateArray()) list.Add(el.GetString() ?? string.Empty);
                        JobsListReceived?.Invoke(list.ToArray());
                        _pendingJobsListTcs?.TrySetResult(list.ToArray());
                        _pendingJobsListTcs = null;
                    }
                    else if (type == "jobs.rights")
                    {
                        var rightsEl = root.GetProperty("rights");
                        var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JobRightsInfo>>(rightsEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new System.Collections.Generic.Dictionary<string, JobRightsInfo>();
                        JobRightsReceived?.Invoke(dict);
                        _pendingJobRightsTcs?.TrySetResult(dict);
                        _pendingJobRightsTcs = null;
                    }
                    else if (type == "jobs.rights.ok")
                    {
                        _pendingUpdateJobRightsTcs?.TrySetResult(true);
                        _pendingUpdateJobRightsTcs = null;
                    }
                    else if (type == "jobs.rights.fail")
                    {
                        _pendingUpdateJobRightsTcs?.TrySetResult(false);
                        _pendingUpdateJobRightsTcs = null;
                    }
                    else if (type == "users.list")
                    {
                        var usersEl = root.GetProperty("users");
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var el in usersEl.EnumerateArray()) list.Add(el.GetString() ?? string.Empty);
                        UsersListReceived?.Invoke(list.ToArray());
                        _pendingUsersListTcs?.TrySetResult(list.ToArray());
                        _pendingUsersListTcs = null;
                    }
                    else if (type == "users.details")
                    {
                        var usersEl = root.GetProperty("users");
                        var details = JsonSerializer.Deserialize<System.Collections.Generic.List<VenuePlus.State.StaffUser>>(usersEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.ToArray();
                        if (details != null)
                        {
                            for (int i = 0; i < details.Length; i++)
                            {
                                NormalizeStaffUser(details[i]);
                            }
                        }
                        if (details != null) UsersDetailsReceived?.Invoke(details);
                        if (details != null) { _pendingUsersDetailsTcs?.TrySetResult(details); _pendingUsersDetailsTcs = null; }
                    }
                    else if (type == "user.update")
                    {
                        var username = root.GetProperty("username").GetString() ?? string.Empty;
                        var hasJob = root.TryGetProperty("job", out var jobEl);
                        var hasJobs = root.TryGetProperty("jobs", out var jobsEl) && jobsEl.ValueKind == JsonValueKind.Array;
                        if (!hasJob && !hasJobs) continue;
                        var job = hasJob ? (jobEl.GetString() ?? string.Empty) : string.Empty;
                        var jobs = new List<string>();
                        if (hasJobs)
                        {
                            foreach (var el in jobsEl.EnumerateArray())
                            {
                                var val = el.GetString();
                                if (!string.IsNullOrWhiteSpace(val)) jobs.Add(val);
                            }
                        }
                        var jobsArr = NormalizeJobs(jobs.Count == 0 ? Array.Empty<string>() : jobs.ToArray(), job);
                        UserJobUpdated?.Invoke(username, job, jobsArr);
                    }
                    else if (type == "access.owner.changed")
                    {
                        var owner = root.TryGetProperty("owner", out var o) ? (o.GetString() ?? string.Empty) : string.Empty;
                        var clubIdMsg = root.TryGetProperty("clubId", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty;
                        OwnerAccessChanged?.Invoke(owner, clubIdMsg);
                    }
                    else if (type == "membership.removed")
                    {
                        var username = root.GetProperty("username").GetString() ?? string.Empty;
                        var clubIdMsg = root.TryGetProperty("clubId", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty;
                        MembershipRemoved?.Invoke(username, clubIdMsg);
                    }
                    else if (type == "membership.added")
                    {
                        var username = root.TryGetProperty("username", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                        var clubIdMsg = root.TryGetProperty("clubId", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty;
                        MembershipAdded?.Invoke(username, clubIdMsg);
                    }
                    else if (type == "server.announcement")
                    {
                        var msgText = root.TryGetProperty("message", out var m) ? (m.GetString() ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(msgText)) ServerAnnouncementReceived?.Invoke(msgText);
                    }
                    else if (type == "vip.snapshot")
                    {
                        var entriesEl = root.GetProperty("entries");
                        var entries = JsonSerializer.Deserialize<System.Collections.Generic.List<VipEntry>>(entriesEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new System.Collections.Generic.List<VipEntry>();
                        try { _log?.Debug($"WS vip.snapshot club={_clubId ?? "--"} count={entries.Count}"); } catch { }
                        Logger.LogDebug($"[VipListSync] ws.msg vip.snapshot club={_clubId ?? "--"} count={entries.Count}");
                        SnapshotReceived?.Invoke(entries);
                    }
                    else if (type == "club.logo")
                    {
                        var logo = root.TryGetProperty("logoBase64", out var l) ? (l.GetString() ?? string.Empty) : string.Empty;
                        var clubIdMsg = root.TryGetProperty("clubId", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(clubIdMsg) && _pendingClubLogoForByClub.TryRemove(clubIdMsg, out var tcsLogo))
                        {
                            tcsLogo.TrySetResult(string.IsNullOrWhiteSpace(logo) ? null : logo);
                        }
                        else if (!string.IsNullOrWhiteSpace(clubIdMsg) && !string.IsNullOrWhiteSpace(_clubId) && string.Equals(clubIdMsg, _clubId, StringComparison.Ordinal))
                        {
                            ClubLogoReceived?.Invoke(string.IsNullOrWhiteSpace(logo) ? null : logo);
                            _pendingClubLogoTcs?.TrySetResult(string.IsNullOrWhiteSpace(logo) ? null : logo);
                            _pendingClubLogoTcs = null;
                        }
                    }
                    else if (type == "club.logo.update.ok")
                    {
                        _pendingClubLogoUpdateTcs?.TrySetResult(true);
                        _pendingClubLogoUpdateTcs = null;
                    }
                    else if (type == "club.logo.update.fail")
                    {
                        _pendingClubLogoUpdateTcs?.TrySetResult(false);
                        _pendingClubLogoUpdateTcs = null;
                    }
                    else if (type == "club.logo.delete.ok")
                    {
                        _pendingClubLogoDeleteTcs?.TrySetResult(true);
                        _pendingClubLogoDeleteTcs = null;
                    }
                    else if (type == "club.logo.delete.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingClubLogoDeleteTcs?.TrySetResult(false);
                        _pendingClubLogoDeleteTcs = null;
                    }
                    else if (type == "vip.update.ok")
                    {
                        _pendingVipUpdateTcs?.TrySetResult(true);
                        _pendingVipUpdateTcs = null;
                    }
                    else if (type == "vip.update.fail")
                    {
                        _pendingVipUpdateTcs?.TrySetResult(false);
                        _pendingVipUpdateTcs = null;
                    }
                    else if (type == "vip.purge.expired.ok")
                    {
                        _pendingVipPurgeTcs?.TrySetResult(true);
                        _pendingVipPurgeTcs = null;
                    }
                    else if (type == "vip.purge.expired.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingVipPurgeTcs?.TrySetResult(false);
                        _pendingVipPurgeTcs = null;
                    }
                    else if (type == "job.add.ok")
                    {
                        _pendingAddJobTcs?.TrySetResult(true);
                        _pendingAddJobTcs = null;
                    }
                    else if (type == "job.add.fail")
                    {
                        _pendingAddJobTcs?.TrySetResult(false);
                        _pendingAddJobTcs = null;
                    }
                    else if (type == "job.delete.ok")
                    {
                        _pendingDeleteJobTcs?.TrySetResult(true);
                        _pendingDeleteJobTcs = null;
                    }
                    else if (type == "job.delete.fail")
                    {
                        _pendingDeleteJobTcs?.TrySetResult(false);
                        _pendingDeleteJobTcs = null;
                    }
                    else if (type == "login.ok")
                    {
                        var token = root.TryGetProperty("token", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;
                        _pendingLoginTcs?.TrySetResult(string.IsNullOrWhiteSpace(token) ? null : token);
                        _pendingLoginTcs = null;
                    }
                    else if (type == "login.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingLoginTcs?.TrySetResult(null);
                        _pendingLoginTcs = null;
                    }
                    else if (type == "register.ok")
                    {
                        var recoveryCode = root.TryGetProperty("recoveryCode", out var rc) ? (rc.GetString() ?? string.Empty) : string.Empty;
                        _pendingRegisterUserTcs?.TrySetResult(string.IsNullOrWhiteSpace(recoveryCode) ? null : recoveryCode);
                        _pendingRegisterUserTcs = null;
                    }
                    else if (type == "register.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingRegisterUserTcs?.TrySetResult(null);
                        _pendingRegisterUserTcs = null;
                    }
                    else if (type == "session.logout.ok")
                    {
                        _pendingLogoutTcs?.TrySetResult(true);
                        _pendingLogoutTcs = null;
                    }
                    else if (type == "session.logout.fail")
                    {
                        _pendingLogoutTcs?.TrySetResult(false);
                        _pendingLogoutTcs = null;
                    }
                    else if (type == "user.self.password.ok")
                    {
                        _pendingSelfPasswordTcs?.TrySetResult(true);
                        _pendingSelfPasswordTcs = null;
                    }
                    else if (type == "user.self.password.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingSelfPasswordTcs?.TrySetResult(false);
                        _pendingSelfPasswordTcs = null;
                    }
                    else if (type == "user.self.birthday")
                    {
                        DateTimeOffset? birthday = null;
                        if (root.TryGetProperty("birthday", out var b))
                        {
                            if (b.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(b.GetString(), out var dt)) birthday = dt;
                        }
                        _pendingSelfBirthdayTcs?.TrySetResult(birthday);
                        _pendingSelfBirthdayTcs = null;
                    }
                    else if (type == "user.self.birthday.ok")
                    {
                        _pendingSelfBirthdaySetTcs?.TrySetResult(true);
                        _pendingSelfBirthdaySetTcs = null;
                    }
                    else if (type == "user.self.birthday.fail")
                    {
                        _pendingSelfBirthdaySetTcs?.TrySetResult(false);
                        _pendingSelfBirthdaySetTcs = null;
                    }
                    else if (type == "user.recovery.generate.ok")
                    {
                        var code = root.TryGetProperty("recoveryCode", out var rc) ? (rc.GetString() ?? string.Empty) : string.Empty;
                        _pendingGenerateRecoveryCodeTcs?.TrySetResult(string.IsNullOrWhiteSpace(code) ? null : code);
                        _pendingGenerateRecoveryCodeTcs = null;
                    }
                    else if (type == "user.recovery.generate.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingGenerateRecoveryCodeTcs?.TrySetResult(null);
                        _pendingGenerateRecoveryCodeTcs = null;
                    }
                    else if (type == "user.password.reset.recovery.ok")
                    {
                        _pendingResetRecoveryPasswordTcs?.TrySetResult(true);
                        _pendingResetRecoveryPasswordTcs = null;
                    }
                    else if (type == "user.password.reset.recovery.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingResetRecoveryPasswordTcs?.TrySetResult(false);
                        _pendingResetRecoveryPasswordTcs = null;
                    }
                    else if (type == "user.self.rights")
                    {
                        var job = root.TryGetProperty("job", out var jobEl) ? (jobEl.GetString() ?? string.Empty) : string.Empty;
                        var jobs = Array.Empty<string>();
                        if (root.TryGetProperty("jobs", out var jobsEl) && jobsEl.ValueKind == JsonValueKind.Array)
                        {
                            var list = new System.Collections.Generic.List<string>();
                            foreach (var el in jobsEl.EnumerateArray())
                            {
                                var val = el.GetString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(val)) list.Add(val);
                            }
                            jobs = NormalizeJobs(list.Count == 0 ? Array.Empty<string>() : list.ToArray(), job);
                            if (string.IsNullOrWhiteSpace(job) || string.Equals(job, "Unassigned", StringComparison.Ordinal))
                            {
                                for (int i = 0; i < jobs.Length; i++)
                                {
                                    if (!string.Equals(jobs[i], "Unassigned", StringComparison.Ordinal)) { job = jobs[i]; break; }
                                }
                                if (string.IsNullOrWhiteSpace(job) && jobs.Length > 0) job = jobs[0];
                            }
                        }
                        var rightsEl = root.GetProperty("rights");
                        var dict = new System.Collections.Generic.Dictionary<string, bool>();
                        foreach (var prop in rightsEl.EnumerateObject()) dict[prop.Name] = prop.Value.GetBoolean();
                        _pendingSelfRightsTcs?.TrySetResult((job, dict));
                        _pendingSelfRightsTcs = null;
                    }
                    else if (type == "user.self.profile")
                    {
                        var username = root.TryGetProperty("username", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                        var uid = root.TryGetProperty("uid", out var idEl) ? (idEl.GetString() ?? string.Empty) : string.Empty;
                        var isServerAdmin = root.TryGetProperty("isServerAdmin", out var adminEl) && adminEl.GetBoolean();
                        _pendingSelfProfileTcs?.TrySetResult((username, uid, isServerAdmin));
                        _pendingSelfProfileTcs = null;
                    }
                    else if (type == "user.exists")
                    {
                        var exists = root.TryGetProperty("exists", out var exEl) && exEl.GetBoolean();
                        _pendingUserExistsTcs?.TrySetResult(exists);
                        _pendingUserExistsTcs = null;
                    }
                    else if (type == "server.announcement.ok")
                    {
                        _pendingServerAnnouncementTcs?.TrySetResult(true);
                        _pendingServerAnnouncementTcs = null;
                    }
                    else if (type == "server.announcement.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingServerAnnouncementTcs?.TrySetResult(false);
                        _pendingServerAnnouncementTcs = null;
                    }
                    else if (type == "server.shutdown.ok")
                    {
                        _pendingServerShutdownTcs?.TrySetResult(true);
                        _pendingServerShutdownTcs = null;
                    }
                    else if (type == "server.shutdown.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingServerShutdownTcs?.TrySetResult(false);
                        _pendingServerShutdownTcs = null;
                    }
                    else if (type == "server.restart.ok")
                    {
                        _pendingServerRestartTcs?.TrySetResult(true);
                        _pendingServerRestartTcs = null;
                    }
                    else if (type == "server.restart.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingServerRestartTcs?.TrySetResult(false);
                        _pendingServerRestartTcs = null;
                    }
                    else if (type == "maintenance.mode.status")
                    {
                        var active = root.TryGetProperty("active", out var a) ? a.GetBoolean() : (root.TryGetProperty("enabled", out var en) && en.GetBoolean());
                        var pending = root.TryGetProperty("pending", out var p) && p.GetBoolean();
                        _pendingMaintenanceStatusTcs?.TrySetResult((active, pending));
                        _pendingMaintenanceStatusTcs = null;
                    }
                    else if (type == "maintenance.mode.status.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingMaintenanceStatusTcs?.TrySetResult(null);
                        _pendingMaintenanceStatusTcs = null;
                    }
                    else if (type == "maintenance.mode.set.ok")
                    {
                        _pendingMaintenanceSetTcs?.TrySetResult(true);
                        _pendingMaintenanceSetTcs = null;
                    }
                    else if (type == "maintenance.mode.set.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingMaintenanceSetTcs?.TrySetResult(false);
                        _pendingMaintenanceSetTcs = null;
                    }
                    else if (type == "user.clubs")
                    {
                        var clubsEl = root.GetProperty("clubs");
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var el in clubsEl.EnumerateArray())
                        {
                            var s = el.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                        }
                        _pendingUserClubsTcs?.TrySetResult(list.ToArray());
                        _pendingUserClubsTcs = null;
                    }
                    else if (type == "user.clubs.created")
                    {
                        var clubsEl = root.GetProperty("clubs");
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var el in clubsEl.EnumerateArray())
                        {
                            var s = el.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                        }
                        _pendingCreatedClubsTcs?.TrySetResult(list.ToArray());
                        _pendingCreatedClubsTcs = null;
                    }
                    else if (type == "club.delete.ok")
                    {
                        _pendingDeleteClubTcs?.TrySetResult(true);
                        _pendingDeleteClubTcs = null;
                    }
                    else if (type == "club.delete.fail")
                    {
                        _pendingDeleteClubTcs?.TrySetResult(false);
                        _pendingDeleteClubTcs = null;
                    }
                    else if (type == "club.register.ok")
                    {
                        _pendingRegisterClubTcs?.TrySetResult(true);
                        _pendingRegisterClubTcs = null;
                    }
                    else if (type == "club.register.fail")
                    {
                        _lastErrorMessage = null;
                        try
                        {
                            if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString();
                        }
                        catch { }
                        _pendingRegisterClubTcs?.TrySetResult(false);
                        _pendingRegisterClubTcs = null;
                    }
                    else if (type == "club.join.ok")
                    {
                        _pendingJoinClubTcs?.TrySetResult(true);
                        _pendingJoinClubTcs = null;
                    }
                    else if (type == "club.join.fail")
                    {
                        _lastErrorMessage = null;
                        try
                        {
                            if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString();
                        }
                        catch { }
                        _pendingJoinClubTcs?.TrySetResult(false);
                        _pendingJoinClubTcs = null;
                    }
                    else if (type == "club.join.password.ok")
                    {
                        _pendingSetJoinPasswordTcs?.TrySetResult(true);
                        _pendingSetJoinPasswordTcs = null;
                    }
                    else if (type == "club.join.password.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingSetJoinPasswordTcs?.TrySetResult(false);
                        _pendingSetJoinPasswordTcs = null;
                    }
                    else if (type == "club.invite.ok")
                    {
                        _pendingInviteStaffTcs?.TrySetResult(true);
                        _pendingInviteStaffTcs = null;
                    }
                    else if (type == "club.invite.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingInviteStaffTcs?.TrySetResult(false);
                        _pendingInviteStaffTcs = null;
                    }
                    else if (type == "user.manual.add.ok")
                    {
                        _pendingManualStaffAddTcs?.TrySetResult(true);
                        _pendingManualStaffAddTcs = null;
                    }
                    else if (type == "user.manual.add.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingManualStaffAddTcs?.TrySetResult(false);
                        _pendingManualStaffAddTcs = null;
                    }
                    else if (type == "user.manual.link.ok")
                    {
                        _pendingManualStaffLinkTcs?.TrySetResult(true);
                        _pendingManualStaffLinkTcs = null;
                    }
                    else if (type == "user.manual.link.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingManualStaffLinkTcs?.TrySetResult(false);
                        _pendingManualStaffLinkTcs = null;
                    }
                    else if (type == "user.create.ok")
                    {
                        _pendingCreateUserTcs?.TrySetResult(true);
                        _pendingCreateUserTcs = null;
                    }
                    else if (type == "user.create.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingCreateUserTcs?.TrySetResult(false);
                        _pendingCreateUserTcs = null;
                    }
                    else if (type == "user.delete.ok")
                    {
                        _pendingDeleteUserTcs?.TrySetResult(true);
                        _pendingDeleteUserTcs = null;
                    }
                    else if (type == "user.delete.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingDeleteUserTcs?.TrySetResult(false);
                        _pendingDeleteUserTcs = null;
                    }
                    else if (type == "user.update.ok")
                    {
                        _pendingUpdateUserJobTcs?.TrySetResult(true);
                        _pendingUpdateUserJobTcs = null;
                        _pendingUpdateUserBirthdayTcs?.TrySetResult(true);
                        _pendingUpdateUserBirthdayTcs = null;
                    }
                    else if (type == "user.update.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingUpdateUserJobTcs?.TrySetResult(false);
                        _pendingUpdateUserJobTcs = null;
                        _pendingUpdateUserBirthdayTcs?.TrySetResult(false);
                        _pendingUpdateUserBirthdayTcs = null;
                    }
                    else if (type == "club.accesskey")
                    {
                        var key = root.TryGetProperty("accessKey", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
                        _pendingAccessKeyTcs?.TrySetResult(string.IsNullOrWhiteSpace(key) ? null : key);
                        _pendingAccessKeyTcs = null;
                    }
                    else if (type == "club.accesskey.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingAccessKeyTcs?.TrySetResult(null);
                        _pendingAccessKeyTcs = null;
                    }
                    else if (type == "club.accesskey.regenerate.ok")
                    {
                        var key = root.TryGetProperty("accessKey", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
                        _pendingRegenerateAccessKeyTcs?.TrySetResult(string.IsNullOrWhiteSpace(key) ? null : key);
                        _pendingRegenerateAccessKeyTcs = null;
                    }
                    else if (type == "club.accesskey.regenerate.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingRegenerateAccessKeyTcs?.TrySetResult(null);
                        _pendingRegenerateAccessKeyTcs = null;
                    }
                    else if (type == "dj.snapshot")
                    {
                        var arrEl = root.TryGetProperty("entries", out var e2) ? e2 : default;
                        if (arrEl.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                var det = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.DjEntry[]>(arrEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) ?? Array.Empty<VenuePlus.State.DjEntry>();
                                DjSnapshotReceived?.Invoke(det);
                            }
                            catch { }
                        }
                    }
                    else if (type == "shift.snapshot")
                    {
                        var arrEl = root.TryGetProperty("entries", out var e2) ? e2 : default;
                        if (arrEl.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                var det = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.ShiftEntry[]>(arrEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) ?? Array.Empty<VenuePlus.State.ShiftEntry>();
                                ShiftSnapshotReceived?.Invoke(det);
                            }
                            catch { }
                        }
                    }
                    else if (type == "dj.update")
                    {
                        var op = root.TryGetProperty("op", out var o) ? (o.GetString() ?? string.Empty) : string.Empty;
                        var entryEl = root.TryGetProperty("entry", out var e) ? e : default;
                        if (entryEl.ValueKind == JsonValueKind.Object)
                        {
                            try
                            {
                                var entry = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.DjEntry>(entryEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                                if (entry != null)
                                {
                                    if (string.Equals(op, "add", StringComparison.Ordinal)) DjEntryAdded?.Invoke(entry);
                                    else if (string.Equals(op, "remove", StringComparison.Ordinal)) DjEntryRemoved?.Invoke(entry);
                                }
                            }
                            catch { }
                        }
                    }
                    else if (type == "shift.update")
                    {
                        var op = root.TryGetProperty("op", out var o) ? (o.GetString() ?? string.Empty) : string.Empty;
                        var entryEl = root.TryGetProperty("entry", out var e) ? e : default;
                        if (string.Equals(op, "remove", StringComparison.Ordinal))
                        {
                            var idEl = root.TryGetProperty("id", out var idProp) ? idProp : default;
                            if (idEl.ValueKind == JsonValueKind.String)
                            {
                                var idStr = idEl.GetString() ?? string.Empty;
                                if (Guid.TryParse(idStr, out var idVal)) ShiftEntryRemoved?.Invoke(idVal);
                            }
                        }
                        else if (entryEl.ValueKind == JsonValueKind.Object)
                        {
                            try
                            {
                                var entry = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.ShiftEntry>(entryEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                                if (entry != null)
                                {
                                    if (string.Equals(op, "add", StringComparison.Ordinal)) ShiftEntryAdded?.Invoke(entry);
                                    else if (string.Equals(op, "update", StringComparison.Ordinal)) ShiftEntryUpdated?.Invoke(entry);
                                }
                            }
                            catch { }
                        }
                    }
                    else if (type == "shift.update.ok")
                    {
                        _pendingShiftUpdateTcs?.TrySetResult(true);
                        _pendingShiftUpdateTcs = null;
                    }
                    else if (type == "shift.update.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingShiftUpdateTcs?.TrySetResult(false);
                        _pendingShiftUpdateTcs = null;
                    }
                    else if (type == "dj.update.ok")
                    {
                        _pendingDjUpdateTcs?.TrySetResult(true);
                        _pendingDjUpdateTcs = null;
                    }
                    else if (type == "dj.update.fail")
                    {
                        _lastErrorMessage = null;
                        try { if (root.TryGetProperty("message", out var m)) _lastErrorMessage = m.GetString(); } catch { }
                        _pendingDjUpdateTcs?.TrySetResult(false);
                        _pendingDjUpdateTcs = null;
                    }
                }
                else
                {
                    var op = root.GetProperty("op").GetString();
                    var entryEl = root.GetProperty("entry");
                    try
                    {
                        var entryVip = JsonSerializer.Deserialize<VipEntry>(entryEl.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        if (entryVip != null)
                        {
                            _log?.Debug($"WS msg op={op} key={entryVip.CharacterName}@{entryVip.HomeWorld}");
                            if (op == "add") EntryAdded?.Invoke(entryVip);
                            else if (op == "remove") EntryRemoved?.Invoke(entryVip);
                            goto EndOp;
                        }
                    }
                    catch { }
                    try
                    {
                        var entryDj = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.DjEntry>(entryEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                        if (entryDj != null)
                        {
                            if (op == "add") DjEntryAdded?.Invoke(entryDj);
                            else if (op == "remove") DjEntryRemoved?.Invoke(entryDj);
                            goto EndOp;
                        }
                    }
                    catch { }
                    try
                    {
                        var entryShift = System.Text.Json.JsonSerializer.Deserialize<VenuePlus.State.ShiftEntry>(entryEl.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                        if (entryShift != null)
                        {
                            if (op == "add") ShiftEntryAdded?.Invoke(entryShift);
                            else if (op == "update") ShiftEntryUpdated?.Invoke(entryShift);
                            else if (op == "remove") ShiftEntryRemoved?.Invoke(entryShift.Id);
                        }
                    }
                    catch { }
                    EndOp:;
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"WS msg parse failed: {ex.Message}");
            }
        }
        IsConnected = false;
        if (!ct.IsCancellationRequested && _allowReconnect) StartReconnectLoop();
        ConnectionChanged?.Invoke(false);
    }

    public async Task<string?> GetClubLogoAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingClubLogoTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.logo.request", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var logo = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingClubLogoTcs = null;
                return logo;
            }
            catch (Exception ex) { _log?.Debug($"WS get club logo failed: {ex.Message}"); _pendingClubLogoTcs = null; return null; }
        }
        return null;
    }

    public async Task<string?> GetClubLogoForAsync(string staffSession, string clubId)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();
                _pendingClubLogoForByClub[clubId] = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.logo.for.request", token = staffSession, clubId });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var logo = tcs.Task.IsCompleted ? tcs.Task.Result : null;
                _pendingClubLogoForByClub.TryRemove(clubId, out _);
                return logo;
            }
            catch (Exception ex) { _log?.Debug($"WS get club logo for failed: {ex.Message}"); return null; }
        }
        return null;
    }

    public async Task<bool> UpdateClubLogoAsync(string logoBase64, string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingClubLogoUpdateTcs = tcs;
                var msgUpdateLogo = JsonSerializer.Serialize(new { type = "club.logo.update", logoBase64, token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msgUpdateLogo));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingClubLogoUpdateTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS update club logo failed: {ex.Message}"); _pendingClubLogoUpdateTcs = null; return false; }
        }
        return false;
    }

    public async Task<bool> DeleteClubLogoAsync(string staffSession)
    {
        if (_useWebSocket && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                _pendingClubLogoDeleteTcs = tcs;
                var payload = JsonSerializer.Serialize(new { type = "club.logo.delete", token = staffSession });
                var seg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload));
                await _ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                _pendingClubLogoDeleteTcs = null;
                return ok;
            }
            catch (Exception ex) { _log?.Debug($"WS delete club logo failed: {ex.Message}"); _pendingClubLogoDeleteTcs = null; return false; }
        }
        return false;
    }

    public Task<bool> HealthCheckAsync()
    {
        try
        {
            var wsState = _ws != null ? _ws.State : WebSocketState.None;
            _log?.Debug($"Health check ws={wsState}");
            return Task.FromResult(wsState == WebSocketState.Open);
        }
        catch (Exception ex) { _log?.Debug($"Health check error: {ex.GetType().Name} {ex.Message} ws={_ws?.State}"); return Task.FromResult(false); }
    }

    private CancellationTokenSource? _healthCts;
    private Task? _healthTask;
    private int _healthIntervalMs = 30000;
    private int _healthFailures;

    private void StartHealthLoop()
    {
        if (_healthTask != null) return;
        if (string.IsNullOrWhiteSpace(_baseUrl)) return;
        _healthCts = new CancellationTokenSource();
        var ct = _healthCts.Token;
        _healthTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_healthIntervalMs, ct);
                    if (ct.IsCancellationRequested) break;
                    var ok = await HealthCheckAsync();
                    if (!ok)
                    {
                    _healthFailures++;
                    var wsStateStr = _ws != null ? _ws.State.ToString() : "null";
                    _log?.Debug($"Health check failed ws={wsStateStr} failures={_healthFailures}");
                        var wsOpen = _ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting);
                        if (_healthFailures >= 2 && !wsOpen)
                        {
                            IsConnected = false;
                            ConnectionChanged?.Invoke(false);
                            StartReconnectLoop();
                        }
                    }
                    else { _healthFailures = 0; _log?.Debug("Health check ok"); }
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex) { _log?.Debug($"Health loop error: {ex.Message}"); }
            }
        }, ct);
    }

    private void StopHealthLoop()
    {
        try { _healthCts?.Cancel(); } catch { }
        _healthTask = null;
        _healthCts?.Dispose();
        _healthCts = null;
    }

    private void StopReconnect()
    {
        if (_reconnectCts != null)
        {
            _reconnectCts.Cancel();
        }
        _reconnectTask = null;
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        _reconnectDelayMs = 1000;
    }

    private void StartReconnectLoop()
    {
        if (_reconnectTask != null) return;
        var wsOpen = _ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting);
        if (wsOpen)
        {
            _log?.Debug("Reconnect skipped: WS already open/connecting");
            return;
        }
        if (string.IsNullOrWhiteSpace(_baseUrl)) return;
        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;
        _reconnectTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_reconnectDelayMs, ct);
                    if (ct.IsCancellationRequested) break;
                    var wsOpenInner = _ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting);
                    if (wsOpenInner) { _log?.Debug("Reconnect loop exit: WS is open/connecting"); break; }
                    if (_reconnectAttempts >= _reconnectMaxAttempts)
                    {
                        _log?.Debug($"Reconnect stopped after {_reconnectAttempts} attempts");
                        break;
                    }
                    _reconnectAttempts++;
                    var ok = await ConnectAsync(_baseUrl!);
                    if (ok) break;
                    _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, 8000);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex) { _log?.Debug($"Reconnect attempt failed: {ex.Message}"); }
            }
        }, ct);
    }
}
