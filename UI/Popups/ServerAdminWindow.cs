using System;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class ServerAdminWindow : Window
{
    private readonly VenuePlusApp _app;
    private readonly HttpClient _httpClient = new();
    private readonly object _stateLock = new();
    private bool _healthInFlight;
    private string _healthStatus = "Unknown";
    private bool? _healthDbOk;
    private DateTimeOffset? _healthServerTime;
    private DateTimeOffset? _healthCheckedAt;
    private DateTimeOffset? _healthCheckStartedAt;
    private bool _healthTimeoutTriggered;
    private string _healthError = string.Empty;
    private string _maintenanceStatus = string.Empty;
    private bool? _maintenanceModeActive;
    private bool? _maintenanceModePendingEnable;
    private DateTimeOffset? _maintenanceModeCheckedAt;
    private bool _maintenanceModeInFlight;
    private bool _maintenanceStatusRequested;
    private string _maintenanceModeStatus = string.Empty;
    private string _userLookupInput = string.Empty;
    private string _userLookupStatus = string.Empty;
    private string _deleteUserInput = string.Empty;
    private bool _deleteUserConfirm;
    private string _deleteUserStatus = string.Empty;
    private string _announcementMessage = string.Empty;
    private string _announcementStatus = string.Empty;
    private string _shutdownMessage = string.Empty;
    private string _shutdownMinutesInput = "30,20,10";
    private bool _shutdownConfirm;
    private string _shutdownStatus = string.Empty;

    public ServerAdminWindow(VenuePlusApp app) : base("Server Admin")
    {
        _app = app;
        Size = new Vector2(520f, 460f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320f, 240f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        if (!_app.IsServerAdmin || !_app.HasStaffSession)
        {
            IsOpen = false;
            return;
        }

        bool triggerTimeoutLogout = false;
        bool requestMaintenanceStatus = false;
        lock (_stateLock)
        {
            if (_healthInFlight && !_healthTimeoutTriggered && _healthCheckStartedAt.HasValue)
            {
                var elapsed = DateTimeOffset.UtcNow - _healthCheckStartedAt.Value;
                if (elapsed >= TimeSpan.FromSeconds(10))
                {
                    _healthTimeoutTriggered = true;
                    _healthInFlight = false;
                    _healthStatus = "Fail";
                    _healthError = "Health check timeout";
                    _healthCheckedAt = DateTimeOffset.UtcNow;
                    triggerTimeoutLogout = true;
                }
            }
            if (!_maintenanceStatusRequested && _app.RemoteConnected)
            {
                _maintenanceStatusRequested = true;
                requestMaintenanceStatus = true;
            }
        }
        if (triggerTimeoutLogout)
        {
            _app.LogoutAll();
            _ = _app.DisconnectRemoteAsync();
        }
        if (requestMaintenanceStatus)
        {
            StartMaintenanceStatusRequest();
        }

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextUnformatted("Server Admin");
        ImGui.Separator();

        var user = string.IsNullOrWhiteSpace(_app.CurrentStaffUsername) ? "—" : _app.CurrentStaffUsername;
        var uid = string.IsNullOrWhiteSpace(_app.CurrentStaffUid) ? "—" : _app.CurrentStaffUid;
        var club = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "—" : _app.CurrentClubId;
        var status = _app.RemoteConnected ? "Online" : "Offline";
        string healthStatus;
        bool? healthDbOk;
        DateTimeOffset? healthServerTime;
        DateTimeOffset? healthCheckedAt;
        bool healthInFlight;
        string healthError;
        string maintenanceStatus;
        bool? maintenanceModeActive;
        bool? maintenanceModePendingEnable;
        DateTimeOffset? maintenanceModeCheckedAt;
        bool maintenanceModeInFlight;
        string maintenanceModeStatus;
        string userLookupStatus;
        string deleteUserStatus;
        string announcementStatus;
        string shutdownStatus;
        lock (_stateLock)
        {
            healthStatus = _healthStatus;
            healthDbOk = _healthDbOk;
            healthServerTime = _healthServerTime;
            healthCheckedAt = _healthCheckedAt;
            healthInFlight = _healthInFlight;
            healthError = _healthError;
            maintenanceStatus = _maintenanceStatus;
            maintenanceModeActive = _maintenanceModeActive;
            maintenanceModePendingEnable = _maintenanceModePendingEnable;
            maintenanceModeCheckedAt = _maintenanceModeCheckedAt;
            maintenanceModeInFlight = _maintenanceModeInFlight;
            maintenanceModeStatus = _maintenanceModeStatus;
            userLookupStatus = _userLookupStatus;
            deleteUserStatus = _deleteUserStatus;
            announcementStatus = _announcementStatus;
            shutdownStatus = _shutdownStatus;
        }

        ImGui.TextUnformatted("Session");
        ImGui.Separator();
        ImGui.TextUnformatted($"Username: {user}");
        ImGui.TextUnformatted($"UID: {uid}");
        ImGui.TextUnformatted($"Current Venue: {club}");
        ImGui.TextUnformatted($"Server Status: {status}");
        ImGui.Spacing();

        ImGui.TextUnformatted("Server Status");
        ImGui.Separator();
        var baseUrl = _app.GetServerBaseUrl();
        ImGui.TextUnformatted($"Base URL: {baseUrl}");
        ImGui.SameLine();
        if (ImGui.Button("Copy##server_base_url"))
        {
            ImGui.SetClipboardText(baseUrl ?? string.Empty);
        }
        ImGui.TextUnformatted($"Health: {healthStatus}");
        if (healthDbOk.HasValue) ImGui.TextUnformatted($"Database: {(healthDbOk.Value ? "OK" : "Fail")}");
        if (healthServerTime.HasValue) ImGui.TextUnformatted($"Server Time: {healthServerTime.Value:yyyy-MM-dd HH:mm}");
        if (healthCheckedAt.HasValue) ImGui.TextUnformatted($"Checked: {healthCheckedAt.Value:HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(healthError)) ImGui.TextUnformatted($"Error: {healthError}");
        ImGui.BeginDisabled(healthInFlight);
        if (ImGui.Button("Run Health Check"))
        {
            StartHealthCheck(baseUrl);
        }
        ImGui.EndDisabled();
        ImGui.Spacing();

        ImGui.TextUnformatted("Maintenance");
        ImGui.Separator();
        var maintenanceModeText = maintenanceModeActive.HasValue ? (maintenanceModeActive.Value ? "Active" : "Inactive") : "Unknown";
        var maintenancePendingText = maintenanceModePendingEnable.HasValue ? (maintenanceModePendingEnable.Value ? "Yes" : "No") : "Unknown";
        ImGui.TextUnformatted($"Maintenance Mode: {maintenanceModeText}");
        ImGui.TextUnformatted($"Enable After Restart: {maintenancePendingText}");
        if (maintenanceModeCheckedAt.HasValue) ImGui.TextUnformatted($"Checked: {maintenanceModeCheckedAt.Value:HH:mm:ss}");
        ImGui.BeginDisabled(maintenanceModeInFlight);
        if (ImGui.Button("Refresh Maintenance Status"))
        {
            StartMaintenanceStatusRequest();
        }
        ImGui.SameLine();
        string toggleLabel;
        bool requestEnable;
        if (maintenanceModeActive.HasValue && maintenanceModeActive.Value)
        {
            toggleLabel = "Disable Maintenance";
            requestEnable = false;
        }
        else if (maintenanceModePendingEnable.HasValue && maintenanceModePendingEnable.Value)
        {
            toggleLabel = "Cancel Pending Enable";
            requestEnable = false;
        }
        else
        {
            toggleLabel = "Enable After Restart";
            requestEnable = true;
        }
        if (ImGui.Button(toggleLabel))
        {
            StartSetMaintenanceMode(requestEnable);
        }
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(maintenanceModeStatus)) ImGui.TextUnformatted(maintenanceModeStatus);
        var btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        if (ImGui.Button("Disconnect", new Vector2(btnW, 0)))
        {
            StartDisconnect();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reconnect", new Vector2(btnW, 0)))
        {
            StartReconnect();
        }
        if (!string.IsNullOrWhiteSpace(maintenanceStatus)) ImGui.TextUnformatted(maintenanceStatus);
        ImGui.Spacing();

        ImGui.TextUnformatted("Announcements");
        ImGui.Separator();
        ImGui.PushItemWidth(360f);
        ImGui.InputTextWithHint("##admin_announcement_message", "Message to users", ref _announcementMessage, 200);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Send Announcement"))
        {
            StartAnnouncement(_announcementMessage);
        }
        if (!string.IsNullOrWhiteSpace(announcementStatus)) ImGui.TextUnformatted(announcementStatus);
        ImGui.Spacing();

        ImGui.TextUnformatted("Shutdown / Restart");
        ImGui.Separator();
        ImGui.PushItemWidth(360f);
        ImGui.InputTextWithHint("##admin_shutdown_message", "Optional message prefix", ref _shutdownMessage, 200);
        ImGui.InputTextWithHint("##admin_shutdown_minutes", "Minutes list (e.g. 30,20,10)", ref _shutdownMinutesInput, 64);
        ImGui.PopItemWidth();
        ImGui.Checkbox("Confirm", ref _shutdownConfirm);
        var btnW2 = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(!_shutdownConfirm);
        if (ImGui.Button("Schedule Shutdown", new Vector2(btnW2, 0)))
        {
            StartScheduleShutdown(_shutdownMessage, _shutdownMinutesInput, false);
        }
        ImGui.SameLine();
        if (ImGui.Button("Schedule Restart", new Vector2(btnW2, 0)))
        {
            StartScheduleShutdown(_shutdownMessage, _shutdownMinutesInput, true);
        }
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(shutdownStatus)) ImGui.TextUnformatted(shutdownStatus);
        ImGui.Spacing();

        ImGui.TextUnformatted("User Tools");
        ImGui.Separator();
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##admin_user_lookup", "Username", ref _userLookupInput, 64);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Check User"))
        {
            StartUserLookup(_userLookupInput);
        }
        if (!string.IsNullOrWhiteSpace(userLookupStatus)) ImGui.TextUnformatted(userLookupStatus);
        ImGui.Spacing();
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##admin_user_delete", "Username to delete", ref _deleteUserInput, 64);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.Checkbox("Confirm", ref _deleteUserConfirm);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_deleteUserInput) || !_deleteUserConfirm);
        if (ImGui.Button("Delete User"))
        {
            StartDeleteUser(_deleteUserInput);
        }
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(deleteUserStatus)) ImGui.TextUnformatted(deleteUserStatus);
    }

    private void StartHealthCheck(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        lock (_stateLock)
        {
            if (_healthInFlight) return;
            _healthInFlight = true;
            _healthCheckStartedAt = DateTimeOffset.UtcNow;
            _healthTimeoutTriggered = false;
            _healthStatus = "Checking...";
            _healthError = string.Empty;
        }
        Task.Run(async () =>
        {
            try
            {
                var url = baseUrl.TrimEnd('/') + "/health";
                var json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                var dbOk = root.TryGetProperty("dbOk", out var dbEl) && dbEl.ValueKind == JsonValueKind.True;
                DateTimeOffset? time = null;
                if (root.TryGetProperty("time", out var timeEl) && timeEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(timeEl.GetString(), out var dt))
                {
                    time = dt;
                }
                lock (_stateLock)
                {
                    if (_healthTimeoutTriggered) return;
                    _healthStatus = ok ? "OK" : "Fail";
                    _healthDbOk = dbOk;
                    _healthServerTime = time;
                    _healthCheckedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    if (_healthTimeoutTriggered) return;
                    _healthStatus = "Fail";
                    _healthError = ex.Message;
                    _healthCheckedAt = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                lock (_stateLock)
                {
                    _healthInFlight = false;
                }
            }
        });
    }

    private void StartMaintenanceStatusRequest()
    {
        lock (_stateLock)
        {
            if (_maintenanceModeInFlight) return;
            _maintenanceModeInFlight = true;
            _maintenanceModeStatus = "Checking maintenance mode...";
        }
        Task.Run(async () =>
        {
            try
            {
                var enabled = await _app.GetMaintenanceModeAsync();
                lock (_stateLock)
                {
                    if (enabled.HasValue)
                    {
                        _maintenanceModeActive = enabled.Value.Active;
                        _maintenanceModePendingEnable = enabled.Value.Pending;
                        _maintenanceModeCheckedAt = DateTimeOffset.UtcNow;
                        _maintenanceModeStatus = string.Empty;
                    }
                    else
                    {
                        _maintenanceModeActive = null;
                        _maintenanceModePendingEnable = null;
                        _maintenanceModeStatus = _app.GetLastServerMessage() ?? "Maintenance status failed";
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _maintenanceModeStatus = ex.Message; }
            }
            finally
            {
                lock (_stateLock) { _maintenanceModeInFlight = false; }
            }
        });
    }

    private void StartSetMaintenanceMode(bool enabled)
    {
        lock (_stateLock)
        {
            if (_maintenanceModeInFlight) return;
            _maintenanceModeInFlight = true;
            _maintenanceModeStatus = enabled ? "Scheduling maintenance mode..." : "Disabling maintenance mode...";
        }
        Task.Run(async () =>
        {
            try
            {
                var ok = await _app.SetMaintenanceModeAsync(enabled);
                lock (_stateLock)
                {
                    if (ok)
                    {
                        _maintenanceModeCheckedAt = DateTimeOffset.UtcNow;
                        if (enabled)
                        {
                            _maintenanceModePendingEnable = true;
                            _maintenanceModeStatus = "Maintenance mode will be enabled after restart";
                        }
                        else
                        {
                            _maintenanceModeActive = false;
                            _maintenanceModePendingEnable = false;
                            _maintenanceModeStatus = "Maintenance mode disabled";
                        }
                    }
                    else
                    {
                        _maintenanceModeStatus = _app.GetLastServerMessage() ?? "Maintenance update failed";
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _maintenanceModeStatus = ex.Message; }
            }
            finally
            {
                lock (_stateLock) { _maintenanceModeInFlight = false; }
            }
        });
    }

    private void StartDisconnect()
    {
        lock (_stateLock)
        {
            _maintenanceStatus = "Disconnecting...";
        }
        Task.Run(async () =>
        {
            try
            {
                await _app.DisconnectRemoteAsync();
                lock (_stateLock) { _maintenanceStatus = "Disconnected"; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _maintenanceStatus = ex.Message; }
            }
        });
    }

    private void StartReconnect()
    {
        lock (_stateLock)
        {
            _maintenanceStatus = "Reconnecting...";
        }
        Task.Run(async () =>
        {
            try
            {
                await _app.DisconnectRemoteAsync();
                var ok = await _app.ConnectRemoteAsync();
                lock (_stateLock) { _maintenanceStatus = ok ? "Reconnected" : "Reconnect failed"; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _maintenanceStatus = ex.Message; }
            }
        });
    }

    private void StartUserLookup(string username)
    {
        var name = (username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_stateLock)
        {
            _userLookupStatus = "Checking...";
        }
        Task.Run(async () =>
        {
            try
            {
                var exists = await _app.CheckUserExistsAsync(name);
                var res = exists.HasValue ? (exists.Value ? "User exists" : "User not found") : "Check failed";
                lock (_stateLock) { _userLookupStatus = res; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _userLookupStatus = ex.Message; }
            }
        });
    }

    private void StartDeleteUser(string username)
    {
        var name = (username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_stateLock)
        {
            _deleteUserStatus = "Deleting...";
        }
        Task.Run(async () =>
        {
            try
            {
                var ok = await _app.DeleteStaffUserAsync(name);
                var msg = ok ? "Deleted" : (_app.GetLastServerMessage() ?? "Delete failed");
                lock (_stateLock) { _deleteUserStatus = msg; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _deleteUserStatus = ex.Message; }
            }
        });
    }

    private void StartAnnouncement(string message)
    {
        var msg = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(msg)) return;
        lock (_stateLock)
        {
            _announcementStatus = "Sending...";
        }
        Task.Run(async () =>
        {
            try
            {
                var ok = await _app.SendServerAnnouncementAsync(msg);
                var res = ok ? "Announcement sent" : (_app.GetLastServerMessage() ?? "Announcement failed");
                lock (_stateLock) { _announcementStatus = res; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _announcementStatus = ex.Message; }
            }
        });
    }

    private void StartScheduleShutdown(string message, string minutesInput, bool restart)
    {
        var minutes = ParseMinutesList(minutesInput);
        lock (_stateLock)
        {
            _shutdownStatus = restart ? "Scheduling restart..." : "Scheduling shutdown...";
        }
        Task.Run(async () =>
        {
            try
            {
                var ok = await _app.ScheduleServerShutdownAsync(message ?? string.Empty, minutes, restart);
                var res = ok ? (restart ? "Restart scheduled" : "Shutdown scheduled") : (_app.GetLastServerMessage() ?? "Schedule failed");
                lock (_stateLock) { _shutdownStatus = res; }
            }
            catch (Exception ex)
            {
                lock (_stateLock) { _shutdownStatus = ex.Message; }
            }
        });
    }

    private static int[] ParseMinutesList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<int>();
        var set = new System.Collections.Generic.HashSet<int>();
        var parts = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out var v) && v > 0 && v <= 1440) set.Add(v);
        }
        if (set.Count == 0) return Array.Empty<int>();
        var arr = new int[set.Count];
        int idx = 0;
        foreach (var v in set)
        {
            arr[idx] = v;
            idx++;
        }
        Array.Sort(arr, (a, b) => b.CompareTo(a));
        return arr;
    }
}
