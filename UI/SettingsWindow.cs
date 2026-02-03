using System;
using System.Numerics;
using System.Globalization;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using VenuePlus.Helpers;
using System.Net.Http;

namespace VenuePlus.UI;

public sealed class SettingsWindow : Window, System.IDisposable
{
    private readonly VenuePlusApp _app;
    private string _rememberStatus = string.Empty;
    private string _autoStatus = string.Empty;
    private bool _selectAccountOnOpen;
    private string _staffNewPassword = string.Empty;
    private string _staffPassStatus = string.Empty;
    private string _recoveryCode = string.Empty;
    private string _recoveryStatus = string.Empty;
    private bool _deleteConfirmChecked;
    private bool _deleteOwnerWarningChecked;
    private readonly Dictionary<string, VenuePlus.State.StaffUser[]> _ownerTransferUsersByClub = new(StringComparer.Ordinal);
    private string _ownerTransferSelectedClub = string.Empty;
    private string _ownerTransferSelectedUser = string.Empty;
    private string _ownerTransferStatus = string.Empty;
    private string _ownerTransferUserFilter = string.Empty;
    private bool _ownerTransferLoading;
    private string _ownerTransferLoadingClub = string.Empty;
    private string _deleteAccountStatus = string.Empty;
    private string _birthdayInput = string.Empty;
    private string _birthdayStatus = string.Empty;
    private System.DateTimeOffset? _birthdaySnapshot;
    private bool _birthdayDirty;
    private int _birthdayMonth = 1;
    private int _birthdayDay = 1;
    private string _birthdayYearInput = string.Empty;
    private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;
    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? _aboutTex;
    private bool _aboutRequested;

