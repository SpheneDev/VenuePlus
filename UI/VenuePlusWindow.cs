using System;
using System.Numerics;
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
    private readonly VenuePlusApp _app;
    private string _filter = string.Empty;
    private bool _disposed;
    private readonly VipTableComponent _vipTable = new();
    private readonly StaffListComponent _staffList = new();
    private readonly DjListComponent _djList = new();
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
    private bool _currentCharExists;
    private System.DateTimeOffset _currentCharExistsLastCheck;
    private string _adminPinInput = string.Empty;
    private string _staffUserInput = string.Empty;
    private string _staffPassInput = string.Empty;
    private string _adminLoginStatus = string.Empty;
    private string _staffLoginStatus = string.Empty;
    private bool _openStaffLoginModal;
    private IDalamudTextureWrap? _clubLogoTex;
    private string? _lastLogoBase64;
    private readonly ITextureProvider _textureProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDalamudTextureWrap> _clubLogoTexCache = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _clubLogoFetchPending = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.HashSet<string> _logoPrefetchDone = new(System.StringComparer.Ordinal);
    private string _clubsFingerprint = string.Empty;
    private readonly System.Collections.Generic.List<(string Message, System.DateTimeOffset ExpiresAt)> _notifications = new();
    private int _statsVipCount;
    private int _statsStaffCount;

    public VenuePlusWindow(VenuePlusApp app, ITextureProvider textureProvider) : base("Venue Plus")
    {
        _app = app;
        _textureProvider = textureProvider;
        Size = new Vector2(660f, 350f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 300),
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
        _app.Notification += OnNotification;
        OnClubLogoChanged(_app.CurrentClubLogoBase64);
    }

    public override void OnOpen()
    {
        _ = _app.ConnectRemoteAsync();
        _ = _app.TryAutoLoginAsync();
        _staffLastRefresh = System.DateTimeOffset.MinValue;
        _app.EnsureSelfRights();
        if (!_app.HasStaffSession)
        {
            _showStaffForm = true;
            var info = _app.GetCurrentCharacter();
            if (info.HasValue) _staffUserInput = info.Value.name + "@" + info.Value.world;
            _currentCharExistsLastCheck = System.DateTimeOffset.MinValue;
        }
    }

    public override void OnClose()
    {
        
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
            var shouldCheckGlobal = _currentCharExistsLastCheck == System.DateTimeOffset.MinValue || _currentCharExistsLastCheck < System.DateTimeOffset.UtcNow.AddSeconds(-15);
            var infoGlobal = _app.GetCurrentCharacter();
            if (shouldCheckGlobal && infoGlobal.HasValue)
            {
                _currentCharExistsLastCheck = System.DateTimeOffset.UtcNow;
                var u = infoGlobal.Value.name + "@" + infoGlobal.Value.world;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try { _currentCharExists = (await _app.CheckUserExistsAsync(u)) ?? false; } catch { _currentCharExists = false; }
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
                if (_currentCharExists)
                {
                    ImGui.Spacing();
                    var infoCur = _app.GetCurrentCharacter();
                    ImGui.TextUnformatted("Character:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(infoCur.HasValue ? infoCur.Value.name : "--");
                    ImGui.TextUnformatted("Homeworld:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(infoCur.HasValue ? infoCur.Value.world : "--");
                    ImGui.PushItemWidth(-1f);
                    ImGui.InputTextWithHint("##staff_pass", "Password", ref _staffPassInput, 64, ImGuiInputTextFlags.Password);
                    var staffPassEnter = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter);
                    ImGui.PopItemWidth();
                    var autoEnabled = _app.AutoLoginEnabled;
                    if (ImGui.Checkbox("Enable Auto Login", ref autoEnabled))
                    {
                        _app.SetAutoLoginEnabledAsync(autoEnabled).GetAwaiter().GetResult();
                        if (autoEnabled) _app.SetRememberStaffLoginAsync(true).GetAwaiter().GetResult();
                    }
                    if (staffPassEnter && !string.IsNullOrWhiteSpace(_staffPassInput))
                    {
                        _staffLoginStatus = "Submitting...";
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await _app.StaffLoginAsync(string.Empty, _staffPassInput);
                            _staffLoginStatus = ok ? "Login successful" : "Login failed";
                            if (ok)
                            {
                                _showStaffForm = false;
                                _staffUserInput = string.Empty; _staffPassInput = string.Empty;
                            }
                        });
                    }
                    ImGui.BeginDisabled(!_app.RemoteConnected);
                    if (ImGui.Button("Login", new Vector2(-1f, 0)))
                    {
                        _staffLoginStatus = "Submitting...";
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await _app.StaffLoginAsync(string.Empty, _staffPassInput);
                            _staffLoginStatus = ok ? "Login successful" : "Login failed";
                            if (ok)
                            {
                                _showStaffForm = false;
                                _staffUserInput = string.Empty; _staffPassInput = string.Empty;
                            }
                        });
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
                    ImGui.BeginDisabled(!_currentCharExists || !_app.RemoteConnected);
                    if (ImGui.Button("Login current character"))
                    {
                        _staffLoginStatus = "Submitting...";
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await _app.StaffLoginAsync(string.Empty, _staffPassInput);
                            _staffLoginStatus = ok ? "Login successful" : "Login failed";
                            if (ok)
                            {
                                _showStaffForm = false;
                                _staffUserInput = string.Empty; _staffPassInput = string.Empty;
                            }
                        });
                    }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Login using current character"); ImGui.EndTooltip(); }
                    if (!string.IsNullOrEmpty(_staffLoginStatus)) ImGui.TextUnformatted(_staffLoginStatus);
                    ImGui.Spacing();
                }
                else
                {
                    ImGui.TextUnformatted("No account found for your current character.");
                    ImGui.TextWrapped("Please register an account to continue. After registration, you can login and create or join a Venue.");
                    ImGui.Spacing();
                    ImGui.BeginDisabled(!_app.RemoteConnected);
                    if (ImGui.Button("Register Account", new Vector2(-1f, 0))) { _registerModal.Open(); }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a staff account for this character"); ImGui.EndTooltip(); }
                }
            }
            else
            {
                ImGui.BeginDisabled(!_app.RemoteConnected || !_currentCharExists);
                if (!_app.IsPowerStaff && _currentCharExists && ImGui.Button("Login", new Vector2(-1f, 0)))
                {
                    _showStaffForm = true; _staffLoginStatus = string.Empty;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open login"); ImGui.EndTooltip(); }
                ImGui.Spacing();
                ImGui.BeginDisabled(!_app.RemoteConnected);
                if (!_app.IsPowerStaff && ImGui.Button("Register", new Vector2(-1f, 0))) { _registerModal.Open(); }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new staff account"); ImGui.EndTooltip(); }
            }
        }


        var btnH_top = ImGui.GetFrameHeight();
        var spacingY_top = style.ItemSpacing.Y;
        var extraRows_top = _app.HasStaffSession ? 1 : 0;
        var bottomBlockH_top = btnH_top + (extraRows_top > 0 ? (btnH_top + spacingY_top) : 0f);
        var fudgeTop = 10f;
        var topH = MathF.Max(0f, ImGui.GetContentRegionAvail().Y - bottomBlockH_top - fudgeTop);
        ImGui.BeginChild("LeftTopContent", new Vector2(0, topH), false);
        if (_app.HasStaffSession)
        {
            ImGui.TextUnformatted("User");
            ImGui.Separator();
            if (_app.HasStaffSession)
            {
                var user = string.IsNullOrWhiteSpace(_app.CurrentStaffUsername) ? "--" : _app.CurrentStaffUsername;
                ImGui.TextUnformatted($"Username: {user}");
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
            }
            ImGui.Spacing();
            ImGui.TextUnformatted("Venue");
            ImGui.Separator();
            ImGui.TextUnformatted("Venue Selection");
            ImGui.PushItemWidth(-1f);
            var labelCur = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
            var created = _app.GetMyCreatedClubs() ?? Array.Empty<string>();
            var clubs = _app.GetMyClubs() ?? Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(_app.CurrentClubId))
            {
                var present = System.Array.IndexOf(created, _app.CurrentClubId) >= 0 || System.Array.IndexOf(clubs, _app.CurrentClubId) >= 0;
                if (!present)
                {
                    var next = created.Length > 0 ? created[0] : (clubs.Length > 0 ? clubs[0] : null);
                    _app.SetClubId(next);
                    labelCur = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
                }
            }
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
            ImGui.PopItemWidth();
        }
        ImGui.TextUnformatted("Server:");
        ImGui.SameLine();
        var statusTextL = _app.RemoteConnected ? "Online" : "Offline";
        var statusColorL = _app.RemoteConnected ? new System.Numerics.Vector4(0.2f, 0.85f, 0.2f, 1f) : new System.Numerics.Vector4(0.9f, 0.25f, 0.25f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, statusColorL);
        ImGui.TextUnformatted(statusTextL);
        ImGui.PopStyleColor();

        ImGui.EndChild();
        var btnH = ImGui.GetFrameHeight();
        if (_app.HasStaffSession)
        {
            var availTop = ImGui.GetContentRegionAvail().X;
            var halfTop = (availTop - style.ItemSpacing.X) * 0.5f;
            if (ImGui.Button("Register Venue", new Vector2(halfTop, 0))) { _registerClubModal.Open(); }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new venue"); ImGui.EndTooltip(); }
            ImGui.SameLine();
            if (ImGui.Button("Join Venue", new Vector2(halfTop, 0))) { _joinClubModal.Open(); }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Join an existing venue"); ImGui.EndTooltip(); }
            ImGui.Spacing();
        }
        var availX = ImGui.GetContentRegionAvail().X;
        var spacingX = style.ItemSpacing.X;
        var gearW = btnH + style.FramePadding.X * 2f;
        var bottomLeftW = (availX - gearW - spacingX * 2f) * 0.5f;
        var bottomRightW = bottomLeftW;
        ImGui.BeginDisabled(!(_app.IsOwnerCurrentClub || _app.IsPowerStaff));
        if (ImGui.Button("Account Settings", new Vector2(bottomLeftW, 0)))
        {
            _app.OpenSettingsWindowAccount();
            _showUserSettingsPanel = false;
        }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Manage your account"); ImGui.EndTooltip(); }
        ImGui.SameLine();
        if (ImGui.Button("Logout", new Vector2(bottomRightW, 0)))
        {
            _app.LogoutAll();
            _showUserSettingsPanel = false;
            _showStaffForm = false; _adminPinInput = string.Empty; _staffUserInput = string.Empty; _staffPassInput = string.Empty; _adminLoginStatus = string.Empty; _staffLoginStatus = string.Empty;
        }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Logout from all sessions"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + "##open_settings_left", new Vector2(gearW, 0)))
        {
            _app.OpenSettingsWindow();
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open Settings"); ImGui.EndTooltip(); }
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
            ImGui.Spacing();
            ImGui.TextUnformatted("Get Started");
            ImGui.BulletText("Register a staff account");
            ImGui.BulletText("Login, then create or join a venue");
            ImGui.BulletText("Use Settings for account and plugin options");
            ImGui.Spacing();
            var halfN = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            ImGui.Spacing();
            ImGui.TextDisabled("Tip: Right-click your Username in the left panel to copy your UID after login.");
        }
        else if (_showUserSettingsPanel)
        {
            _showUserSettingsPanel = false;
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
            if (ImGui.Button("Register Venue", new Vector2(half, 0))) { _registerClubModal.Open(); }
            ImGui.SameLine();
            if (ImGui.Button("Join Venue", new Vector2(half, 0))) { _joinClubModal.Open(); }
            ImGui.Spacing();
            ImGui.TextUnformatted("Tips");
            ImGui.Separator();
            ImGui.BulletText("Right-click your Username to copy your UID.");
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
            var accessR = _app.AccessLoading ? "Loading..." : (_app.IsOwnerCurrentClub ? "Owner" : (_app.IsPowerStaff ? "Staff" : "Guest"));
            ImGui.TextUnformatted($"Access: {accessR}");
            var jobR = string.IsNullOrWhiteSpace(_app.CurrentStaffJob) ? string.Empty : _app.CurrentStaffJob;
            if (!string.IsNullOrWhiteSpace(jobR))
            {
                ImGui.TextUnformatted("Job:");
                ImGui.SameLine();
                var rightsR = _app.GetJobRightsCache();
                if (rightsR != null && rightsR.TryGetValue(jobR, out var infoR))
                {
                    var colR = VenuePlus.Helpers.ColorUtil.HexToU32(infoR.ColorHex);
                    var iconR = VenuePlus.Helpers.IconDraw.ParseIcon(infoR.IconKey);
                    VenuePlus.Helpers.IconDraw.IconText(iconR, 0.9f, colR);
                    ImGui.SameLine();
                    ImGui.TextUnformatted(jobR);
                }
                else
                {
                    ImGui.TextUnformatted(jobR);
                }
            }
            else
            {
                ImGui.TextUnformatted("Job: --");
            }
            try { _statsVipCount = _app.GetActive()?.Count ?? 0; } catch { _statsVipCount = 0; }
            ImGui.TextUnformatted($"VIPs: {_statsVipCount}   Staff: {_statsStaffCount}");
            ImGui.EndGroup();
            ImGui.Spacing();
            if (ImGui.BeginTabBar("MainTabs"))
            {
            if (ImGui.BeginTabItem("VIPs"))
            {
                _djList.CloseAddForm();
                _staffList.CloseInviteInline();
                ImGui.Spacing();
                ImGui.PushItemWidth(260f);
                ImGui.InputTextWithHint("##filter", "Search VIPs by name or homeworld", ref _filter, 256);
                ImGui.PopItemWidth();
                var canAddVipTab = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanAddVip);
                var canRemoveVipTab = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanRemoveVip);
                if (canAddVipTab)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Add VIP")) { _vipTable.OpenAddForm(); }
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Add a new VIP"); ImGui.EndTooltip(); }
                }
                if (canRemoveVipTab)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Purge Expired")) { _app.PurgeExpired(); }
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Remove expired VIPs"); ImGui.EndTooltip(); }
                }
                ImGui.Separator();
                _vipTable.Draw(_app, _filter);
                ImGui.EndTabItem();
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.BeginTabItem("DJs"))
                {
                    _vipTable.CloseAddForm();
                    _staffList.CloseInviteInline();
                    var canManageDj = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanManageUsers);
                    if (canManageDj)
                    {
                        if (ImGui.Button("Add DJ")) { _djList.OpenAddForm(); }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Add a new DJ"); ImGui.EndTooltip(); }
                        ImGui.SameLine();
                    }
                    _djList.Draw(_app);
                    ImGui.EndTabItem();
                }
            }
            if (_app.HasStaffSession)
            {
                if (ImGui.BeginTabItem("Staff"))
                {
                    _vipTable.CloseAddForm();
                    _djList.CloseAddForm();
                    if (_staffLastRefresh == System.DateTimeOffset.MinValue)
                    {
                        _staffList.TriggerRefresh(_app);
                        _staffLastRefresh = System.DateTimeOffset.UtcNow;
                    }
                    _staffList.Draw(_app);
                    ImGui.EndTabItem();
                }
            }
            if (_app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanManageJobs))
            {
                if (ImGui.BeginTabItem("Roles"))
                {
                    _jobsPanel.Draw(_app);
                    ImGui.EndTabItem();
                }
            }
            if (ImGui.BeginTabItem("Venue Settings"))
            {
                _settingsPanel.Draw(_app);
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
        
        if (_app.HasStaffSession)
        {
            ImGui.SetNextWindowSize(new Vector2(320, 220), ImGuiCond.FirstUseEver);
            ImGui.Begin("My Venues", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);
            var clubs2 = _app.GetMyClubs() ?? Array.Empty<string>();
            var created2 = _app.GetMyCreatedClubs() ?? Array.Empty<string>();
            var labelCur2 = string.IsNullOrWhiteSpace(_app.CurrentClubId) ? "--" : _app.CurrentClubId;
            ImGui.TextUnformatted($"Current Venue: {labelCur2}");
            ImGui.Separator();
            foreach (var c in created2)
            {
                var label = string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal) ? $"{c} (owned, active)" : $"{c} (owned)";
                if (ImGui.Button(label)) { _app.SetClubId(c); _staffLastRefresh = System.DateTimeOffset.MinValue; }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Switch to this Venue  "); ImGui.EndTooltip(); }
            }
            if (created2.Length > 0 && clubs2.Length > 0) ImGui.Separator();
            foreach (var c in clubs2)
            {
                var isOwned = Array.IndexOf(created2, c) >= 0;
                if (isOwned) continue;
                var label = string.Equals(c, _app.CurrentClubId, StringComparison.Ordinal) ? $"{c} (member, active)" : $"{c} (member)";
                if (ImGui.Button(label)) { _app.SetClubId(c); _staffLastRefresh = System.DateTimeOffset.MinValue; }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Switch to this Venue"); ImGui.EndTooltip(); }
            }
            if (created2.Length == 0 && clubs2.Length == 0) ImGui.TextUnformatted("No Venues yet.");
            ImGui.End();
        }
        
        _registerModal.Draw(_app);
        _registerClubModal.Draw(_app);
        _joinClubModal.Draw(_app);
        _addVip.Draw(_app);
        RenderNotifications();
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

    private void OnNotification(string message)
    {
        var until = System.DateTimeOffset.UtcNow.AddSeconds(4);
        _notifications.Add((message, until));
    }

    private void RenderNotifications()
    {
        var now = System.DateTimeOffset.UtcNow;
        for (int i = _notifications.Count - 1; i >= 0; i--)
        {
            if (_notifications[i].ExpiresAt <= now) _notifications.RemoveAt(i);
        }
        if (_notifications.Count == 0) return;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.WorkPos.X + vp.WorkSize.X - 12f, vp.WorkPos.Y + 12f), ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.SetNextWindowBgAlpha(0.8f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
        ImGui.Begin("VenuePlusNotifications", flags);
        var msg = _notifications[_notifications.Count - 1].Message;
        ImGui.TextUnformatted(msg);
        ImGui.End();
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
        _app.Notification -= OnNotification;
        _app.AutoLoginResultEvt -= OnAutoLoginResult;
        _app.RememberStaffNeedsPasswordEvt -= OnRememberStaffNeedsPassword;
        _app.ClubLogoChanged -= OnClubLogoChanged;
        try { _clubLogoTex?.Dispose(); } catch { }
        _clubLogoTex = null;
        foreach (var kv in _clubLogoTexCache) { try { kv.Value.Dispose(); } catch { } }
        _clubLogoTexCache.Clear();
    }

    private void OnUsersDetailsChanged(VenuePlus.State.StaffUser[] users)
    {
        _staffList.SetUsersFromServer(_app, users);
        _statsStaffCount = users?.Length ?? 0;
    }

    private void OnUserJobUpdated(string username, string job)
    {
        _staffList.ApplyUserJobUpdate(_app, username, job);
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
        if (_clubLogoFetchPending.ContainsKey(clubId)) return null;
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
