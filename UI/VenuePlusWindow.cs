using System;
using System.Numerics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using VenuePlus.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Textures.TextureWraps;
using VenuePlus.UI.Components;
using VenuePlus.UI.Modals;
using VenuePlus.Helpers;

namespace VenuePlus.UI;

public sealed class VenuePlusWindow : Window, IDisposable
{
    private const int StatusPollDelayMs = 120;
    private const int StatusPollMaxTicks = 60;
    private readonly VenuePlusApp _app;
    private string _filter = string.Empty;
    private bool _disposed;
    private readonly VipTableComponent _vipTable = new();
    private readonly StaffListComponent _staffList = new();
    private readonly DjListComponent _djList = new();
    private readonly ShiftPlanComponent _shiftPlan = new();
    private readonly AddVipModal _addVip = new();
    private readonly StaffLoginModal _staffLoginModal = new();
    private readonly RegisterModal _registerModal = new();
    private readonly RegisterClubModal _registerClubModal = new();
    private readonly JoinClubModal _joinClubModal = new();
    private string _adminTokenInput = string.Empty;
    private System.DateTimeOffset _staffLastRefresh;
    private readonly SettingsPanelComponent _settingsPanel = new();
    private readonly JobsPanelComponent _jobsPanel = new();
    private bool _showStaffForm;
    private bool _showUserSettingsPanel;
    private bool? _currentCharExists;
    private System.DateTimeOffset _currentCharExistsLastCheck;
    private string _adminPinInput = string.Empty;
    private string _staffUserInput = string.Empty;
    private string _staffPassInput = string.Empty;
    private string _adminLoginStatus = string.Empty;
    private string _staffLoginStatus = string.Empty;
    private bool _showRecoveryForm;
    private bool _manualLoginMode;
    private bool _pendingAutoLoginEnable;
    private string _resetRecoveryCode = string.Empty;
    private string _resetPassword = string.Empty;
    private string _resetStatus = string.Empty;
    private string _birthdayFilter = string.Empty;
    private string _birthdayStatus = string.Empty;
    private System.DateTimeOffset _birthdayLastRefresh;
    private System.DateTimeOffset _serverStatusLastCheck;
    private bool _serverStatusCheckInFlight;
    private readonly HttpClient _serverStatusHttpClient = new();
    private System.DateTimeOffset _serverStatusHealthCheckedAt;
    private System.DateTimeOffset? _serverStatusHealthStartedAt;
    private bool _serverStatusHealthInFlight;
    private bool? _serverStatusHealthOk;
    private bool? _serverStatusMaintenanceActive;
    private VenuePlus.State.StaffUser[] _birthdayUsers = Array.Empty<VenuePlus.State.StaffUser>();
    private string? _birthdayClubId;
    private int _birthdayPageIndex;
    private string _birthdayPageFilter = string.Empty;
    private bool _requestScheduleTab;
    private bool _openStaffLoginModal;
    private IDalamudTextureWrap? _clubLogoTex;
    private IDalamudTextureWrap? _fallbackLogoTex;
    private string? _lastLogoBase64;
    private readonly ITextureProvider _textureProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDalamudTextureWrap> _clubLogoTexCache = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _clubLogoFetchPending = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.HashSet<string> _logoPrefetchDone = new(System.StringComparer.Ordinal);
    private string _clubsFingerprint = string.Empty;
    private int _statsVipCount;
    private int _statsStaffCount;
    private int _statsStaffOnlineCount;

    private enum ServerStatus
    {
        Checking,
        Online,
        Offline,
        Maintenance
    }