    public SettingsWindow(VenuePlusApp app, Dalamud.Plugin.Services.ITextureProvider textureProvider) : base("Venue Plus Settings")
    {
        _app = app;
        _textureProvider = textureProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350f, 500f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void OnOpen()
    {
        ResetStatusMessages();
    }

    public override void OnClose()
    {
        ResetStatusMessages();
    }

    public override void Draw()
    {
        var req = _app.ConsumeRequestedSettingsTab();
        _selectAccountOnOpen = string.Equals(req, "Account", System.StringComparison.Ordinal);

        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            if (ImGui.BeginTabItem("Settings", ImGuiTabItemFlags.None))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                if (ImGui.BeginTabBar("SettingsInnerTabs"))
                {
                    if (ImGui.BeginTabItem("Login Behavior"))
                    {
                        if (ImGui.IsItemActivated()) ResetStatusMessages();
                        DrawSettingsLoginBehavior();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Appearance"))
                    {
                        if (ImGui.IsItemActivated()) ResetStatusMessages();
                        DrawSettingsAppearance();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Notifications"))
                    {
                        if (ImGui.IsItemActivated()) ResetStatusMessages();
                        DrawSettingsNotifications();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }

            var accFlags = _selectAccountOnOpen ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Account Settings", accFlags))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                DrawAccountSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("About"))
            {
                if (ImGui.IsItemActivated()) ResetStatusMessages();
                var ver = typeof(VenuePlus.Plugin.Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
                var availX = ImGui.GetContentRegionAvail().X;
                if (_aboutTex == null && !_aboutRequested)
                {
                    _aboutRequested = true;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            using var http = new HttpClient();
                            var bytes = await http.GetByteArrayAsync("https://raw.githubusercontent.com/SpheneDev/repo/refs/heads/main/images/venueplus.png");
                            var tex = await _textureProvider.CreateFromImageAsync(bytes);
                            _aboutTex = tex;
                        }
                        catch { }
                    });
                }
                if (_aboutTex != null)
                {
                    var desiredW = 220f;
                    var desiredH = 220f;
                    var xImg = (availX - desiredW) / 2f;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + xImg);
                    ImGui.Image(_aboutTex.Handle, new Vector2(desiredW, desiredH));
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Venue Plus"); ImGui.EndTooltip(); }
                    ImGui.Spacing();
                }
                var tTitle = "About Venue Plus";
                var wTitle = ImGui.CalcTextSize(tTitle).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wTitle) / 2f);
                ImGui.TextUnformatted(tTitle);
                ImGui.Separator();
                var tVer = $"Version: {ver}";
                var wVer = ImGui.CalcTextSize(tVer).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wVer) / 2f);
                ImGui.TextUnformatted(tVer);
                var tAuth = "Authors: Keqing Yu & SpheneDev";
                var wAuth = ImGui.CalcTextSize(tAuth).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wAuth) / 2f);
                ImGui.TextUnformatted(tAuth);
                ImGui.Spacing();
                var tDesc = "Manage your venue: VIPs, staff and DJs with fast actions.";
                var wDesc = ImGui.CalcTextSize(tDesc).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wDesc) / 2f);
                ImGui.TextDisabled(tDesc);
                ImGui.Spacing();

                var baseCol = VenuePlus.Helpers.ColorUtil.HexToVec4("#FF5E5B");
                var hoverCol = new System.Numerics.Vector4(baseCol.X + 0.08f, baseCol.Y + 0.08f, baseCol.Z + 0.08f, 1f);
                var activeCol = new System.Numerics.Vector4(baseCol.X - 0.06f, baseCol.Y - 0.06f, baseCol.Z - 0.06f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol);
                var btnSize = new System.Numerics.Vector2(160f, 0f);
                var wBtn = btnSize.X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wBtn) / 2f);
                var clicked = ImGui.Button("Ko-fi##kofi_btn", btnSize);
                var rectMin = ImGui.GetItemRectMin();
                var rectMax = ImGui.GetItemRectMax();
                var draw = ImGui.GetWindowDrawList();
                var iconStr = Dalamud.Interface.FontAwesomeIcon.Coffee.ToIconString();
                ImGui.PushFont(UiBuilder.IconFont);
                var iconSize = ImGui.CalcTextSize(iconStr);
                var yIcon = rectMin.Y + ((rectMax.Y - rectMin.Y) - iconSize.Y) / 2f;
                var xIcon = rectMin.X + ImGui.GetStyle().FramePadding.X;
                draw.AddText(new System.Numerics.Vector2(xIcon, yIcon), ImGui.GetColorU32(ImGuiCol.Text), iconStr);
                ImGui.PopFont();
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Support Sphene Dev on Ko-fi"); ImGui.EndTooltip(); }
                if (clicked)
                {
                    var url = "https://ko-fi.com/sphenedev";
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                    });
                }

                ImGui.Spacing();
                var baseCol2 = VenuePlus.Helpers.ColorUtil.HexToVec4("#5E81AC");
                var hoverCol2 = new System.Numerics.Vector4(baseCol2.X + 0.08f, baseCol2.Y + 0.08f, baseCol2.Z + 0.08f, 1f);
                var activeCol2 = new System.Numerics.Vector4(baseCol2.X - 0.06f, baseCol2.Y - 0.06f, baseCol2.Z - 0.06f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol2);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol2);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol2);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wBtn) / 2f);
                var clicked2 = ImGui.Button("Changelog##changelog_btn", btnSize);
                var rectMin2 = ImGui.GetItemRectMin();
                var rectMax2 = ImGui.GetItemRectMax();
                var draw2 = ImGui.GetWindowDrawList();
                var iconStr2 = Dalamud.Interface.FontAwesomeIcon.ListUl.ToIconString();
                ImGui.PushFont(UiBuilder.IconFont);
                var iconSize2 = ImGui.CalcTextSize(iconStr2);
                var yIcon2 = rectMin2.Y + ((rectMax2.Y - rectMin2.Y) - iconSize2.Y) / 2f;
                var xIcon2 = rectMin2.X + ImGui.GetStyle().FramePadding.X;
                draw2.AddText(new System.Numerics.Vector2(xIcon2, yIcon2), ImGui.GetColorU32(ImGuiCol.Text), iconStr2);
                ImGui.PopFont();
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open plugin changelog"); ImGui.EndTooltip(); }
                if (clicked2) { _app.OpenChangelogWindow(); }
                ImGui.EndTabItem();
            }

            
            ImGui.EndTabBar();
        }
    }

    private void ResetStatusMessages()
    {
        _rememberStatus = string.Empty;
        _autoStatus = string.Empty;
        _staffPassStatus = string.Empty;
        _recoveryStatus = string.Empty;
        _ownerTransferStatus = string.Empty;
        _deleteAccountStatus = string.Empty;
        _birthdayStatus = string.Empty;
    }

    private void DrawAccountSettings()
    {
        var canUse = _app.HasStaffSession;
        if (!canUse)
        {
            ImGui.TextUnformatted("Login to use account settings");
            return;
        }

        var info = _app.GetCurrentCharacter();
        var name = info.HasValue ? info.Value.name : "—";
        var world = info.HasValue ? info.Value.world : "—";
        var suggested = (info.HasValue && !string.IsNullOrWhiteSpace(info.Value.name) && !string.IsNullOrWhiteSpace(info.Value.world)) ? (info.Value.name + "@" + info.Value.world) : "—";
        var staffUser = string.IsNullOrWhiteSpace(_app.CurrentStaffUsername) ? "Not logged in" : _app.CurrentStaffUsername;
        var staffJob = string.IsNullOrWhiteSpace(_app.CurrentStaffJob) ? "—" : _app.CurrentStaffJob;
        var uidDisplay = string.IsNullOrWhiteSpace(_app.CurrentStaffUid) ? "—" : _app.CurrentStaffUid;

        var ownedVenues = _app.GetMyCreatedClubs() ?? Array.Empty<string>();
        if (ownedVenues.Length == 0)
        {
            _deleteOwnerWarningChecked = false;
            _ownerTransferSelectedClub = string.Empty;
            _ownerTransferSelectedUser = string.Empty;
        }

        var transparentHeader = new Vector4(0f, 0f, 0f, 0f);
        ImGui.PushStyleColor(ImGuiCol.Header, transparentHeader);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, transparentHeader);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, transparentHeader);

        if (ImGui.CollapsingHeader("Account Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Spacing();
            ImGui.Columns(2, "acc-info", false);
            ImGui.SetColumnWidth(0, 160f);
            ImGui.TextUnformatted("Character");
            ImGui.NextColumn();
            ImGui.TextUnformatted(name);
            ImGui.NextColumn();
            ImGui.TextUnformatted("Home World");
            ImGui.NextColumn();
            ImGui.TextUnformatted(world);
            ImGui.NextColumn();
            ImGui.TextUnformatted("Suggested Username");
            ImGui.NextColumn();
            ImGui.TextUnformatted(suggested);
            ImGui.NextColumn();
            ImGui.TextUnformatted("User UID");
            ImGui.NextColumn();
            ImGui.TextUnformatted(uidDisplay);
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##copy_uid_settings"))
            {
                if (!string.IsNullOrWhiteSpace(uidDisplay)) ImGui.SetClipboardText(uidDisplay);
            }
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
            ImGui.Columns(1);
            ImGui.Spacing();
        }
        var currentBirthday = _app.CurrentStaffBirthday;
        if (!_birthdayDirty && _birthdaySnapshot != currentBirthday)
        {
            _birthdaySnapshot = currentBirthday;
            if (currentBirthday.HasValue)
            {
                var bday = currentBirthday.Value.UtcDateTime;
                _birthdayMonth = bday.Month;
                _birthdayDay = bday.Day;
                _birthdayYearInput = bday.Year == 2000 ? string.Empty : bday.Year.ToString(CultureInfo.InvariantCulture);
                _birthdayInput = bday.Year == 2000
                    ? bday.ToString("MM-dd", CultureInfo.InvariantCulture)
                    : bday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                var now = DateTime.UtcNow;
                _birthdayMonth = now.Month;
                _birthdayDay = now.Day;
                _birthdayYearInput = string.Empty;
                _birthdayInput = string.Empty;
            }
        }
        if (ImGui.CollapsingHeader("Profile", ImGuiTreeNodeFlags.None))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Set your birthday to show in staff lists.");
            ImGui.Spacing();
            ImGui.TextDisabled("Select day and month. Year is optional.");
            ImGui.Spacing();
            var monthNames = new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
            var monthIndex = _birthdayMonth - 1;
            if (monthIndex < 0) monthIndex = 0;
            if (monthIndex > 11) monthIndex = 11;
            ImGui.PushItemWidth(140f);
            if (ImGui.Combo("##birthday_month", ref monthIndex, monthNames, monthNames.Length))
            {
                _birthdayMonth = monthIndex + 1;
                _birthdayDirty = true;
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            var yearForDays = 2000;
            if (!string.IsNullOrWhiteSpace(_birthdayYearInput) && int.TryParse(_birthdayYearInput, out var parsedYear) && parsedYear >= 1 && parsedYear <= 9999)
            {
                yearForDays = parsedYear;
            }
            var maxDay = DateTime.DaysInMonth(yearForDays, _birthdayMonth);
            if (_birthdayDay > maxDay) _birthdayDay = maxDay;
            if (_birthdayDay < 1) _birthdayDay = 1;
            var dayLabels = new string[maxDay];
            for (int i = 0; i < maxDay; i++) dayLabels[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
            var dayIndex = _birthdayDay - 1;
            ImGui.PushItemWidth(90f);
            if (ImGui.Combo("##birthday_day", ref dayIndex, dayLabels, dayLabels.Length))
            {
                _birthdayDay = dayIndex + 1;
                _birthdayDirty = true;
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.PushItemWidth(80f);
            var yearText = _birthdayYearInput;
            if (ImGui.InputTextWithHint("##birthday_year", "Year", ref yearText, 4))
            {
                _birthdayYearInput = yearText;
                _birthdayDirty = true;
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Save Birthday"))
            {
                _birthdayStatus = "Submitting...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    System.DateTimeOffset? bday = null;
                    var yearTextLocal = _birthdayYearInput?.Trim() ?? string.Empty;
                    var yearValue = 2000;
                    if (!string.IsNullOrWhiteSpace(yearTextLocal))
                    {
                        if (!int.TryParse(yearTextLocal, out yearValue) || yearValue < 1 || yearValue > 9999)
                        {
                            _birthdayStatus = "Invalid year. Use 1 to 9999 or leave empty.";
                            return;
                        }
                    }
                    try
                    {
                        var normalized = new DateTime(yearValue, _birthdayMonth, _birthdayDay, 0, 0, 0, DateTimeKind.Utc);
                        bday = new System.DateTimeOffset(normalized, TimeSpan.Zero);
                    }
                    catch
                    {
                        _birthdayStatus = "Invalid date.";
                        return;
                    }
                    var ok = await _app.SetSelfBirthdayAsync(bday);
                    _birthdayStatus = ok ? "Birthday updated" : "Update failed";
                    if (ok)
                    {
                        _birthdayDirty = false;
                        _birthdaySnapshot = bday;
                        if (bday.HasValue)
                        {
                            var bdayVal = bday.Value.UtcDateTime;
                            _birthdayMonth = bdayVal.Month;
                            _birthdayDay = bdayVal.Day;
                            _birthdayYearInput = bdayVal.Year == 2000 ? string.Empty : bdayVal.Year.ToString(CultureInfo.InvariantCulture);
                            _birthdayInput = bdayVal.Year == 2000
                                ? bdayVal.ToString("MM-dd", CultureInfo.InvariantCulture)
                                : bdayVal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            _birthdayInput = string.Empty;
                        }
                    }
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear Birthday"))
            {
                _birthdayStatus = "Submitting...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await _app.SetSelfBirthdayAsync(null);
                    _birthdayStatus = ok ? "Birthday cleared" : "Update failed";
                    if (ok)
                    {
                        _birthdayDirty = false;
                        _birthdaySnapshot = null;
                        _birthdayInput = string.Empty;
                        var now = DateTime.UtcNow;
                        _birthdayMonth = now.Month;
                        _birthdayDay = now.Day;
                        _birthdayYearInput = string.Empty;
                    }
                });
            }
            if (!string.IsNullOrEmpty(_birthdayStatus)) ImGui.TextUnformatted(_birthdayStatus);
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Security", ImGuiTreeNodeFlags.None))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Change your login password.");
            ImGui.SameLine();
            DrawHelpIcon("Change your login password.");
            ImGui.Spacing();
            ImGui.PushItemWidth(150f);
            ImGui.InputTextWithHint("##change_login_password_settings", "Change Login Password", ref _staffNewPassword, 64, ImGuiInputTextFlags.Password);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Save Password"))
            {
                _staffPassStatus = "Submitting...";
                var text = _staffNewPassword;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await _app.StaffSetOwnPasswordAsync(text);
                    _staffPassStatus = ok ? "Password updated" : "Update failed";
                    if (ok) _staffNewPassword = string.Empty;
                });
            }
            ImGui.SameLine();
            DrawHelpIcon("Updates the password used to login.");
            if (!string.IsNullOrEmpty(_staffPassStatus)) ImGui.TextUnformatted(_staffPassStatus);
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Recovery", ImGuiTreeNodeFlags.None))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Generate a recovery code to reset your password.");
            ImGui.SameLine();
            DrawHelpIcon("Generate a recovery code to reset your password.");
            ImGui.Spacing();
            if (ImGui.Button("Generate Recovery Code"))
            {
                _recoveryStatus = "Submitting...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var code = await _app.GenerateRecoveryCodeAsync();
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        _recoveryCode = code;
                        _recoveryStatus = "Recovery code generated";
                    }
                    else
                    {
                        _recoveryStatus = "Generation failed";
                    }
                });
            }
            ImGui.SameLine();
            DrawHelpIcon("Use the code in Password Recovery to reset your password.");
            if (!string.IsNullOrEmpty(_recoveryStatus)) ImGui.TextUnformatted(_recoveryStatus);
            if (!string.IsNullOrWhiteSpace(_recoveryCode))
            {
                ImGui.TextUnformatted(_recoveryCode);
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##copy_recovery_code"))
                {
                    ImGui.SetClipboardText(_recoveryCode);
                }
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
            }
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Owner Transfer", ImGuiTreeNodeFlags.None))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Transfer venue ownership before deleting your account.");
            ImGui.SameLine();
            DrawHelpIcon("Transfer venue ownership before deleting your account.");
            ImGui.Spacing();
            if (ownedVenues.Length == 0)
            {
                ImGui.TextUnformatted("You do not own any venues.");
            }
            else
            {
                ImGui.TextWrapped("Select a venue and choose a new owner.");
                ImGui.Spacing();
                for (int i = 0; i < ownedVenues.Length; i++)
                {
                    var clubId = ownedVenues[i];
                    var hasUsers = _ownerTransferUsersByClub.TryGetValue(clubId, out var usersForClub);
                    var eligibleCount = hasUsers ? GetEligibleOwnerTransferUsersCount(usersForClub ?? Array.Empty<VenuePlus.State.StaffUser>()) : 0;
                    var canPick = !_ownerTransferLoading && (!hasUsers || eligibleCount > 0);
                    var label = string.Equals(clubId, _ownerTransferSelectedClub, StringComparison.Ordinal) ? (clubId + " (selected)") : clubId;
                    ImGui.BeginDisabled(!canPick);
                    if (ImGui.Button(label))
                    {
                        _ownerTransferSelectedClub = clubId;
                        _ownerTransferSelectedUser = string.Empty;
                        _ownerTransferUserFilter = string.Empty;
                        if (!hasUsers) RequestOwnerTransferUsers(clubId);
                    }
                    ImGui.EndDisabled();
                    if (hasUsers && eligibleCount == 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled("No other staff members");
                    }
                }
                if (_ownerTransferLoading)
                {
                    var loadingLabel = string.IsNullOrWhiteSpace(_ownerTransferLoadingClub) ? "Loading staff list..." : $"Loading staff list for {_ownerTransferLoadingClub}...";
                    ImGui.TextUnformatted(loadingLabel);
                }
                if (!string.IsNullOrWhiteSpace(_ownerTransferSelectedClub) && _ownerTransferUsersByClub.TryGetValue(_ownerTransferSelectedClub, out var selectedUsers))
                {
                    var eligibleUsers = GetEligibleOwnerTransferUsers(selectedUsers ?? Array.Empty<VenuePlus.State.StaffUser>(), _ownerTransferUserFilter);
                    ImGui.Spacing();
                    ImGui.TextUnformatted($"Venue: {_ownerTransferSelectedClub}");
                    ImGui.PushItemWidth(200f);
                    ImGui.InputTextWithHint("##owner_transfer_filter", "Search by username", ref _ownerTransferUserFilter, 128);
                    ImGui.PopItemWidth();
                    if (eligibleUsers.Length == 0)
                    {
                        ImGui.TextDisabled("No eligible users found.");
                    }
                    else
                    {
                        for (int i = 0; i < eligibleUsers.Length; i++)
                        {
                            var uname = eligibleUsers[i].Username ?? string.Empty;
                            var selected = string.Equals(uname, _ownerTransferSelectedUser, StringComparison.Ordinal);
                            if (ImGui.Selectable(uname, selected))
                            {
                                _ownerTransferSelectedUser = uname;
                                TriggerOwnerTransfer(_ownerTransferSelectedClub, uname);
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(_ownerTransferStatus)) ImGui.TextUnformatted(_ownerTransferStatus);
            }
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Account Removal", ImGuiTreeNodeFlags.None))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Permanently deletes your account and logs you out.");
            ImGui.SameLine();
            DrawHelpIcon("Permanently deletes your account and logs you out.");
            ImGui.Spacing();
            ImGui.TextWrapped("This action cannot be undone.");
            if (ownedVenues.Length > 0)
            {
                ImGui.Spacing();
                var warnCol = VenuePlus.Helpers.ColorUtil.HexToVec4("#FF5E5B");
                ImGui.TextColored(warnCol, "Owner Warning");
                ImGui.TextWrapped("Deleting your account will also delete any venues you own.");
                ImGui.TextWrapped("If any of these venues has other members, transfer ownership or delete the venue before you continue.");
                ImGui.TextWrapped("Deletion proceeds without regard to other members.");
                ImGui.Spacing();
                ImGui.TextUnformatted($"Owned venues: {ownedVenues.Length}");
                ImGui.TextWrapped("Use Owner Transfer above to pick a venue and assign a new owner.");
                ImGui.Spacing();
                ImGui.Checkbox("I have transferred ownership or accept deletion of my venues", ref _deleteOwnerWarningChecked);
                ImGui.Spacing();
            }
            ImGui.Checkbox("I understand this cannot be undone", ref _deleteConfirmChecked);
            var confirmReady = _deleteConfirmChecked && (ownedVenues.Length == 0 || _deleteOwnerWarningChecked);
            ImGui.BeginDisabled(!confirmReady);
            var ctrlPressed = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
            ImGui.BeginDisabled(!ctrlPressed);
            if (ImGui.Button("Delete Account"))
            {
                _deleteAccountStatus = "Deleting account...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await _app.DeleteCurrentUserAsync();
                    _deleteAccountStatus = ok ? "Account deleted" : (_app.GetLastServerMessage() ?? "Delete failed");
                    if (ok)
                    {
                        _deleteConfirmChecked = false;
                        _deleteOwnerWarningChecked = false;
                    }
                });
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(ctrlPressed ? "Permanently delete your account" : "Hold Ctrl to confirm");
                ImGui.EndTooltip();
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrEmpty(_deleteAccountStatus)) ImGui.TextUnformatted(_deleteAccountStatus);
            ImGui.Spacing();
        }

        ImGui.PopStyleColor(3);
    }

    private void RequestOwnerTransferUsers(string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return;
        if (_ownerTransferLoading) return;
        _ownerTransferLoading = true;
        _ownerTransferLoadingClub = clubId;
        _ownerTransferStatus = "Loading staff list...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            var previousClub = _app.CurrentClubId;
            if (!string.Equals(previousClub, clubId, StringComparison.Ordinal))
            {
                _app.SetClubId(clubId);
            }
            await WaitForClubAsync(clubId);
            var users = await _app.FetchStaffUsersDetailedAsync();
            _ownerTransferUsersByClub[clubId] = users ?? Array.Empty<VenuePlus.State.StaffUser>();
            if (!string.Equals(previousClub, clubId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(previousClub))
            {
                _app.SetClubId(previousClub);
            }
            _ownerTransferLoading = false;
            _ownerTransferLoadingClub = string.Empty;
            _ownerTransferStatus = users == null ? "No staff data available" : $"Loaded {users.Length} users";
        });
    }

    private void TriggerOwnerTransfer(string clubId, string username)
    {
        if (string.IsNullOrWhiteSpace(clubId) || string.IsNullOrWhiteSpace(username)) return;
        if (_ownerTransferLoading) return;
        _ownerTransferLoading = true;
        _ownerTransferStatus = "Transferring owner...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            var previousClub = _app.CurrentClubId;
            if (!string.Equals(previousClub, clubId, StringComparison.Ordinal))
            {
                _app.SetClubId(clubId);
            }
            await WaitForClubAsync(clubId);
            if (!_ownerTransferUsersByClub.TryGetValue(clubId, out var users)) users = Array.Empty<VenuePlus.State.StaffUser>();
            var target = FindStaffUser(users, username);
            var selfName = _app.CurrentStaffUsername;
            var selfUser = FindStaffUser(users, selfName);
            if (target == null)
            {
                _ownerTransferStatus = "User not found";
                _ownerTransferLoading = false;
                if (!string.Equals(previousClub, clubId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(previousClub)) _app.SetClubId(previousClub);
                return;
            }
            var targetJobs = AddOwnerJob(BuildJobsFromUser(target));
            var okTarget = await _app.UpdateStaffUserJobsAsync(target.Username, targetJobs);
            if (!okTarget)
            {
                _ownerTransferStatus = _app.GetLastServerMessage() ?? "Owner transfer failed";
                _ownerTransferLoading = false;
                if (!string.Equals(previousClub, clubId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(previousClub)) _app.SetClubId(previousClub);
                return;
            }
            if (selfUser != null)
            {
                var selfJobs = RemoveOwnerJob(BuildJobsFromUser(selfUser));
                var okSelf = await _app.UpdateStaffUserJobsAsync(selfUser.Username, selfJobs);
                _ownerTransferStatus = okSelf ? "Owner transferred" : (_app.GetLastServerMessage() ?? "Owner transferred, self role not updated");
            }
            else
            {
                _ownerTransferStatus = "Owner transferred";
            }
            _ownerTransferLoading = false;
            if (!string.Equals(previousClub, clubId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(previousClub)) _app.SetClubId(previousClub);
        });
    }

    private async System.Threading.Tasks.Task WaitForClubAsync(string clubId)
    {
        var start = System.DateTime.UtcNow;
        while ((System.DateTime.UtcNow - start).TotalSeconds < 5)
        {
            if (string.Equals(_app.CurrentClubId, clubId, StringComparison.Ordinal) && !_app.AccessLoading) return;
            await System.Threading.Tasks.Task.Delay(100);
        }
    }

    private int GetEligibleOwnerTransferUsersCount(VenuePlus.State.StaffUser[] users)
    {
        var selfName = _app.CurrentStaffUsername;
        var count = 0;
        for (int i = 0; i < users.Length; i++)
        {
            var u = users[i];
            if (u == null) continue;
            var uname = u.Username ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uname)) continue;
            if (u.IsManual) continue;
            if (string.Equals(uname, selfName, StringComparison.Ordinal)) continue;
            count++;
        }
        return count;
    }

    private VenuePlus.State.StaffUser[] GetEligibleOwnerTransferUsers(VenuePlus.State.StaffUser[] users, string filter)
    {
        var selfName = _app.CurrentStaffUsername;
        var list = new List<VenuePlus.State.StaffUser>();
        var f = filter?.Trim() ?? string.Empty;
        for (int i = 0; i < users.Length; i++)
        {
            var u = users[i];
            if (u == null) continue;
            var uname = u.Username ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uname)) continue;
            if (u.IsManual) continue;
            if (string.Equals(uname, selfName, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrWhiteSpace(f) && uname.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
            list.Add(u);
        }
        return list.ToArray();
    }

    private static VenuePlus.State.StaffUser? FindStaffUser(VenuePlus.State.StaffUser[] users, string username)
    {
        if (users == null || string.IsNullOrWhiteSpace(username)) return null;
        for (int i = 0; i < users.Length; i++)
        {
            var u = users[i];
            if (u == null) continue;
            if (string.Equals(u.Username, username, StringComparison.Ordinal)) return u;
        }
        return null;
    }

    private static string[] BuildJobsFromUser(VenuePlus.State.StaffUser user)
    {
        if (user == null) return new[] { "Unassigned" };
        var jobs = user.Jobs ?? Array.Empty<string>();
        if (jobs.Length > 0) return jobs;
        if (!string.IsNullOrWhiteSpace(user.Job)) return new[] { user.Job };
        return new[] { "Unassigned" };
    }

    private static string[] AddOwnerJob(string[] jobs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            if (!string.IsNullOrWhiteSpace(j)) set.Add(j);
        }
        set.Add("Owner");
        if (set.Count == 0) set.Add("Owner");
        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    private static string[] RemoveOwnerJob(string[] jobs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            if (string.Equals(j, "Owner", StringComparison.Ordinal)) continue;
            if (!string.IsNullOrWhiteSpace(j)) set.Add(j);
        }
        if (set.Count == 0) set.Add("Unassigned");
        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    private void DrawSettingsLoginBehavior()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Control login privacy and automation.");
        ImGui.TextDisabled("Store an encrypted password locally and auto-login on startup.");
        ImGui.Spacing();
        var remember = _app.RememberStaffLogin;
        if (ImGui.Checkbox("Remember Login Password", ref remember))
        {
            _rememberStatus = "Submitting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await _app.SetRememberStaffLoginAsync(remember);
                _rememberStatus = ok ? "Saved" : "Failed";
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Stores your password locally (encrypted) to simplify login.");
        var auto = _app.AutoLoginEnabled;
        if (ImGui.Checkbox("Auto login enabled", ref auto))
        {
            _autoStatus = "Submitting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await _app.SetAutoLoginEnabledAsync(auto);
                _autoStatus = ok ? "Saved" : "Failed";
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Attempts automatic login on plugin start.");
        if (!string.IsNullOrEmpty(_rememberStatus)) ImGui.TextUnformatted(_rememberStatus);
        if (!string.IsNullOrEmpty(_autoStatus)) ImGui.TextUnformatted(_autoStatus);
    }

    private void DrawSettingsAppearance()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Customize how VIP information appears in-game.");
        ImGui.TextDisabled("Nameplate options affect only visual presentation.");
        ImGui.Spacing();
        var showNameplate = _app.ShowVipNameplateHook;
        if (ImGui.Checkbox("Show VIP Indicator in nameplate", ref showNameplate))
        {
            _app.SetShowVipNameplateHookAsync(showNameplate).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Adds a colored symbol before VIP names in the nameplate.");
        ImGui.TextDisabled("Choose symbol & position.");
        ImGui.Spacing();
        var posOptions = new string[] { "Before name", "After name", "Before and after" };
        int posIndex = _app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Left ? 0 : (_app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Right ? 1 : 2);
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_star_position", ref posIndex, posOptions, posOptions.Length))
        {
            var newPos = posIndex == 0 ? VenuePlus.Configuration.VipStarPosition.Left : (posIndex == 1 ? VenuePlus.Configuration.VipStarPosition.Right : VenuePlus.Configuration.VipStarPosition.Both);
            _app.SetVipStarPositionAsync(newPos).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        var preset = new string[] { "None", "★", "☆", "♥", "♡" , "◆", "◇" };
        int presetIndex = 0;
        var currentSym = _app.VipStarChar;
        for (int i = 1; i < preset.Length; i++) { if (string.Equals(currentSym, preset[i], System.StringComparison.Ordinal)) { presetIndex = i; break; } }
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_star_preset", ref presetIndex, preset, preset.Length))
        {
            var chosen = presetIndex == 0 ? string.Empty : preset[presetIndex];
            _app.SetVipStarCharAsync(chosen).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.PushItemWidth(140f);
        var starChar = _app.VipStarChar;
        if (ImGui.InputText("##vip_star_char", ref starChar, 16))
        {
            _app.SetVipStarCharAsync(starChar).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        DrawHelpIcon("Symbol preset or custom text; 'None' disables the symbol.");

        ImGui.Spacing();
        var lblEnabled = _app.VipTextEnabled;
        if (ImGui.Checkbox("Show label text", ref lblEnabled))
        {
            _app.SetVipTextEnabledAsync(lblEnabled).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Shows custom label text near the nameplate (e.g., [VIP]).");
        ImGui.PushItemWidth(220f);
        var lblText = _app.VipLabelText;
        if (ImGui.InputText("##vip_label_text", ref lblText, 32))
        {
            _app.SetVipLabelTextAsync(lblText).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();

        var orderOptions = new string[] { "Symbol + Text", "Text + Symbol" };
        int orderIndex = _app.VipLabelOrder == VenuePlus.Configuration.VipLabelOrder.SymbolThenText ? 0 : 1;
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_label_order", ref orderIndex, orderOptions, orderOptions.Length))
        {
            var newOrder = orderIndex == 0 ? VenuePlus.Configuration.VipLabelOrder.SymbolThenText : VenuePlus.Configuration.VipLabelOrder.TextThenSymbol;
            _app.SetVipLabelOrderAsync(newOrder).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        DrawHelpIcon("Choose composition when both symbol and label are enabled.");
        ImGui.PushItemWidth(220f);
        var sliderIndex = GetSliderIndexFromActualKey((int)_app.VipStarColorKey);
        var sliderChanged = ImGui.SliderInt("##vip_star_color_key", ref sliderIndex, 0, 89);
        var displayActual = GetActualKeyFromSliderIndex(sliderIndex);
        if (sliderChanged)
        {
            _app.SetVipStarColorKeyAsync(displayActual).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        Vector4 rgbBox;
        try
        {
            var rgbaCur = new UIForegroundPayload(_app.VipStarColorKey).RGBA;
            var rCur = (byte)((rgbaCur >> 24) & 0xFF);
            var gCur = (byte)((rgbaCur >> 16) & 0xFF);
            var bCur = (byte)((rgbaCur >> 8) & 0xFF);
            rgbBox = new Vector4(rCur / 255f, gCur / 255f, bCur / 255f, 1f);
        }
        catch { rgbBox = new Vector4(1f, 0.84f, 0f, 1f); }
        ImGui.ColorButton("##vip_star_preview_settings", rgbBox, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoBorder, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
        ImGui.SameLine();
        DrawHelpIcon("Change color of VIP symbol in nameplate.");
        ImGui.PopItemWidth();
    }

    private void DrawSettingsNotifications()
    {
        var prefs = _app.GetNotificationPreferences();
        ImGui.BeginChild("NotifContentInner", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
        var modes = new string[] { "None", "Chat", "Toast", "Both" };
        ImGui.TextUnformatted("Choose how and which notifications are shown.");
        ImGui.TextDisabled("Select a display mode, then enable categories you care about.");
        ImGui.Spacing();
        int modeIndex = (int)prefs.DisplayMode;
        ImGui.TextUnformatted("Display Mode");
        ImGui.SameLine();
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##notif_display_mode_inner", ref modeIndex, modes, modes.Length))
        {
            prefs.DisplayMode = (VenuePlus.Configuration.NotificationDisplayMode)modeIndex;
            _app.SavePluginConfig();
        }
        ImGui.PopItemWidth();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
        if (ImGui.CollapsingHeader("Account & Access", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Login results and password prompts.");
            var s1 = prefs.ShowLoginSuccess; if (ImGui.Checkbox("Show login success", ref s1)) { prefs.ShowLoginSuccess = s1; _app.SavePluginConfig(); }
            var s2 = prefs.ShowLoginFailed; if (ImGui.Checkbox("Show login failed", ref s2)) { prefs.ShowLoginFailed = s2; _app.SavePluginConfig(); }
            var s3 = prefs.ShowPasswordRequired; if (ImGui.Checkbox("Show password required", ref s3)) { prefs.ShowPasswordRequired = s3; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("Roles & Ownership", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Role changes for you and ownership events.");
            var r1 = prefs.ShowRoleChangedSelf; if (ImGui.Checkbox("Show role changed (self)", ref r1)) { prefs.ShowRoleChangedSelf = r1; _app.SavePluginConfig(); }
            var r2 = prefs.ShowOwnershipGranted; if (ImGui.Checkbox("Show ownership granted", ref r2)) { prefs.ShowOwnershipGranted = r2; _app.SavePluginConfig(); }
            var r3 = prefs.ShowOwnershipTransferred; if (ImGui.Checkbox("Show ownership transferred", ref r3)) { prefs.ShowOwnershipTransferred = r3; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("Membership", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Join or removal from venues affecting your account.");
            var m1 = prefs.ShowMembershipJoined; if (ImGui.Checkbox("Show membership joined", ref m1)) { prefs.ShowMembershipJoined = m1; _app.SavePluginConfig(); }
            var m2 = prefs.ShowMembershipRemoved; if (ImGui.Checkbox("Show membership removed", ref m2)) { prefs.ShowMembershipRemoved = m2; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("VIPs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("VIP list changes: added or removed entries.");
            var v1 = prefs.ShowVipAdded; if (ImGui.Checkbox("Show VIP added", ref v1)) { prefs.ShowVipAdded = v1; _app.SavePluginConfig(); }
            var v2 = prefs.ShowVipRemoved; if (ImGui.Checkbox("Show VIP removed", ref v2)) { prefs.ShowVipRemoved = v2; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("DJs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("DJ roster changes in the current venue.");
            var d0 = prefs.ShowDjAdded; if (ImGui.Checkbox("Show DJ added", ref d0)) { prefs.ShowDjAdded = d0; _app.SavePluginConfig(); }
            var d1 = prefs.ShowDjRemoved; if (ImGui.Checkbox("Show DJ removed", ref d1)) { prefs.ShowDjRemoved = d1; _app.SavePluginConfig(); }
        }
        // TODO: Comming soon
        //if (ImGui.CollapsingHeader("Shifts", ImGuiTreeNodeFlags.DefaultOpen))
        //{
        //    ImGui.TextDisabled("Shift schedule updates and removals.");
        //    var sh1 = prefs.ShowShiftCreated; if (ImGui.Checkbox("Show shift created", ref sh1)) { prefs.ShowShiftCreated = sh1; _app.SavePluginConfig(); }
        //    var sh2 = prefs.ShowShiftUpdated; if (ImGui.Checkbox("Show shift updated", ref sh2)) { prefs.ShowShiftUpdated = sh2; _app.SavePluginConfig(); }
        //    var sh3 = prefs.ShowShiftRemoved; if (ImGui.Checkbox("Show shift removed", ref sh3)) { prefs.ShowShiftRemoved = sh3; _app.SavePluginConfig(); }
        //}
        ImGui.PopStyleColor(3);
        ImGui.EndChild();
    }

    private static void DrawHelpIcon(string text)
    {
        IconDraw.IconText(FontAwesomeIcon.QuestionCircle, text);
    }

    private static int GetSliderIndexFromActualKey(int actual)
    {
        if (actual >= 0 && actual <= 77) return actual;
        if (actual >= 500 && actual <= 511) return 78 + (actual - 500);
        return 43;
    }

    private static ushort GetActualKeyFromSliderIndex(int index)
    {
        if (index <= 77) return (ushort)index;
        var off = index - 78;
        return (ushort)(500 + off);
    }

    public void Dispose()
    {
        try { _aboutTex?.Dispose(); } catch { }
        _aboutTex = null;
    }
}