    public VenuePlusWindow(VenuePlusApp app, ITextureProvider textureProvider) : base("Venue Plus")
    {
        _app = app;
        _textureProvider = textureProvider;
        Size = new Vector2(660f, 350f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 300),
            MaximumSize = new Vector2(1600, 1200)
        };
        RespectCloseHotkey = true;
        _app.UsersDetailsChanged += OnUsersDetailsChanged;
        _app.UserJobUpdatedEvt += OnUserJobUpdated;
        _app.JobsChanged += OnJobsChangedPanel;
        _app.JobRightsChanged += OnJobRightsChanged;
        _app.JobsChanged += OnJobsChangedStaffList;
        _app.AutoLoginResultEvt += OnAutoLoginResult;
        _app.RememberStaffNeedsPasswordEvt += OnRememberStaffNeedsPassword;
        _app.ClubLogoChanged += OnClubLogoChanged;
        OnClubLogoChanged(_app.CurrentClubLogoBase64);
        _staffList.SetAssignShiftAction(u =>
        {
            _requestScheduleTab = true;
            _shiftPlan.OpenAddFormForUser(u.Uid, u.Username, u.Job);
        });
        _djList.SetAssignShiftAction(dj =>
        {
            _requestScheduleTab = true;
            _shiftPlan.OpenAddFormForDj(dj.DjName);
        });
    }

    public override void OnOpen()
    {
        _ = _app.ConnectRemoteAsync();
        _ = _app.TryAutoLoginAsync();
        _staffLastRefresh = System.DateTimeOffset.MinValue;
        _app.EnsureSelfRights();
        ResetStatusMessages();
        if (!_app.HasStaffSession)
        {
            _showStaffForm = true;
            _showRecoveryForm = false;
            _manualLoginMode = !_app.AutoLoginEnabled;
            _pendingAutoLoginEnable = false;
            var info = _app.GetCurrentCharacter();
            if (info.HasValue) _staffUserInput = info.Value.name + "@" + info.Value.world;
            _currentCharExistsLastCheck = System.DateTimeOffset.MinValue;
        }
    }

    public override void OnClose()
    {
        ResetStatusMessages();
    }

    public override bool DrawConditions()
    {
        return !_app.IsBetweenAreas;
    }

    public override void Draw()
    {
        _app.UpdateCurrentCharacterCache();
        _app.EnsureSelfRights();
        if (_app.HasStaffSession)
        {
            _showStaffForm = false;
            _staffLoginStatus = string.Empty;
            _currentCharExists = true;
        }
        if (!_app.HasStaffSession)
        {
            _showStaffForm = true;
            if (!_app.RemoteConnected)
            {
                _currentCharExists = null;
                _currentCharExistsLastCheck = System.DateTimeOffset.MinValue;
            }
            var shouldCheckGlobal = _currentCharExistsLastCheck == System.DateTimeOffset.MinValue || _currentCharExistsLastCheck < System.DateTimeOffset.UtcNow.AddSeconds(-15);
            var infoGlobal = _app.GetCurrentCharacter();
            if (shouldCheckGlobal && infoGlobal.HasValue)
            {
                _currentCharExistsLastCheck = System.DateTimeOffset.UtcNow;
                var u = infoGlobal.Value.name + "@" + infoGlobal.Value.world;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var exists = await _app.CheckUserExistsAsync(u);
                        _currentCharExists = exists.HasValue ? exists.Value : null;
                    }
                    catch
                    {
                        _currentCharExists = null;
                    }
                });
            }
        }

        var style = ImGui.GetStyle();
        var avail = ImGui.GetContentRegionAvail();
        var leftW = MathF.Min(250f, avail.X * 0.35f);
        var rightW = avail.X - leftW - style.ItemSpacing.X;

        ImGui.BeginChild("LeftPanel", new Vector2(leftW, 0), true);

        PrefetchClubLogos(_app);

        if (!_app.IsOwnerCurrentClub)
        {
            if (_showStaffForm)
            {
                var bw2 = ImGui.CalcTextSize("Back").X + style.FramePadding.X * 2f;
                var infoCur = _app.GetCurrentCharacter();
                if (!infoCur.HasValue)
                {
                    ImGui.TextUnformatted("No character detected.");
                    ImGui.TextWrapped("Please log in with a character first.");
                    ImGui.Spacing();
                }
                else if (_currentCharExists == true)
                {
                    ImGui.Spacing();
                    ImGui.TextUnformatted("Character:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(infoCur.Value.name);
                    ImGui.TextUnformatted("Homeworld:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(infoCur.Value.world);
                    EnsureServerStatus();
                    var serverStatus = GetServerStatus();
                    if (serverStatus != ServerStatus.Online && !_app.IsServerAdminOrKnown)
                    {
                        var statusText = serverStatus switch
                        {
                            ServerStatus.Maintenance => "Server is in maintenance. Login is not possible right now.",
                            ServerStatus.Offline => "Server is offline. Login is not possible right now.",
                            _ => "Server status is being checked. Login fields will appear once online."
                        };
                        ImGui.Spacing();
                        ImGui.TextWrapped(statusText);
                    }
                    else
                    {
                        var autoEnabled = _app.AutoLoginEnabled;
                        if (autoEnabled) _pendingAutoLoginEnable = false;
                        if (!autoEnabled) _manualLoginMode = true;
                        var showManualLogin = _manualLoginMode;
                        if (showManualLogin)
                        {
                            ImGui.PushItemWidth(-1f);
                            ImGui.InputTextWithHint("##staff_pass", "Password", ref _staffPassInput, 64, ImGuiInputTextFlags.Password);
                            var staffPassEnter = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter);
                            ImGui.PopItemWidth();
                            if (staffPassEnter && !string.IsNullOrWhiteSpace(_staffPassInput))
                            {
                                StartStaffLoginWithPhases();
                            }
                            if (autoEnabled)
                            {
                                ImGui.TextUnformatted("Auto Login active");
                            }
                            else
                            {
                                var autoCheck = _pendingAutoLoginEnable;
                                if (ImGui.Checkbox("Enable Auto Login", ref autoCheck))
                                {
                                    _pendingAutoLoginEnable = autoCheck;
                                }
                            }
                        }
                        else
                        {
                            ImGui.TextUnformatted("Auto Login active");
                        }
                        ImGui.BeginDisabled(!_app.RemoteConnected);
                        if (ImGui.Button(showManualLogin ? "Login" : "Login Manual", new Vector2(-1f, 0)))
                        {
                            if (!showManualLogin)
                            {
                                _manualLoginMode = true;
                            }
                            else
                            {
                                StartStaffLoginWithPhases();
                            }
                        }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Login with entered credentials"); ImGui.EndTooltip(); }
                        ImGui.EndDisabled();
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + "##open_settings_icon"))
                        {
                            _app.OpenSettingsWindow();
                        }
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open Settings"); ImGui.EndTooltip(); }
                        ImGui.SameLine();
                        ImGui.BeginDisabled(_currentCharExists != true || !_app.RemoteConnected);
                        if (ImGui.Button("Login current character"))
                        {
                            StartStaffLoginWithPhases();
                        }
                        ImGui.EndDisabled();
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Login using current character"); ImGui.EndTooltip(); }
                        if (!string.IsNullOrEmpty(_staffLoginStatus)) ImGui.TextUnformatted(_staffLoginStatus);
                        ImGui.Spacing();
                        if (!_showRecoveryForm)
                        {
                            if (ImGui.Button("Forgot Password", new Vector2(-1f, 0)))
                            {
                                _showRecoveryForm = true;
                                _resetStatus = string.Empty;
                            }
                            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Use a recovery code to reset your password"); ImGui.EndTooltip(); }
                        }
                        if (_showRecoveryForm)
                        {
                            ImGui.Separator();
                            ImGui.TextUnformatted("Password Recovery");
                            ImGui.TextWrapped("Use a recovery code to reset the password.");
                            ImGui.TextWrapped("Generate a recovery code in Account Settings.");
                            ImGui.Spacing();
                            var recoverInfo = _app.GetCurrentCharacter();
                            var recoverUser = recoverInfo.HasValue ? recoverInfo.Value.name + "@" + recoverInfo.Value.world : string.Empty;
                            ImGui.TextUnformatted("Recovery Account:");
                            ImGui.SameLine();
                            ImGui.TextUnformatted(!string.IsNullOrWhiteSpace(recoverUser) ? recoverUser : "--");
                            ImGui.PushItemWidth(-1f);
                            ImGui.InputTextWithHint("##reset_recovery_code", "Recovery Code", ref _resetRecoveryCode, 32);
                            ImGui.InputTextWithHint("##reset_password", "New Password", ref _resetPassword, 64, ImGuiInputTextFlags.Password);
                            ImGui.PopItemWidth();
                            ImGui.BeginDisabled(!_app.RemoteConnected || string.IsNullOrWhiteSpace(recoverUser) || _currentCharExists != true);
                            if (ImGui.Button("Reset Password", new Vector2(-1f, 0)))
                            {
                                _resetStatus = "Submitting...";
                                var user = recoverUser;
                                var code = _resetRecoveryCode;
                                var pass = _resetPassword;
                                System.Threading.Tasks.Task.Run(async () =>
                                {
                                    var ok = await _app.ResetPasswordByRecoveryCodeAsync(user, code, pass);
                                    _resetStatus = ok ? "Password reset" : "Reset failed";
                                    if (ok)
                                    {
                                        _resetPassword = string.Empty;
                                        _resetRecoveryCode = string.Empty;
                                    }
                                });
                            }
                            ImGui.EndDisabled();
                            if (!string.IsNullOrWhiteSpace(_resetStatus)) ImGui.TextUnformatted(_resetStatus);
                            if (ImGui.Button("Cancel Recovery", new Vector2(-1f, 0)))
                            {
                                _showRecoveryForm = false;
                                _resetStatus = string.Empty;
                                _resetPassword = string.Empty;
                                _resetRecoveryCode = string.Empty;
                            }
                        }
                    }
                }
                else if (_currentCharExists == false)
                {
                    ImGui.TextUnformatted("No account found for your current character.");
                    ImGui.TextWrapped("Please register an account to continue. After registration, you can login and create or join a Venue.");
                    ImGui.Spacing();
                    ImGui.BeginDisabled(!_app.RemoteConnected);
                    if (ImGui.Button("Register Account", new Vector2(-1f, 0))) { _registerModal.Open(); }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create an account for this character"); ImGui.EndTooltip(); }
                }
            }
            else
            {
                ImGui.BeginDisabled(!_app.RemoteConnected || _currentCharExists != true);
                    if (!_app.IsPowerStaff && _currentCharExists == true && ImGui.Button("Login", new Vector2(-1f, 0)))
                {
                    _showStaffForm = true; _staffLoginStatus = string.Empty;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open login"); ImGui.EndTooltip(); }
                ImGui.Spacing();
                ImGui.BeginDisabled(!_app.RemoteConnected);
                if (!_app.IsPowerStaff && ImGui.Button("Register", new Vector2(-1f, 0))) { _registerModal.Open(); }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new account"); ImGui.EndTooltip(); }
            }
        }


        
        if (_app.HasStaffSession)
        {
            var userFull = string.IsNullOrWhiteSpace(_app.CurrentStaffUsername) ? "--" : _app.CurrentStaffUsername;
            var userName = userFull;
            var userWorld = string.Empty;
            var atIdx = userFull.IndexOf('@');
            if (atIdx > 0)
            {
                userName = userFull.Substring(0, atIdx);
                if (atIdx + 1 < userFull.Length) userWorld = userFull.Substring(atIdx + 1);
            }
            else
            {
                var infoCur = _app.GetCurrentCharacter();
                if (infoCur.HasValue) userWorld = infoCur.Value.world ?? string.Empty;
            }
            ImGui.TextUnformatted($"Username: {userName}");
            ImGui.TextUnformatted($"Homeworld: {(string.IsNullOrWhiteSpace(userWorld) ? "--" : userWorld)}");
            var uidLeft = string.IsNullOrWhiteSpace(_app.CurrentStaffUid) ? "--" : _app.CurrentStaffUid!;
            ImGui.TextUnformatted($"UID: {uidLeft}");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_app.CurrentStaffUid));
            if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##copy_uid_left"))
            {
                if (!string.IsNullOrWhiteSpace(_app.CurrentStaffUid)) ImGui.SetClipboardText(_app.CurrentStaffUid!);
            }
            ImGui.EndDisabled();
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Copy UID to clipboard"); ImGui.EndTooltip(); }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted("Venue Selection");
            ImGui.PushItemWidth(-1f);
            var labelCur = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
            var created = _app.GetMyCreatedClubs() ?? Array.Empty<string>();
            var clubs = _app.GetMyClubs() ?? Array.Empty<string>();
            
            if ((created.Length + clubs.Length) > 0 && ImGui.BeginCombo("##club_select", labelCur))
            {
                foreach (var c in created)
                {
                    var label = string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal) ? $"{c} (owned, active)" : $"{c} (owned)";
                    var tex = GetClubLogoTexture(c);
                    if (tex != null) { ImGui.Image(tex.Handle, new Vector2(18f, 18f)); ImGui.SameLine(0f, 6f); }
                    if (ImGui.Selectable(label, string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal))) { _app.SetClubId(c); _staffLastRefresh = System.DateTimeOffset.MinValue; }
                }
                if (created.Length > 0 && clubs.Length > 0) ImGui.Separator();
                foreach (var c in clubs)
                {
                    var isOwned = Array.IndexOf(created, c) >= 0;
                    if (isOwned) continue;
                    var label = string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal) ? $"{c} (member, active)" : $"{c} (member)";
                    var tex = GetClubLogoTexture(c);
                    if (tex != null) { ImGui.Image(tex.Handle, new Vector2(18f, 18f)); ImGui.SameLine(0f, 6f); }
                    if (ImGui.Selectable(label, string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal))) { _app.SetClubId(c); _staffLastRefresh = System.DateTimeOffset.MinValue; }
                }
                ImGui.EndCombo();
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.Button("Venues List", new Vector2(-1f, 0))) { _app.OpenVenuesListWindow(); }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("My Venues List"); ImGui.EndTooltip(); }
                var availTop = ImGui.GetContentRegionAvail().X;
                var halfTop = (availTop - style.ItemSpacing.X) * 0.5f;
                if (ImGui.Button("Create Venue", new Vector2(halfTop, 0))) { _registerClubModal.Open(); }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new venue"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Join Venue", new Vector2(halfTop, 0))) { _joinClubModal.Open(); }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Join an existing venue"); ImGui.EndTooltip(); }
                ImGui.Spacing();
            }
            ImGui.PopItemWidth();
        }
        
        var btnH = ImGui.GetFrameHeight();
        var spacingY = style.ItemSpacing.Y;
        var textH = ImGui.GetTextLineHeightWithSpacing();
        var bottomH = textH * 3f + spacingY + btnH;
        var offsetY = MathF.Max(0f, ImGui.GetContentRegionAvail().Y - bottomH);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        EnsureServerStatus();
        ImGui.TextUnformatted("Server Status:");
        ImGui.SameLine();
        var status = GetServerStatus();
        var statusTextL = status switch
        {
            ServerStatus.Online => "Online",
            ServerStatus.Offline => "Offline",
            ServerStatus.Maintenance => "Maintenance",
            _ => "Checking..."
        };
        var statusColorL = status switch
        {
            ServerStatus.Online => new System.Numerics.Vector4(0.2f, 0.85f, 0.2f, 1f),
            ServerStatus.Maintenance => new System.Numerics.Vector4(0.95f, 0.55f, 0.15f, 1f),
            ServerStatus.Offline => new System.Numerics.Vector4(0.9f, 0.25f, 0.25f, 1f),
            _ => new System.Numerics.Vector4(0.95f, 0.75f, 0.2f, 1f)
        };
        ImGui.PushStyleColor(ImGuiCol.Text, statusColorL);
        ImGui.TextUnformatted(statusTextL);
        ImGui.PopStyleColor();
        var serverTimeText = System.DateTimeOffset.UtcNow.ToString("HH:mm");
        var systemTimeText = System.DateTimeOffset.Now.ToString("HH:mm");
        ImGui.TextUnformatted("Server Time:");
        ImGui.SameLine();
        ImGui.TextUnformatted(serverTimeText);
        ImGui.TextUnformatted("Your Local Time:");
        ImGui.SameLine();
        ImGui.TextUnformatted(systemTimeText);
        ImGui.Spacing();
        var availX = ImGui.GetContentRegionAvail().X;
        var spacingX = style.ItemSpacing.X;
        var gearW = btnH + style.FramePadding.X * 2f;
        var iconW = gearW;
        var logoutW = MathF.Max(0f, availX - (iconW + iconW + gearW) - spacingX * 3f);
        ImGui.BeginDisabled(!(_app.IsOwnerCurrentClub || _app.IsPowerStaff));
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        if (ImGui.Button(FontAwesomeIcon.IdCard.ToIconString() + "##account_icon_bottom", new Vector2(iconW, btnH)))
        {
            _app.OpenSettingsWindowAccount();
            _showUserSettingsPanel = false;
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Account Info"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        if (ImGui.Button(FontAwesomeIcon.Toolbox.ToIconString() + "##qol_icon_bottom", new Vector2(iconW, btnH))) { _app.OpenQolToolsWindow(); }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("QOL Tools"); ImGui.EndTooltip(); }
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + "##open_settings_left", new Vector2(gearW, btnH)))
        {
            _app.OpenSettingsWindow();
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open Settings"); ImGui.EndTooltip(); }
        ImGui.SameLine();
        ImGui.BeginDisabled(!(_app.IsOwnerCurrentClub || _app.IsPowerStaff));
        if (ImGui.Button("Logout", new Vector2(logoutW, 0)))
        {
            _app.LogoutAll();
            _showUserSettingsPanel = false;
            _showStaffForm = false; _adminPinInput = string.Empty; _staffUserInput = string.Empty; _staffPassInput = string.Empty; _adminLoginStatus = string.Empty; _staffLoginStatus = string.Empty;
        }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Logout from all sessions"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        ImGui.EndChild();
        
        ImGui.SameLine();
        ImGui.BeginChild("RightPanel", new Vector2(rightW, 0), true);
        var canView = _app.HasStaffSession;
        if (!canView)
        {
            ImGui.TextUnformatted("Welcome to Venue Plus");
            ImGui.Separator();
            ImGui.TextWrapped("Venue Plus helps manage VIPs, staff roles and venue settings for your venue. Login to unlock features.");
            ImGui.Spacing();
            ImGui.TextUnformatted("Features");
            ImGui.BulletText("Add VIPs with 4/12 weeks or lifetime durations");
            ImGui.BulletText("Purge expired VIP entries");
            ImGui.BulletText("Assign jobs, icons and colors to staff");
            ImGui.BulletText("Invite staff by UID and manage memberships");
            ImGui.BulletText("Manage DJs list and open Twitch links");
            ImGui.BulletText("Create shift plans with time ranges");
            ImGui.BulletText("Customize VIP nameplates and labels");
            ImGui.BulletText("Share public VIP, staff and DJ links");
            ImGui.BulletText("View staff birthdays and reminders");
            ImGui.BulletText("Use QOL tools like Macro Helper and Macro Hotbar");
            ImGui.Spacing();
            ImGui.TextUnformatted("Get Started");
            ImGui.BulletText("Register a staff account");
            ImGui.BulletText("Login, then create or join a venue");
            ImGui.BulletText("Use Settings for account and plugin options");
            ImGui.Spacing();
            var halfN = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            ImGui.Spacing();
            ImGui.TextDisabled("Tip: Copy your UID in the left panel to share it with Venue Owners to be invited to a venue.");
        }
        else if (_showUserSettingsPanel)
        {
            _showUserSettingsPanel = false;
        }
        else if (_app.AccessLoading || !_app.ClubListsLoaded)
        {
            ImGui.TextUnformatted("Loading venue data...");
            ImGui.Separator();
            ImGui.TextWrapped("Please wait while your venue list and permissions are loaded.");
        }
        else if (string.IsNullOrWhiteSpace(_app.CurrentClubId))
        {
            ImGui.TextUnformatted("Welcome to Venue Plus");
            ImGui.Separator();
            ImGui.TextWrapped("Manage VIPs and staff for your venue. To get started:");
            ImGui.BulletText("Register a new Venue if you own a venue.");
            ImGui.BulletText("Or join an existing Venue using join password.");
            ImGui.Spacing();
            var half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            if (ImGui.Button("Create Venue", new Vector2(half, 0))) { _registerClubModal.Open(); }
            ImGui.SameLine();
            if (ImGui.Button("Join Venue", new Vector2(half, 0))) { _joinClubModal.Open(); }
            ImGui.Spacing();
            ImGui.TextUnformatted("Tips");
            ImGui.Separator();
            ImGui.BulletText("Use Settings to manage account and plugin options.");
            ImGui.BulletText("VIPs: add 4/12 weeks or lifetime durations, purge expired.");
            ImGui.BulletText("Venue Plus can be opened with /v+ or /venueplus.");
        }
        else
        {

            var labelVenue = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
            var texCur = !string.IsNullOrWhiteSpace(_app.CurrentClubId) ? GetClubLogoTexture(_app.CurrentClubId!) : null;
            if (texCur != null)
            {
                try
                {
                    ImGui.Image(texCur.Handle, new Vector2(80f, 80f));
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Venue Logo"); ImGui.EndTooltip(); }
                }
                catch { try { texCur?.Dispose(); } catch { } }
                ImGui.SameLine();
            }
            ImGui.BeginGroup();
            var accessR = (_app.AccessLoading && !_app.HasStaffSession) ? "Loading..." : (_app.IsOwnerCurrentClub ? "Owner" : (_app.HasStaffSession ? "Staff" : "Guest"));
            var accessLabel = $"Access: {accessR}";
            var useLocalTimeView = _app.ShowShiftTimesInLocalTime;
            var timeViewLabel = useLocalTimeView ? "Current View: LT" : "Current View: ST";
            var styleTime = ImGui.GetStyle();
            var toggleHeight = ImGui.GetFrameHeight();
            var toggleSize = new Vector2(toggleHeight * 1.6f, toggleHeight);
            var timeViewWidth = ImGui.CalcTextSize(timeViewLabel).X + styleTime.ItemSpacing.X + toggleSize.X;
            var accessStartX = ImGui.GetCursorPosX();
            var accessRightLimit = accessStartX + ImGui.GetContentRegionAvail().X;
            ImGui.TextUnformatted(accessLabel);
            var afterAccessX = ImGui.GetCursorPosX();
            ImGui.SameLine();
            var accessRightX = accessRightLimit - timeViewWidth - 6f;
            if (accessRightX < afterAccessX) accessRightX = afterAccessX;
            ImGui.SetCursorPosX(accessRightX);
            var lineStartY = ImGui.GetCursorPosY();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(timeViewLabel);
            ImGui.SameLine();
            var toggleY = lineStartY + (ImGui.GetTextLineHeightWithSpacing() - toggleHeight) * 0.5f;
            if (toggleY > ImGui.GetCursorPosY()) ImGui.SetCursorPosY(toggleY);
            if (DrawSlideToggle("##time_view_toggle", ref useLocalTimeView, toggleSize))
            {
                _ = _app.SetShowShiftTimesInLocalTimeAsync(useLocalTimeView);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("ST = Server Time\nLT = Local Time (your PC)\nShows shifts in your timezone so you know when they start.");
                ImGui.EndTooltip();
            }
            var jobsR = _app.CurrentStaffJobs;
            if (jobsR != null && jobsR.Length > 0)
            {
                ImGui.TextUnformatted("Job:");
                ImGui.SameLine();
                var rightsR = _app.GetJobRightsCache();
                var defaultColor = ImGui.GetColorU32(ImGuiCol.Text);
                var hasNonUnassigned = false;
                for (int i = 0; i < jobsR.Length; i++)
                {
                    if (!string.Equals(jobsR[i], "Unassigned", StringComparison.Ordinal)) { hasNonUnassigned = true; break; }
                }
                string[] jobsToShow = jobsR;
                if (hasNonUnassigned)
                {
                    var count = 0;
                    for (int i = 0; i < jobsR.Length; i++) { if (!string.Equals(jobsR[i], "Unassigned", StringComparison.Ordinal)) count++; }
                    if (count > 0 && count != jobsR.Length)
                    {
                        var filtered = new string[count];
                        var idx = 0;
                        for (int i = 0; i < jobsR.Length; i++)
                        {
                            var j = jobsR[i];
                            if (!string.Equals(j, "Unassigned", StringComparison.Ordinal)) { filtered[idx] = j; idx++; }
                        }
                        jobsToShow = filtered;
                    }
                }
                if (rightsR != null && jobsToShow.Length > 1)
                {
                    var sortArr = new string[jobsToShow.Length];
                    Array.Copy(jobsToShow, sortArr, jobsToShow.Length);
                    Array.Sort(sortArr, (a, b) =>
                    {
                        var ra = 0;
                        var rb = 0;
                        if (!string.IsNullOrWhiteSpace(a) && rightsR.TryGetValue(a, out var infoA)) ra = infoA.Rank;
                        else if (string.Equals(a, "Owner", StringComparison.Ordinal)) ra = 10;
                        else if (string.Equals(a, "Unassigned", StringComparison.Ordinal)) ra = 0;
                        if (!string.IsNullOrWhiteSpace(b) && rightsR.TryGetValue(b, out var infoB)) rb = infoB.Rank;
                        else if (string.Equals(b, "Owner", StringComparison.Ordinal)) rb = 10;
                        else if (string.Equals(b, "Unassigned", StringComparison.Ordinal)) rb = 0;
                        var cmp = rb.CompareTo(ra);
                        if (cmp != 0) return cmp;
                        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                    });
                    jobsToShow = sortArr;
                }
                var first = true;
                for (int i = 0; i < jobsToShow.Length; i++)
                {
                    var job = jobsToShow[i];
                    if (!first) ImGui.SameLine();
                    first = false;
                    VenuePlus.State.JobRightsInfo? infoR = null;
                    var hasInfo = rightsR != null && rightsR.TryGetValue(job, out infoR);
                    var colR = hasInfo && infoR != null ? ColorUtil.HexToU32(infoR.ColorHex) : defaultColor;
                    var iconR = hasInfo && infoR != null ? IconDraw.ParseIcon(infoR.IconKey) : FontAwesomeIcon.User;
                    IconDraw.IconText(iconR, 0.9f, colR);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(job);
                        ImGui.EndTooltip();
                    }
                }
            }
            else
            {
                ImGui.TextUnformatted("Job: --");
            }
            try { _statsVipCount = _app.GetActive()?.Count ?? 0; } catch { _statsVipCount = 0; }
            ImGui.TextUnformatted($"VIPs: {_statsVipCount}   Staff: {_statsStaffCount}   Online: {_statsStaffOnlineCount}/{_statsStaffCount}");
            ImGui.EndGroup();
            ImGui.Spacing();
            var showShiftTab = _app.HasStaffSession;
            if (ImGui.BeginTabBar("MainTabs"))
            {
            if (ImGui.BeginTabItem("VIPs"))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                ImGui.BeginChild("VipTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                _djList.CloseAddForm();
                _staffList.CloseInviteInline();
                ImGui.Spacing();
                ImGui.PushItemWidth(260f);
                ImGui.InputTextWithHint("##filter", "Search VIPs by name or homeworld", ref _filter, 256);
                ImGui.PopItemWidth();
                var canAddVipTab = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanAddVip);
                var canRemoveVipTab = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanRemoveVip);
                var styleVip = ImGui.GetStyle();
                float addW = canAddVipTab ? (ImGui.CalcTextSize("Add VIP").X + styleVip.FramePadding.X * 2f) : 0f;
                float purgeW = canRemoveVipTab ? (ImGui.CalcTextSize("Purge Expired").X + styleVip.FramePadding.X * 2f) : 0f;
                float btnHVip = ImGui.GetFrameHeight();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                var _vipIconStr = IconDraw.ToIconStringFromKey("ArrowUpRightFromSquare");
                float listW = ImGui.CalcTextSize(_vipIconStr).X + styleVip.FramePadding.X * 2f;
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                int count = (canAddVipTab ? 1 : 0) + (canRemoveVipTab ? 1 : 0) + 1;
                float totalW = addW + purgeW + listW + ((count - 1) * styleVip.ItemSpacing.X);
                var startXVip = ImGui.GetCursorPosX();
                var rightXVip = startXVip + ImGui.GetContentRegionAvail().X - totalW;
                ImGui.SameLine(rightXVip);
                if (canAddVipTab)
                {
                    if (ImGui.Button("Add VIP")) { _vipTable.OpenAddForm(); }
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Add a new VIP"); ImGui.EndTooltip(); }
                }
                if (canRemoveVipTab)
                {
                    if (canAddVipTab) ImGui.SameLine();
                    if (ImGui.Button("Purge Expired")) { _app.PurgeExpired(); }
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Remove expired VIPs"); ImGui.EndTooltip(); }
                }
                if (canAddVipTab || canRemoveVipTab) ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                var clickedVip = ImGui.Button(_vipIconStr + "##vip_list_btn", new Vector2(listW, btnHVip));
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                if (clickedVip) { _app.OpenVipListWindow(); }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open VIP list window"); ImGui.EndTooltip(); }
                ImGui.Separator();
                _vipTable.Draw(_app, _filter);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.BeginTabItem("Staff"))
                {
                    if (ImGui.IsItemActivated()) ResetStatusMessages();
                    ImGui.BeginChild("StaffTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                    _vipTable.CloseAddForm();
                    _djList.CloseAddForm();
                    if (_staffLastRefresh == System.DateTimeOffset.MinValue)
                    {
                        _staffList.TriggerRefresh(_app);
                        _staffLastRefresh = System.DateTimeOffset.UtcNow;
                    }
                    _staffList.Draw(_app);
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
            }
            if (showShiftTab && ImGui.BeginTabItem("Shifts"))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                ImGui.BeginChild("ShiftListTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                _vipTable.CloseAddForm();
                _staffList.CloseInviteInline();
                _djList.CloseAddForm();
                _shiftPlan.DrawStaffShiftTab(_app);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.BeginTabItem("DJs"))
                {
                    if (ImGui.IsItemActivated()) ResetStatusMessages();
                    ImGui.BeginChild("DjsTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                    _vipTable.CloseAddForm();
                    _staffList.CloseInviteInline();
                    _djList.Draw(_app);
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.BeginTabItem("Birthdays"))
                {
                    if (ImGui.IsItemActivated()) ResetStatusMessages();
                    ImGui.BeginChild("BirthdayTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                    _vipTable.CloseAddForm();
                    _djList.CloseAddForm();
                    _staffList.CloseInviteInline();
                    var clubId = _app.CurrentClubId;
                    if (!string.Equals(_birthdayClubId, clubId, StringComparison.Ordinal))
                    {
                        _birthdayClubId = clubId;
                        _birthdayLastRefresh = System.DateTimeOffset.MinValue;
                        _birthdayUsers = Array.Empty<VenuePlus.State.StaffUser>();
                    }
                    if (_birthdayLastRefresh == System.DateTimeOffset.MinValue)
                    {
                        TriggerBirthdayRefresh();
                        _birthdayLastRefresh = System.DateTimeOffset.UtcNow;
                    }
                    ImGui.Spacing();
                    ImGui.PushItemWidth(260f);
                    ImGui.InputTextWithHint("##birthday_filter", "Search by username", ref _birthdayFilter, 128);
                    ImGui.PopItemWidth();
                    if (!string.IsNullOrEmpty(_birthdayStatus)) ImGui.TextUnformatted(_birthdayStatus);
                    var users = _birthdayUsers ?? Array.Empty<VenuePlus.State.StaffUser>();
                    var list = new System.Collections.Generic.List<(VenuePlus.State.StaffUser User, System.DateTime Next, int Days)>();
                    var now = DateTime.UtcNow.Date;
                    var filter = _birthdayFilter;
                    for (int i = 0; i < users.Length; i++)
                    {
                        var u = users[i];
                        if (!u.Birthday.HasValue) continue;
                        if (!string.IsNullOrWhiteSpace(filter))
                        {
                            var uname = u.Username ?? string.Empty;
                            if (uname.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        }
                        var b = u.Birthday.Value.UtcDateTime.Date;
                        var month = b.Month;
                        var day = b.Day;
                        var year = now.Year;
                        var daysInMonth = DateTime.DaysInMonth(year, month);
                        if (day > daysInMonth) day = daysInMonth;
                        var next = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                        if (next.Date < now)
                        {
                            year++;
                            daysInMonth = DateTime.DaysInMonth(year, month);
                            if (day > daysInMonth) day = daysInMonth;
                            next = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                        }
                        var days = (int)(next.Date - now).TotalDays;
                        list.Add((u, next, days));
                    }
                    list.Sort((a, b) =>
                    {
                        var comp = a.Days.CompareTo(b.Days);
                        if (comp != 0) return comp;
                        return string.Compare(a.User.Username, b.User.Username, StringComparison.Ordinal);
                    });
                    if (!string.Equals(_birthdayPageFilter, filter, StringComparison.Ordinal))
                    {
                        _birthdayPageFilter = filter ?? string.Empty;
                        _birthdayPageIndex = 0;
                    }
                    const int pageSize = 15;
                    var totalCount = list.Count;
                    var totalPages = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
                    if (_birthdayPageIndex >= totalPages) _birthdayPageIndex = totalPages - 1;
                    if (_birthdayPageIndex < 0) _birthdayPageIndex = 0;
                    var startIndex = _birthdayPageIndex * pageSize;
                    var pageCount = Math.Min(pageSize, Math.Max(0, totalCount - startIndex));
                    var pageList = pageCount > 0 ? list.GetRange(startIndex, pageCount) : new System.Collections.Generic.List<(VenuePlus.State.StaffUser User, System.DateTime Next, int Days)>();
                    if (totalCount == 0)
                    {
                        ImGui.TextUnformatted("No birthdays set");
                    }
                    else
                    {
                        var birthdayStyle = ImGui.GetStyle();
                        var todayColor = birthdayStyle.Colors[(int)ImGuiCol.PlotHistogramHovered];
                        var soonColor = birthdayStyle.Colors[(int)ImGuiCol.PlotHistogram];
                        var headerColor = birthdayStyle.Colors[(int)ImGuiCol.TextDisabled];
                        var cakeIcon = IconDraw.ToIconStringFromKey("BirthdayCake");
                        ImGui.Spacing();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.TextUnformatted(cakeIcon);
                        ImGui.PopFont();
                        ImGui.SameLine(0f, 6f);
                        ImGui.TextUnformatted("Birthday List");
                        ImGui.SameLine(0f, 6f);
                        ImGui.TextDisabled($"({totalCount})");
                        ImGui.Separator();
                        ImGui.BeginDisabled(_birthdayPageIndex <= 0);
                        if (ImGui.Button("Prev##birthday_page_prev")) { _birthdayPageIndex--; }
                        ImGui.EndDisabled();
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"Page {_birthdayPageIndex + 1} / {totalPages}");
                        ImGui.SameLine();
                        ImGui.BeginDisabled(_birthdayPageIndex >= totalPages - 1);
                        if (ImGui.Button("Next##birthday_page_next")) { _birthdayPageIndex++; }
                        ImGui.EndDisabled();
                        ImGui.Separator();
                        bool hasToday = false;
                        bool hasUpcoming = false;
                        for (int i = 0; i < pageList.Count; i++)
                        {
                            if (pageList[i].Days == 0) hasToday = true;
                            else hasUpcoming = true;
                        }
                        ImGui.Columns(3, "birthday_cols", false);
                        ImGui.SetColumnWidth(0, 240f);
                        ImGui.TextUnformatted("User");
                        ImGui.NextColumn();
                        ImGui.TextUnformatted("Birthday");
                        ImGui.NextColumn();
                        ImGui.TextUnformatted("Next");
                        ImGui.NextColumn();
                        ImGui.Separator();
                        if (hasToday)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, todayColor);
                            ImGui.TextUnformatted("Today");
                            ImGui.PopStyleColor();
                            ImGui.NextColumn();
                            ImGui.TextUnformatted(string.Empty);
                            ImGui.NextColumn();
                            ImGui.TextUnformatted(string.Empty);
                            ImGui.NextColumn();
                            ImGui.Separator();
                            for (int i = 0; i < pageList.Count; i++)
                            {
                                var entry = pageList[i];
                                if (entry.Days != 0) continue;
                                var name = entry.User.Username ?? string.Empty;
                                var birthday = entry.User.Birthday.HasValue ? entry.User.Birthday.Value.UtcDateTime.ToString("MMM dd", CultureInfo.InvariantCulture) : "—";
                                var nextLabel = "Today";
                                ImGui.PushStyleColor(ImGuiCol.Text, todayColor);
                                ImGui.TextUnformatted(name);
                                ImGui.NextColumn();
                                ImGui.TextUnformatted(birthday);
                                ImGui.NextColumn();
                                ImGui.TextUnformatted(nextLabel);
                                ImGui.NextColumn();
                                ImGui.PopStyleColor();
                            }
                        }
                        if (hasUpcoming)
                        {
                            if (hasToday) ImGui.Separator();
                            ImGui.PushStyleColor(ImGuiCol.Text, headerColor);
                            ImGui.TextUnformatted("Upcoming");
                            ImGui.PopStyleColor();
                            ImGui.NextColumn();
                            ImGui.TextUnformatted(string.Empty);
                            ImGui.NextColumn();
                            ImGui.TextUnformatted(string.Empty);
                            ImGui.NextColumn();
                            ImGui.Separator();
                            for (int i = 0; i < pageList.Count; i++)
                            {
                                var entry = pageList[i];
                                if (entry.Days == 0) continue;
                                var name = entry.User.Username ?? string.Empty;
                                var birthday = entry.User.Birthday.HasValue ? entry.User.Birthday.Value.UtcDateTime.ToString("MMM dd", CultureInfo.InvariantCulture) : "—";
                                var nextLabel = entry.Days == 1 ? "Tomorrow" : $"{entry.Next.ToString("MMM dd", CultureInfo.InvariantCulture)} ({entry.Days}d)";
                                bool useColor = entry.Days <= 7;
                                if (useColor) ImGui.PushStyleColor(ImGuiCol.Text, soonColor);
                                ImGui.TextUnformatted(name);
                                ImGui.NextColumn();
                                ImGui.TextUnformatted(birthday);
                                ImGui.NextColumn();
                                ImGui.TextUnformatted(nextLabel);
                                ImGui.NextColumn();
                                if (useColor) ImGui.PopStyleColor();
                            }
                        }
                        ImGui.Columns(1);
                    }
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
            }
            var scheduleFlags = _requestScheduleTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (showShiftTab && ImGui.BeginTabItem("Schedule", scheduleFlags))
            {
                if (_requestScheduleTab) _requestScheduleTab = false;
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                ImGui.BeginChild("ShiftsTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                _vipTable.CloseAddForm();
                _staffList.CloseInviteInline();
                _djList.CloseAddForm();
                _shiftPlan.Draw(_app);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (_app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanManageJobs))
            {
                if (ImGui.BeginTabItem("Roles"))
                {
                    if (ImGui.IsItemActivated()) ResetStatusMessages();
                    ImGui.BeginChild("RolesTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                    _vipTable.CloseAddForm();
                    _djList.CloseAddForm();
                    _staffList.CloseInviteInline();
                    _jobsPanel.Draw(_app);
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
            }
            var canViewSettings = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanManageVenueSettings);
            if (canViewSettings && ImGui.BeginTabItem("Venue Settings"))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                ImGui.BeginChild("SettingsTabContent", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                _vipTable.CloseAddForm();
                _djList.CloseAddForm();
                _staffList.CloseInviteInline();
                _settingsPanel.Draw(_app);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
        if (_openStaffLoginModal)
        {
            ImGui.OpenPopup("Login");
            _openStaffLoginModal = false;
        }
        _staffLoginModal.Draw(_app);
        
        
        _registerModal.Draw(_app);
        _registerClubModal.Draw(_app);
        _joinClubModal.Draw(_app);
        _addVip.Draw(_app);
        
    }

    private void TriggerBirthdayRefresh()
    {
        _birthdayStatus = "Refreshing...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            var list = await _app.FetchStaffUsersDetailedAsync();
            _birthdayUsers = list ?? Array.Empty<VenuePlus.State.StaffUser>();
            _birthdayStatus = string.Empty;
        });
    }

    private void OnAutoLoginResult(bool adminOk, bool staffOk)
    {
        _staffLoginStatus = staffOk ? "Auto-login successful" : "Auto-login failed";
        if (staffOk)
        {
            _showStaffForm = false;
            _currentCharExists = true;
        }
    }

    private void StartStaffLoginWithPhases()
    {
        var pass = _staffPassInput;
        if (string.IsNullOrWhiteSpace(pass)) return;
        _staffLoginStatus = "Authenticating...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            var ok = await _app.StaffLoginAsync(string.Empty, pass);
            if (!ok)
            {
                _staffLoginStatus = "Login failed";
                return;
            }
            if (_pendingAutoLoginEnable)
            {
                await _app.SetAutoLoginEnabledAsync(true);
                await _app.RememberStaffLoginWithPasswordAsync(pass);
                _pendingAutoLoginEnable = false;
            }
            if (_app.AccessLoading)
            {
                _staffLoginStatus = "Loading profile...";
                for (int i = 0; i < StatusPollMaxTicks; i++)
                {
                    await System.Threading.Tasks.Task.Delay(StatusPollDelayMs);
                    if (!_app.AccessLoading) break;
                }
            }
            _staffLoginStatus = "Login successful";
            _showStaffForm = false;
            _staffUserInput = string.Empty;
            _staffPassInput = string.Empty;
            _showRecoveryForm = false;
        });
    }

    private void ResetStatusMessages()
    {
        _adminLoginStatus = string.Empty;
        _staffLoginStatus = string.Empty;
        _resetStatus = string.Empty;
        _birthdayStatus = string.Empty;
        _settingsPanel.ResetStatusMessages();
        _jobsPanel.ResetStatusMessages();
    }

    private void EnsureServerConnection()
    {
        if (_app.RemoteConnected) return;
        if (_serverStatusCheckInFlight) return;
        var now = System.DateTimeOffset.UtcNow;
        if (_serverStatusLastCheck != System.DateTimeOffset.MinValue && _serverStatusLastCheck > now.AddSeconds(-5)) return;
        _serverStatusLastCheck = now;
        _serverStatusCheckInFlight = true;
        _app.SetAutoLoginStatusCheckInFlight(true);
        System.Threading.Tasks.Task.Run(async () =>
        {
            try { await _app.ConnectRemoteAsync(); }
            finally
            {
                _serverStatusCheckInFlight = false;
                _app.SetAutoLoginStatusCheckInFlight(_serverStatusCheckInFlight || _serverStatusHealthInFlight);
            }
        });
    }

    private void EnsureServerStatus()
    {
        var now = System.DateTimeOffset.UtcNow;
        if (_serverStatusHealthInFlight && _serverStatusHealthStartedAt.HasValue)
        {
            var elapsed = now - _serverStatusHealthStartedAt.Value;
            if (elapsed >= TimeSpan.FromSeconds(6))
            {
                _serverStatusHealthInFlight = false;
                _serverStatusHealthOk = false;
                _serverStatusMaintenanceActive = null;
                _serverStatusHealthCheckedAt = now;
                _app.SetAutoLoginServerOnline(false);
                _app.SetAutoLoginStatusCheckInFlight(_serverStatusCheckInFlight || _serverStatusHealthInFlight);
            }
        }
        EnsureServerConnection();
        _app.SetAutoLoginMaintenanceActive(_serverStatusMaintenanceActive);
        _app.SetAutoLoginServerOnline(_serverStatusHealthOk);
        _app.SetAutoLoginStatusCheckInFlight(_serverStatusCheckInFlight || _serverStatusHealthInFlight);
        if (_app.HasStaffSession && !_app.IsServerAdmin) return;
        if (_serverStatusHealthInFlight) return;
        if (_serverStatusHealthCheckedAt != System.DateTimeOffset.MinValue && _serverStatusHealthCheckedAt > now.AddSeconds(-5)) return;
        StartServerStatusHealthCheck(_app.GetServerBaseUrl());
    }

    private ServerStatus GetServerStatus()
    {
        if (_serverStatusMaintenanceActive.HasValue && _serverStatusMaintenanceActive.Value) return ServerStatus.Maintenance;
        if (_app.RemoteConnected) return ServerStatus.Online;
        if (_serverStatusCheckInFlight || _serverStatusHealthInFlight) return ServerStatus.Checking;
        if (_serverStatusHealthOk.HasValue) return _serverStatusHealthOk.Value ? ServerStatus.Online : ServerStatus.Offline;
        return ServerStatus.Checking;
    }

    private void StartServerStatusHealthCheck(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        if (_serverStatusHealthInFlight) return;
        _serverStatusHealthInFlight = true;
        _app.SetAutoLoginStatusCheckInFlight(_serverStatusCheckInFlight || _serverStatusHealthInFlight);
        _serverStatusHealthStartedAt = System.DateTimeOffset.UtcNow;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var url = baseUrl.TrimEnd('/') + "/health";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var json = await _serverStatusHttpClient.GetStringAsync(url, cts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                bool? maintenance = null;
                if (root.TryGetProperty("maintenanceMode", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                {
                    maintenance = m.GetBoolean();
                }
                _serverStatusHealthOk = ok;
                _serverStatusMaintenanceActive = maintenance;
                _serverStatusHealthCheckedAt = System.DateTimeOffset.UtcNow;
                _app.SetAutoLoginServerOnline(ok);
                _app.SetAutoLoginMaintenanceActive(maintenance);
            }
            catch
            {
                _serverStatusHealthOk = false;
                _serverStatusMaintenanceActive = null;
                _serverStatusHealthCheckedAt = System.DateTimeOffset.UtcNow;
                _app.SetAutoLoginServerOnline(false);
            }
            finally
            {
                _serverStatusHealthInFlight = false;
                _app.SetAutoLoginStatusCheckInFlight(_serverStatusCheckInFlight || _serverStatusHealthInFlight);
            }
        });
    }


    private void DrawWrappedButton(string label, Action onClick)
    {
        var style = ImGui.GetStyle();
        var w = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
        var avail = ImGui.GetContentRegionAvail().X;
        if (w > avail) ImGui.NewLine();
        if (ImGui.Button(label)) onClick();
        ImGui.SameLine();
    }

    private static bool DrawSlideToggle(string id, ref bool value, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        var toggled = false;
        if (ImGui.IsItemClicked())
        {
            value = !value;
            toggled = true;
        }
        var drawList = ImGui.GetWindowDrawList();
        var radius = size.Y * 0.5f;
        var bgColor = value ? ImGui.GetColorU32(ImGuiCol.FrameBgActive) : ImGui.GetColorU32(ImGuiCol.FrameBg);
        var knobColor = ImGui.GetColorU32(value ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab);
        drawList.AddRectFilled(pos, pos + size, bgColor, radius);
        var knobX = value ? (pos.X + size.X - radius) : (pos.X + radius);
        var knobCenter = new Vector2(knobX, pos.Y + radius);
        var knobRadius = MathF.Max(1f, radius - 2f);
        drawList.AddCircleFilled(knobCenter, knobRadius, knobColor);
        return toggled;
    }


    public void OpenAddDialogWithPrefill(string name, string world, VenuePlus.State.VipDuration duration)
    {
        _addVip.Open();
        IsOpen = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _app.UsersDetailsChanged -= OnUsersDetailsChanged;
        _app.UserJobUpdatedEvt -= OnUserJobUpdated;
        _app.JobsChanged -= OnJobsChangedPanel;
        _app.JobRightsChanged -= OnJobRightsChanged;
        _app.JobsChanged -= OnJobsChangedStaffList;
        _app.AutoLoginResultEvt -= OnAutoLoginResult;
        _app.RememberStaffNeedsPasswordEvt -= OnRememberStaffNeedsPassword;
        _app.ClubLogoChanged -= OnClubLogoChanged;
        try { _clubLogoTex?.Dispose(); } catch { }
        _clubLogoTex = null;
        try { _fallbackLogoTex?.Dispose(); } catch { }
        _fallbackLogoTex = null;
        foreach (var kv in _clubLogoTexCache) { try { kv.Value.Dispose(); } catch { } }
        _clubLogoTexCache.Clear();
    }

    private void OnUsersDetailsChanged(VenuePlus.State.StaffUser[] users)
    {
        _staffList.SetUsersFromServer(_app, users);
        _shiftPlan.SetUsersFromServer(users);
        _statsStaffCount = users?.Length ?? 0;
        _birthdayUsers = users ?? Array.Empty<VenuePlus.State.StaffUser>();
        _birthdayLastRefresh = System.DateTimeOffset.UtcNow;
        var online = 0;
        var arr = users ?? Array.Empty<VenuePlus.State.StaffUser>();
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i].IsOnline) online++;
        }
        _statsStaffOnlineCount = online;
    }

    private void OnUserJobUpdated(string username, string job, string[] jobs)
    {
        _staffList.ApplyUserJobUpdate(_app, username, job, jobs);
    }

    private void OnJobsChangedPanel(string[] jobs)
    {
        _jobsPanel.SetJobs(jobs);
    }

    private void OnJobRightsChanged(System.Collections.Generic.Dictionary<string, VenuePlus.State.JobRightsInfo>? rights)
    {
        _jobsPanel.SetRights(rights);
    }

    private void OnJobsChangedStaffList(string[] jobs)
    {
        _staffList.SetJobOptions(jobs);
    }


    private void OnRememberStaffNeedsPassword()
    {
        _openStaffLoginModal = true;
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

    private void OnClubLogoChanged(string? base64)
    {
        try
        {
            if (string.Equals(_lastLogoBase64 ?? string.Empty, base64 ?? string.Empty, StringComparison.Ordinal)) return;
            _lastLogoBase64 = base64 ?? string.Empty;
            try { _clubLogoTex?.Dispose(); } catch { }
            _clubLogoTex = null;
            if (string.IsNullOrWhiteSpace(base64)) return;
            var bytes = Convert.FromBase64String(base64!);
            try
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var tex = await _textureProvider.CreateFromImageAsync(bytes);
                        _clubLogoTex = tex;
                        var id = _app.CurrentClubId;
                        var okId = !string.IsNullOrWhiteSpace(id);
                        if (okId)
                        {
                            if (_clubLogoTexCache.TryGetValue(id!, out var old)) { try { old.Dispose(); } catch { } }
                            _clubLogoTexCache[id!] = tex;
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
        catch { }
    }

    private IDalamudTextureWrap? GetClubLogoTexture(string clubId)
    {
        EnsureFallbackLogoTexture();
        var cur = _app.CurrentClubId;
        if (!string.IsNullOrWhiteSpace(cur) && string.Equals(cur, clubId, StringComparison.Ordinal) && _clubLogoTex != null)
        {
            try { var _ = _clubLogoTex.Handle; return _clubLogoTex; }
            catch { try { _clubLogoTex?.Dispose(); } catch { } _clubLogoTex = null; }
        }
        if (_clubLogoTexCache.TryGetValue(clubId, out var t))
        {
            try { var _ = t.Handle; return t; }
            catch { try { t.Dispose(); } catch { } _clubLogoTexCache.TryRemove(clubId, out _); }
        }
        if (_clubLogoFetchPending.ContainsKey(clubId))
        {
            if (_fallbackLogoTex != null)
            {
                try { var _ = _fallbackLogoTex.Handle; return _fallbackLogoTex; } catch { try { _fallbackLogoTex?.Dispose(); } catch { } _fallbackLogoTex = null; }
            }
            return null;
        }
        if (!_clubLogoFetchPending.TryAdd(clubId, 1)) return null;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var logo = _app.GetClubLogoForClub(clubId);
                if (string.IsNullOrWhiteSpace(logo)) logo = await _app.FetchClubLogoForClubAsync(clubId);
                if (!string.IsNullOrWhiteSpace(logo))
                {
                    var bytes = Convert.FromBase64String(logo!);
                    try
                    {
                        var tex = await _textureProvider.CreateFromImageAsync(bytes);
                        if (_clubLogoTexCache.TryGetValue(clubId, out var old)) { try { old.Dispose(); } catch { } }
                        _clubLogoTexCache[clubId] = tex;
                    }
                    catch { }
                }
                else
                {
                    if (_clubLogoTexCache.TryGetValue(clubId, out var old))
                    {
                        var isCur = !string.IsNullOrWhiteSpace(_app.CurrentClubId) && string.Equals(_app.CurrentClubId, clubId, StringComparison.Ordinal);
                        if (isCur && ReferenceEquals(old, _clubLogoTex)) { _clubLogoTexCache.TryRemove(clubId, out _); }
                        else { try { old.Dispose(); } catch { } _clubLogoTexCache.TryRemove(clubId, out _); }
                    }
                }
            }
            finally
            {
                _clubLogoFetchPending.TryRemove(clubId, out _);
            }
        });
        if (_fallbackLogoTex != null)
        {
            try { var _ = _fallbackLogoTex.Handle; return _fallbackLogoTex; } catch { try { _fallbackLogoTex?.Dispose(); } catch { } _fallbackLogoTex = null; }
        }
        return null;
    }
    private void PrefetchClubLogos(VenuePlusApp app)
    {
        var created = app.GetMyCreatedClubs() ?? Array.Empty<string>();
        var clubs = app.GetMyClubs() ?? Array.Empty<string>();
        var fingerprintNew = string.Join("|", created) + "||" + string.Join("|", clubs);
        if (string.Equals(_clubsFingerprint, fingerprintNew, StringComparison.Ordinal)) return;
        _clubsFingerprint = fingerprintNew;
        _logoPrefetchDone.Clear();
        foreach (var c in created)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (_logoPrefetchDone.Contains(c)) continue;
            _logoPrefetchDone.Add(c);
            _ = GetClubLogoTexture(c);
        }
        foreach (var c in clubs)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (_logoPrefetchDone.Contains(c)) continue;
            _logoPrefetchDone.Add(c);
            _ = GetClubLogoTexture(c);
        }
    }
}
