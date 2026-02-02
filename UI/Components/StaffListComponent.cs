using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenuePlus.Plugin;
using VenuePlus.State;
using VenuePlus.Helpers;
using System.Numerics;

namespace VenuePlus.UI.Components;

public sealed class StaffListComponent
{
    private StaffUser[] _users = Array.Empty<StaffUser>();
    private string _status = string.Empty;
    private string _filter = string.Empty;
    private int _sortCol = 1;
    private bool _sortAsc = false;
    private readonly Dictionary<string, HashSet<string>> _selectedJobsByClub = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rowStatus = new();
    private bool _openInviteByUidRequested;
    private bool _inviteModalOpen;
    private string _inviteUid = string.Empty;
    private readonly HashSet<string> _inviteJobsInline = new(StringComparer.Ordinal);
    private string _inviteStatus = string.Empty;
    private string _manualDisplayName = string.Empty;
    private readonly HashSet<string> _manualJobsInline = new(StringComparer.Ordinal);
    private string _manualAddStatus = string.Empty;
    private bool _manualBirthdayEnabled;
    private int _manualBirthdayMonth = 1;
    private int _manualBirthdayDay = 1;
    private string _manualBirthdayYearInput = string.Empty;
    private string _editBirthdayUser = string.Empty;
    private string _editBirthdayUserSnapshot = string.Empty;
    private bool _editBirthdayEnabled;
    private int _editBirthdayMonth = 1;
    private int _editBirthdayDay = 1;
    private string _editBirthdayYearInput = string.Empty;
    private string _editBirthdayStatus = string.Empty;
    private string _manualLinkManualUid = string.Empty;
    private string _manualLinkTargetUid = string.Empty;
    private string _manualLinkStatus = string.Empty;
    private bool _focusManualLinkTarget;
    private string[] _jobOptions = new[] { "Unassigned", "Greeter", "Barkeeper", "Dancer", "Escort" };
    private readonly System.Collections.Generic.HashSet<string> _dirtyKeys = new(System.StringComparer.Ordinal);
    private bool _inviteInlineOpen;
    private bool _manualLinkInlineOpen;
    private bool _confirmOpen;
    private string _confirmUser = string.Empty;
    private string[] _confirmNewJobs = Array.Empty<string>();
    private string _confirmStatus = string.Empty;
    private bool _editBirthdayOpen;
    private int _pageIndex;
    private string _pageFilter = string.Empty;

    public void TriggerRefresh(VenuePlusApp app)
    {
        _status = string.Empty;
        System.Threading.Tasks.Task.Run(async () =>
        {
            var list = await app.FetchStaffUsersDetailedAsync();
            _users = list ?? Array.Empty<StaffUser>();
            _status = _users.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var clubId = app.CurrentClubId;
            var present = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var u in _users)
            {
                var key = clubId + "|" + u.Username;
                present.Add(key);
                if (!_selectedJobsByClub.ContainsKey(key) || !_dirtyKeys.Contains(key))
                {
                    _selectedJobsByClub[key] = new HashSet<string>(NormalizeJobs(GetJobsFromUser(u)), StringComparer.Ordinal);
                }
                _rowStatus[u.Username] = string.Empty;
            }
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var k in _selectedJobsByClub.Keys)
            {
                if (!present.Contains(k)) toRemove.Add(k);
            }
            foreach (var k in toRemove) _selectedJobsByClub.Remove(k);
            var toRemoveStatus = new System.Collections.Generic.List<string>();
            foreach (var uname in _rowStatus.Keys)
            {
                var key = clubId + "|" + uname;
                if (!present.Contains(key)) toRemoveStatus.Add(uname);
            }
            foreach (var uname in toRemoveStatus) _rowStatus.Remove(uname);
        });
    }

    public void Draw(VenuePlusApp app)
    {
        
        ImGui.Spacing();
        var canInvite = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
        if (!canInvite)
        {
            _inviteInlineOpen = false;
            _manualLinkInlineOpen = false;
            _focusManualLinkTarget = false;
        }
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##staff_filter", "Search by username or job", ref _filter, 128);
        ImGui.PopItemWidth();
        if (canInvite)
        {
            var styleStaff = ImGui.GetStyle();
            var btnWStaff = ImGui.CalcTextSize("Add Staff").X + styleStaff.FramePadding.X * 2f;
            ImGui.SameLine();
            var rightXStaff = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - btnWStaff;
            ImGui.SetCursorPosX(rightXStaff);
            if (ImGui.Button("Add Staff##invite_open"))
            {
                _inviteInlineOpen = true;
                _manualLinkInlineOpen = false;
                _inviteStatus = string.Empty;
                _inviteUid = string.Empty;
                _inviteJobsInline.Clear();
                _inviteJobsInline.Add("Unassigned");
                _manualDisplayName = string.Empty;
                _manualJobsInline.Clear();
                _manualJobsInline.Add("Unassigned");
                _manualAddStatus = string.Empty;
                var nowBday = DateTime.UtcNow;
                _manualBirthdayMonth = nowBday.Month;
                _manualBirthdayDay = nowBday.Day;
                _manualBirthdayYearInput = string.Empty;
                _manualBirthdayEnabled = false;
                _manualLinkManualUid = string.Empty;
                _manualLinkTargetUid = string.Empty;
                _manualLinkStatus = string.Empty;
                _focusManualLinkTarget = false;
            }
        }
        if (_inviteInlineOpen && canInvite)
        {
            ImGui.Separator();
            ImGui.PushItemWidth(150f);
            ImGui.InputTextWithHint("##invite_uid_inline", "Target UID", ref _inviteUid, 24);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            var currentJob = FormatJobs(NormalizeJobs(_inviteJobsInline));
            ImGui.PushItemWidth(150f);
                if (ImGui.BeginCombo("##invite_job_inline", currentJob))
                {
                    var rights = app.GetJobRightsCache();
                    var names = _jobOptions ?? Array.Empty<string>();
                    System.Array.Sort(names, (a, b) =>
                    {
                        if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) return string.Equals(b, "Owner", System.StringComparison.Ordinal) ? 0 : -1;
                        if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) return 1;
                        int ra = 1;
                        int rb = 1;
                        if (rights != null && rights.TryGetValue(a, out var ia)) ra = ia.Rank; else if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) ra = 10; else if (string.Equals(a, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                        if (rights != null && rights.TryGetValue(b, out var ib)) rb = ib.Rank; else if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) rb = 10; else if (string.Equals(b, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                        int cmp = rb.CompareTo(ra);
                        if (cmp != 0) return cmp;
                        return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
                    });
                    foreach (var name in names)
                    {
                        if (string.Equals(name, "Owner", System.StringComparison.Ordinal) && !app.IsOwnerCurrentClub) continue;
                        var rightsCache2 = rights;
                        if (rightsCache2 != null && rightsCache2.TryGetValue(name, out var infoOpt))
                        {
                            var col2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoOpt.ColorHex);
                            var icon2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoOpt.IconKey);
                            VenuePlus.Helpers.IconDraw.IconText(icon2, 0.9f, col2);
                            ImGui.SameLine();
                        }
                        bool selected = _inviteJobsInline.Contains(name);
                        if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.DontClosePopups))
                        {
                            if (selected) _inviteJobsInline.Remove(name);
                            else _inviteJobsInline.Add(name);
                            NormalizeJobSet(_inviteJobsInline);
                        }
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_inviteUid));
            if (ImGui.Button("Add##invite_submit"))
            {
                _inviteStatus = "Submitting...";
                var uid = _inviteUid;
                var jobsInline = NormalizeJobs(_inviteJobsInline);
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.InviteStaffByUidAsync(uid, jobsInline);
                    _inviteStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Invite failed");
                    if (ok)
                    {
                        _inviteInlineOpen = false;
                        _inviteUid = string.Empty;
                        _inviteJobsInline.Clear();
                        TriggerRefresh(app);
                    }
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close##invite_inline_close")) { _inviteInlineOpen = false; _inviteStatus = string.Empty; }
            if (!string.IsNullOrEmpty(_inviteStatus)) { ImGui.TextUnformatted(_inviteStatus); }
            ImGui.Separator();
            ImGui.TextUnformatted("Manual Staff");
            ImGui.PushItemWidth(150f);
            ImGui.InputTextWithHint("##manual_name_inline", "Display name", ref _manualDisplayName, 64);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            var manualJob = FormatJobs(NormalizeJobs(_manualJobsInline));
            ImGui.PushItemWidth(150f);
            if (ImGui.BeginCombo("##manual_job_inline", manualJob))
            {
                var rights = app.GetJobRightsCache();
                var names = _jobOptions ?? Array.Empty<string>();
                System.Array.Sort(names, (a, b) =>
                {
                    if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) return string.Equals(b, "Owner", System.StringComparison.Ordinal) ? 0 : -1;
                    if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) return 1;
                    int ra = 1;
                    int rb = 1;
                    if (rights != null && rights.TryGetValue(a, out var ia)) ra = ia.Rank; else if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) ra = 10; else if (string.Equals(a, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                    if (rights != null && rights.TryGetValue(b, out var ib)) rb = ib.Rank; else if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) rb = 10; else if (string.Equals(b, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                    int cmp = rb.CompareTo(ra);
                    if (cmp != 0) return cmp;
                    return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
                });
                foreach (var name in names)
                {
                    if (string.Equals(name, "Owner", System.StringComparison.Ordinal) && !app.IsOwnerCurrentClub) continue;
                    var rightsCache2 = rights;
                    if (rightsCache2 != null && rightsCache2.TryGetValue(name, out var infoOpt))
                    {
                        var col2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoOpt.ColorHex);
                        var icon2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoOpt.IconKey);
                        VenuePlus.Helpers.IconDraw.IconText(icon2, 0.9f, col2);
                        ImGui.SameLine();
                    }
                    bool selected = _manualJobsInline.Contains(name);
                    if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (selected) _manualJobsInline.Remove(name);
                        else _manualJobsInline.Add(name);
                        NormalizeJobSet(_manualJobsInline);
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            ImGui.Spacing();
            ImGui.TextDisabled("Birthday");
            ImGui.SameLine();
            var manualBirthdayEnabled = _manualBirthdayEnabled;
            if (ImGui.Checkbox("Set birthday##manual_birthday_enable", ref manualBirthdayEnabled))
            {
                _manualBirthdayEnabled = manualBirthdayEnabled;
            }
            ImGui.BeginDisabled(!_manualBirthdayEnabled);
            var monthNames = new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
            var monthIndex = _manualBirthdayMonth - 1;
            if (monthIndex < 0) monthIndex = 0;
            if (monthIndex > 11) monthIndex = 11;
            ImGui.PushItemWidth(140f);
            if (ImGui.Combo("##manual_birthday_month", ref monthIndex, monthNames, monthNames.Length))
            {
                _manualBirthdayMonth = monthIndex + 1;
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            var yearForDays = 2000;
            if (!string.IsNullOrWhiteSpace(_manualBirthdayYearInput) && int.TryParse(_manualBirthdayYearInput, out var parsedYear) && parsedYear >= 1 && parsedYear <= 9999)
            {
                yearForDays = parsedYear;
            }
            var maxDay = DateTime.DaysInMonth(yearForDays, _manualBirthdayMonth);
            if (_manualBirthdayDay > maxDay) _manualBirthdayDay = maxDay;
            if (_manualBirthdayDay < 1) _manualBirthdayDay = 1;
            var dayLabels = new string[maxDay];
            for (int i = 0; i < maxDay; i++) dayLabels[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
            var dayIndex = _manualBirthdayDay - 1;
            ImGui.PushItemWidth(90f);
            if (ImGui.Combo("##manual_birthday_day", ref dayIndex, dayLabels, dayLabels.Length))
            {
                _manualBirthdayDay = dayIndex + 1;
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.PushItemWidth(80f);
            var yearText = _manualBirthdayYearInput;
            if (ImGui.InputTextWithHint("##manual_birthday_year", "Year", ref yearText, 4))
            {
                _manualBirthdayYearInput = yearText;
            }
            ImGui.PopItemWidth();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_manualDisplayName));
            if (ImGui.Button("Create##manual_add_inline"))
            {
                _manualAddStatus = "Submitting...";
                var name = _manualDisplayName;
                var jobsInline = NormalizeJobs(_manualJobsInline);
                DateTimeOffset? birthday = null;
                if (_manualBirthdayEnabled)
                {
                    var yearTextLocal = _manualBirthdayYearInput?.Trim() ?? string.Empty;
                    var yearValue = 2000;
                    if (!string.IsNullOrWhiteSpace(yearTextLocal))
                    {
                        if (!int.TryParse(yearTextLocal, out yearValue) || yearValue < 1 || yearValue > 9999)
                        {
                            _manualAddStatus = "Invalid year. Use 1 to 9999 or leave empty.";
                            return;
                        }
                    }
                    try
                    {
                        var normalized = new DateTime(yearValue, _manualBirthdayMonth, _manualBirthdayDay, 0, 0, 0, DateTimeKind.Utc);
                        birthday = new DateTimeOffset(normalized, TimeSpan.Zero);
                    }
                    catch
                    {
                        _manualAddStatus = "Invalid date.";
                        return;
                    }
                }
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.CreateManualStaffEntryAsync(name, jobsInline, birthday);
                    _manualAddStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                    if (ok)
                    {
                        _manualDisplayName = string.Empty;
                        _manualJobsInline.Clear();
                        _manualJobsInline.Add("Unassigned");
                        var now = DateTime.UtcNow;
                        _manualBirthdayMonth = now.Month;
                        _manualBirthdayDay = now.Day;
                        _manualBirthdayYearInput = string.Empty;
                        _manualBirthdayEnabled = false;
                        TriggerRefresh(app);
                    }
                });
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrEmpty(_manualAddStatus)) { ImGui.TextUnformatted(_manualAddStatus); }
            ImGui.Separator();
        }
        if (_manualLinkInlineOpen && canInvite)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Link Manual Entry");
            ImGui.PushItemWidth(150f);
            ImGui.InputTextWithHint("##manual_uid_inline", "Manual UID", ref _manualLinkManualUid, 24);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.PushItemWidth(150f);
            if (_focusManualLinkTarget) ImGui.SetKeyboardFocusHere();
            ImGui.InputTextWithHint("##manual_target_uid_inline", "Target UID", ref _manualLinkTargetUid, 24);
            ImGui.PopItemWidth();
            _focusManualLinkTarget = false;
            ImGui.SameLine();
            var disableLink = string.IsNullOrWhiteSpace(_manualLinkManualUid) || string.IsNullOrWhiteSpace(_manualLinkTargetUid);
            ImGui.BeginDisabled(disableLink);
            if (ImGui.Button("Link##manual_link_inline"))
            {
                _manualLinkStatus = "Submitting...";
                var manualUid = _manualLinkManualUid;
                var targetUid = _manualLinkTargetUid;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.LinkManualStaffEntryAsync(manualUid, targetUid);
                    _manualLinkStatus = ok ? "Linked" : (app.GetLastServerMessage() ?? "Link failed");
                    if (ok)
                    {
                        _manualLinkManualUid = string.Empty;
                        _manualLinkTargetUid = string.Empty;
                        TriggerRefresh(app);
                    }
                });
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrEmpty(_manualLinkStatus)) { ImGui.TextUnformatted(_manualLinkStatus); }
            ImGui.Separator();
        }
        if (canInvite && _editBirthdayOpen)
        {
            var nameList = new List<string>();
            for (int i = 0; i < _users.Length; i++)
            {
                var uname = _users[i].Username ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(uname) && _users[i].IsManual) nameList.Add(uname);
            }
            var namesArr = nameList.ToArray();
            Array.Sort(namesArr, StringComparer.OrdinalIgnoreCase);
            if (namesArr.Length > 0)
            {
                var selectedIndex = Array.IndexOf(namesArr, _editBirthdayUser);
                if (selectedIndex < 0)
                {
                    _editBirthdayUser = namesArr[0];
                }
                if (!string.Equals(_editBirthdayUserSnapshot, _editBirthdayUser, StringComparison.Ordinal))
                {
                    _editBirthdayUserSnapshot = _editBirthdayUser;
                    var now = DateTime.UtcNow;
                    _editBirthdayMonth = now.Month;
                    _editBirthdayDay = now.Day;
                    _editBirthdayYearInput = string.Empty;
                    _editBirthdayEnabled = false;
                    for (int i = 0; i < _users.Length; i++)
                    {
                        var u = _users[i];
                        if (!string.Equals(u.Username, _editBirthdayUser, StringComparison.Ordinal)) continue;
                        if (u.Birthday.HasValue)
                        {
                            var bday = u.Birthday.Value.UtcDateTime;
                            _editBirthdayMonth = bday.Month;
                            _editBirthdayDay = bday.Day;
                            _editBirthdayYearInput = bday.Year == 2000 ? string.Empty : bday.Year.ToString(CultureInfo.InvariantCulture);
                            _editBirthdayEnabled = true;
                        }
                        break;
                    }
                }
                ImGui.Separator();
                ImGui.TextUnformatted("Edit Birthday");
                ImGui.SameLine();
                if (ImGui.Button("Close##edit_birthday_close"))
                {
                    _editBirthdayOpen = false;
                    _editBirthdayStatus = string.Empty;
                }
                ImGui.TextUnformatted($"User: {_editBirthdayUser}");
                var editEnabled = _editBirthdayEnabled;
                if (ImGui.Checkbox("Has birthday##edit_birthday_enable", ref editEnabled))
                {
                    _editBirthdayEnabled = editEnabled;
                }
                ImGui.BeginDisabled(!_editBirthdayEnabled);
                var editMonthNames = new[]
                {
                    "January", "February", "March", "April", "May", "June",
                    "July", "August", "September", "October", "November", "December"
                };
                var editMonthIndex = _editBirthdayMonth - 1;
                if (editMonthIndex < 0) editMonthIndex = 0;
                if (editMonthIndex > 11) editMonthIndex = 11;
                ImGui.PushItemWidth(140f);
                if (ImGui.Combo("##edit_birthday_month", ref editMonthIndex, editMonthNames, editMonthNames.Length))
                {
                    _editBirthdayMonth = editMonthIndex + 1;
                }
                ImGui.PopItemWidth();
                ImGui.SameLine();
                var editYearForDays = 2000;
                if (!string.IsNullOrWhiteSpace(_editBirthdayYearInput) && int.TryParse(_editBirthdayYearInput, out var editParsedYear) && editParsedYear >= 1 && editParsedYear <= 9999)
                {
                    editYearForDays = editParsedYear;
                }
                var editMaxDay = DateTime.DaysInMonth(editYearForDays, _editBirthdayMonth);
                if (_editBirthdayDay > editMaxDay) _editBirthdayDay = editMaxDay;
                if (_editBirthdayDay < 1) _editBirthdayDay = 1;
                var editDayLabels = new string[editMaxDay];
                for (int i = 0; i < editMaxDay; i++) editDayLabels[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
                var editDayIndex = _editBirthdayDay - 1;
                ImGui.PushItemWidth(90f);
                if (ImGui.Combo("##edit_birthday_day", ref editDayIndex, editDayLabels, editDayLabels.Length))
                {
                    _editBirthdayDay = editDayIndex + 1;
                }
                ImGui.PopItemWidth();
                ImGui.SameLine();
                ImGui.PushItemWidth(80f);
                var editYearText = _editBirthdayYearInput;
                if (ImGui.InputTextWithHint("##edit_birthday_year", "Year", ref editYearText, 4))
                {
                    _editBirthdayYearInput = editYearText;
                }
                ImGui.PopItemWidth();
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_editBirthdayUser));
                if (ImGui.Button("Save##edit_birthday_save"))
                {
                    _editBirthdayStatus = "Submitting...";
                    DateTimeOffset? birthday = null;
                    var invalidBirthday = false;
                    if (_editBirthdayEnabled)
                    {
                        var yearTextLocal = _editBirthdayYearInput?.Trim() ?? string.Empty;
                        var yearValue = 2000;
                        if (!string.IsNullOrWhiteSpace(yearTextLocal))
                        {
                            if (!int.TryParse(yearTextLocal, out yearValue) || yearValue < 1 || yearValue > 9999)
                            {
                                _editBirthdayStatus = "Invalid year. Use 1 to 9999 or leave empty.";
                                invalidBirthday = true;
                            }
                        }
                        if (!invalidBirthday)
                        {
                            try
                            {
                                var normalized = new DateTime(yearValue, _editBirthdayMonth, _editBirthdayDay, 0, 0, 0, DateTimeKind.Utc);
                                birthday = new DateTimeOffset(normalized, TimeSpan.Zero);
                            }
                            catch
                            {
                                _editBirthdayStatus = "Invalid date.";
                                invalidBirthday = true;
                            }
                        }
                    }
                    if (!invalidBirthday)
                    {
                        var username = _editBirthdayUser;
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await app.UpdateStaffBirthdayAsync(username, birthday);
                            _editBirthdayStatus = ok ? (_editBirthdayEnabled ? "Birthday updated" : "Birthday cleared") : (app.GetLastServerMessage() ?? "Update failed");
                            if (ok) TriggerRefresh(app);
                        });
                    }
                }
                ImGui.EndDisabled();
                if (!string.IsNullOrEmpty(_editBirthdayStatus)) { ImGui.TextUnformatted(_editBirthdayStatus); }
            }
            else
            {
                ImGui.Separator();
                ImGui.TextDisabled("No manual entries available");
            }
        }
        ImGui.Separator();
        var style = ImGui.GetStyle();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        var canAct = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
        float actionsWidth = 0f;
        int actionsCount = 0;
        if (canAct)
        {
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Trash.ToIconString()).X + style.FramePadding.X * 2f; actionsCount++;
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Save.ToIconString()).X + style.FramePadding.X * 2f; actionsCount++;
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Calendar.ToIconString()).X + style.FramePadding.X * 2f; actionsCount++;
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Link.ToIconString()).X + style.FramePadding.X * 2f; actionsCount++;
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (actionsCount > 1) actionsWidth += style.ItemSpacing.X * (actionsCount - 1);
        var showActions = actionsCount > 0;
        if (showActions && actionsWidth <= 0f) actionsWidth = ImGui.GetFrameHeight() * 1.5f;

        if (!string.Equals(_pageFilter, _filter, StringComparison.Ordinal))
        {
            _pageFilter = _filter;
            _pageIndex = 0;
        }
        var visible = ApplyFilter(_users, _filter);
        System.Array.Sort(visible, (a, b) =>
        {
            int r = 0;
            switch (_sortCol)
            {
                case 0:
                    r = string.Compare(a.Username, b.Username, System.StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    var rights = app.GetJobRightsCache();
                    int ra = 1;
                    int rb = 1;
                    var aj = GetPrimaryJob(rights, NormalizeJobs(GetJobsFromUser(a)));
                    var bj = GetPrimaryJob(rights, NormalizeJobs(GetJobsFromUser(b)));
                    if (rights != null && rights.TryGetValue(aj, out var ia)) ra = ia.Rank;
                    else if (string.Equals(aj, "Owner", System.StringComparison.Ordinal)) ra = 10;
                    else if (string.Equals(aj, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                    if (rights != null && rights.TryGetValue(bj, out var ib)) rb = ib.Rank;
                    else if (string.Equals(bj, "Owner", System.StringComparison.Ordinal)) rb = 10;
                    else if (string.Equals(bj, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                    r = ra.CompareTo(rb);
                    if (!_sortAsc) r = -r;
                    if (r == 0) r = string.Compare(aj, bj, System.StringComparison.OrdinalIgnoreCase);
                    break;
            }
            return r;
        });
        const int pageSize = 15;
        var totalCount = visible.Length;
        var totalPages = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        if (_pageIndex >= totalPages) _pageIndex = totalPages - 1;
        if (_pageIndex < 0) _pageIndex = 0;
        ImGui.BeginDisabled(_pageIndex <= 0);
        if (ImGui.Button("Prev##staff_page_prev")) { _pageIndex--; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Page {_pageIndex + 1} / {totalPages}");
        ImGui.SameLine();
        ImGui.BeginDisabled(_pageIndex >= totalPages - 1);
        if (ImGui.Button("Next##staff_page_next")) { _pageIndex++; }
        ImGui.EndDisabled();
        ImGui.Separator();
        var startIndex = _pageIndex * pageSize;
        var pageCount = Math.Min(pageSize, Math.Max(0, totalCount - startIndex));
        var pageItems = new StaffUser[pageCount];
        if (pageCount > 0) Array.Copy(visible, startIndex, pageItems, 0, pageCount);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("staff_table", showActions ? 3 : 2, flags))
        {
            ImGui.TableSetupColumn("Username", ImGuiTableColumnFlags.WidthFixed, 170f);
            ImGui.TableSetupColumn("Job");
            if (showActions)
            {
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            }
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Username", 0, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Job", 1, ref _sortCol, ref _sortAsc);
            if (showActions)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted("Actions");
            }
            int rowIndex = 0;
            foreach (var u in pageItems)
            {
                var rowId = !string.IsNullOrWhiteSpace(u.Uid) ? u.Uid : u.Username;
                if (!string.IsNullOrWhiteSpace(rowId)) ImGui.PushID(rowId);
                else ImGui.PushID(rowIndex);
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                ImGui.TableSetColumnIndex(0);
                var baseY = ImGui.GetCursorPosY();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.8f);
                var statusIconH = ImGui.CalcTextSize(FontAwesomeIcon.Circle.ToIconString()).Y;
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                var fullName = u.Username ?? string.Empty;
                string displayName = fullName;
                string? homeWorld = null;
                var atIndex = fullName.IndexOf('@');
                if (atIndex > 0 && atIndex < fullName.Length - 1)
                {
                    displayName = fullName.Substring(0, atIndex);
                    homeWorld = fullName[(atIndex + 1)..];
                }
                var textH = ImGui.CalcTextSize(displayName).Y;
                const float nameOffsetY = -1f;
                var contentH = statusIconH > textH ? statusIconH : textH;
                ImGui.SetCursorPosY(baseY + (rowH - contentH) / 2f);
                if (u.IsOnline)
                {
                    var t = (float)ImGui.GetTime();
                    var pulse = 0.5f + 0.5f * System.MathF.Sin(t * 3f);
                    var c1 = new Vector4(0.1f, 0.9f, 0.3f, 1f);
                    var c2 = new Vector4(0.1f, 0.7f, 0.3f, 1f);
                    var col = new Vector4(c1.X + (c2.X - c1.X) * pulse, c1.Y + (c2.Y - c1.Y) * pulse, c1.Z + (c2.Z - c1.Z) * pulse, 1f);
                    var colU32 = ImGui.ColorConvertFloat4ToU32(col);
                    IconDraw.IconText(FontAwesomeIcon.Circle, 0.8f, colU32);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Online");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine(0f, 6f);
                }
                else
                {
                    var colU32 = ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0f, 0f, 1f));
                    IconDraw.IconText(FontAwesomeIcon.Circle, 0.8f, colU32);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Offline");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine(0f, 6f);
                }
                ImGui.SetCursorPosY(baseY + (rowH - textH) / 2f + nameOffsetY);
                ImGui.TextUnformatted(displayName);
                var showTooltip = ImGui.IsItemHovered();
                if (u.IsManual)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("Manual (Editable)");
                    showTooltip = showTooltip || ImGui.IsItemHovered();
                }
                if (showTooltip)
                {
                    ImGui.BeginTooltip();
                    var createdStr = u.CreatedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "--";
                    ImGui.TextUnformatted($"Added: {createdStr}");
                    if (!string.IsNullOrWhiteSpace(homeWorld)) ImGui.TextUnformatted($"Homeworld: {homeWorld}");
                    if (u.IsManual) ImGui.TextUnformatted("Manual Entry (Editable)");
                    ImGui.EndTooltip();
                }
                ImGui.TableSetColumnIndex(1);
                var key = app.CurrentClubId + "|" + u.Username;
            if (!_selectedJobsByClub.TryGetValue(key, out var currentSet))
            {
                currentSet = new HashSet<string>(NormalizeJobs(GetJobsFromUser(u)), StringComparer.Ordinal);
                _selectedJobsByClub[key] = currentSet;
            }
            var currentArr = NormalizeJobs(currentSet);
            var currentPrimary = GetPrimaryJob(app.GetJobRightsCache(), currentArr);
            var currentDisplayArr = currentArr;
            var rightsCachePreview = app.GetJobRightsCache();
            if (rightsCachePreview != null && currentArr.Length > 1)
            {
                var sorted = new string[currentArr.Length];
                Array.Copy(currentArr, sorted, currentArr.Length);
                Array.Sort(sorted, (a, b) =>
                {
                    if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) return string.Equals(b, "Owner", System.StringComparison.Ordinal) ? 0 : -1;
                    if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) return 1;
                    int ra = 1;
                    int rb = 1;
                    if (rightsCachePreview.TryGetValue(a, out var ia)) ra = ia.Rank; else if (string.Equals(a, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                    if (rightsCachePreview.TryGetValue(b, out var ib)) rb = ib.Rank; else if (string.Equals(b, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                    int cmp = rb.CompareTo(ra);
                    if (cmp != 0) return cmp;
                    return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
                });
                currentDisplayArr = sorted;
            }
            var yBase = ImGui.GetCursorPosY();
            var cellStartX = ImGui.GetCursorPosX();
            var cellWidth = ImGui.GetContentRegionAvail().X;
            float jobIconH;
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            jobIconH = ImGui.CalcTextSize(FontAwesomeIcon.Square.ToIconString()).Y;
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
            var centerYIcon = yBase + (rowH - jobIconH) / 2f;
            ImGui.SetCursorPosY(centerYIcon);
            var rightsCachePre2 = app.GetJobRightsCache();
            for (int i = 0; i < currentDisplayArr.Length; i++)
            {
                var jn = currentDisplayArr[i];
                if (rightsCachePre2 != null && rightsCachePre2.TryGetValue(jn, out var infoPre2))
                {
                    var iconPre2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoPre2.IconKey);
                    var colPre2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoPre2.ColorHex);
                    IconDraw.IconText(iconPre2, 0.9f, colPre2);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(jn ?? string.Empty);
                        ImGui.EndTooltip();
                    }
                }
                else
                {
                    ImGui.TextUnformatted(jn ?? string.Empty);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(jn ?? string.Empty);
                        ImGui.EndTooltip();
                    }
                }
                if (i + 1 < currentDisplayArr.Length) ImGui.SameLine(0f, 6f);
            }
            var styleCell = ImGui.GetStyle();
            float comboW = ImGui.GetFrameHeight();
            float infoW;
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            infoW = ImGui.CalcTextSize(FontAwesomeIcon.QuestionCircle.ToIconString()).X;
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
            float totalW = comboW + styleCell.ItemSpacing.X + infoW;
            ImGui.SameLine();
            ImGui.SetCursorPosX(cellStartX + cellWidth - totalW);
            ImGui.SetCursorPosY(centerYIcon);
            ImGui.PushItemWidth(comboW);
            var isOwnerJob = HasOwner(currentArr);
            ImGui.BeginDisabled(!canAct || (isOwnerJob && !app.IsOwnerCurrentClub));
            if (ImGui.BeginCombo($"##job_{u.Username}", string.Empty, ImGuiComboFlags.NoPreview))
            {
                if (currentArr.Length == 1 && Array.IndexOf(_jobOptions ?? Array.Empty<string>(), currentArr[0]) < 0)
                    {
                        bool selected = true;
                    if (ImGui.Selectable(currentArr[0], selected)) { _selectedJobsByClub[key] = new HashSet<string>(currentArr, StringComparer.Ordinal); _dirtyKeys.Add(key); }
                    }
                    var rights = app.GetJobRightsCache();
                    var names = _jobOptions ?? Array.Empty<string>();
                    System.Array.Sort(names, (a, b) =>
                    {
                        if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) return string.Equals(b, "Owner", System.StringComparison.Ordinal) ? 0 : -1;
                        if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) return 1;
                        int ra = 1;
                        int rb = 1;
                        if (rights != null && rights.TryGetValue(a, out var ia)) ra = ia.Rank; else if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) ra = 10; else if (string.Equals(a, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                        if (rights != null && rights.TryGetValue(b, out var ib)) rb = ib.Rank; else if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) rb = 10; else if (string.Equals(b, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                        int cmp = rb.CompareTo(ra);
                        if (cmp != 0) return cmp;
                        return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
                    });
                    foreach (var name in names)
                    {
                        if (string.Equals(name, "Owner", System.StringComparison.Ordinal) && !app.IsOwnerCurrentClub) continue;
                        var rightsCache2 = rights;
                        if (rightsCache2 != null && rightsCache2.TryGetValue(name, out var infoOpt))
                        {
                            var col2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoOpt.ColorHex);
                            var icon2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoOpt.IconKey);
                            IconDraw.IconText(icon2, 0.9f, col2);
                            ImGui.SameLine();
                        }
                        bool selected = currentSet.Contains(name);
                        if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.DontClosePopups))
                        {
                            if (selected) currentSet.Remove(name);
                            else currentSet.Add(name);
                            NormalizeJobSet(currentSet);
                            _dirtyKeys.Add(key);
                        }
                    }
                    ImGui.EndCombo();
                }
            ImGui.EndDisabled();
            ImGui.PopItemWidth();
            ImGui.SameLine(0f, styleCell.ItemSpacing.X);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Select the staff member's job for the venue");
                    ImGui.EndTooltip();
                }
                if (showActions)
                {
                    ImGui.TableSetColumnIndex(2);
                    if (canAct)
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        var yBaseAct = ImGui.GetCursorPosY();
                        var centerY = yBaseAct + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        var isOwnerRow = _selectedJobsByClub.TryGetValue(key, out var ownerSet) && ownerSet.Contains("Owner");
                        var isSelfRow = !string.IsNullOrWhiteSpace(app.CurrentStaffUsername) && string.Equals(u.Username, app.CurrentStaffUsername, System.StringComparison.Ordinal);
                        var ctrlDown = ImGui.GetIO().KeyCtrl;
                        ImGui.BeginDisabled(!ctrlDown || isOwnerRow || isSelfRow);
                        if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + $"##rm_{u.Username}"))
                        {
                            _rowStatus[u.Username] = "Removing...";
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                var ok = await app.DeleteStaffUserAsync(u.Username);
                                _rowStatus[u.Username] = ok ? "Removed" : (app.GetLastServerMessage() ?? "Remove failed");
                            });
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(isOwnerRow ? "Owner cannot be removed" : (isSelfRow ? "You cannot remove yourself" : (ctrlDown ? "Remove this staff member" : "Hold Ctrl to remove this staff member"))); ImGui.EndTooltip(); }

                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBaseAct + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        var newJobsSelf = NormalizeJobs(currentSet);
                        var rightsCacheSave = app.GetJobRightsCache();
                        var selfLosesManageJobs = isSelfRow && !isOwnerRow && !HasManageJobs(rightsCacheSave, newJobsSelf);
                        ImGui.BeginDisabled((isOwnerRow && !app.IsOwnerCurrentClub) || selfLosesManageJobs);
                        if (ImGui.Button(FontAwesomeIcon.Save.ToIconString() + $"##save_{u.Username}"))
                        {
                            var prevJobs = NormalizeJobs(GetJobsFromUser(u));
                            var newJobs = NormalizeJobs(currentSet);
                            var ownerChange = HasOwner(prevJobs) != HasOwner(newJobs);
                            if (ownerChange)
                            {
                                _confirmOpen = true;
                                _confirmUser = u.Username;
                                _confirmNewJobs = newJobs;
                                _confirmStatus = string.Empty;
                            }
                            else
                            {
                                _rowStatus[u.Username] = "Saving...";
                                System.Threading.Tasks.Task.Run(async () =>
                                {
                                var ok = await app.UpdateStaffUserJobsAsync(u.Username, newJobs);
                                _rowStatus[u.Username] = ok ? "Saved" : (app.GetLastServerMessage() ?? "Save failed");
                                if (ok)
                                {
                                    var k = key;
                                    _dirtyKeys.Remove(k);
                                }
                            });
                            }
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(isOwnerRow ? "Owner job cannot be changed" : (selfLosesManageJobs ? "You cannot assign yourself a role that removes role editing rights" : "Save changes to this staff member's job")); ImGui.EndTooltip(); }

                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBaseAct + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        ImGui.BeginDisabled(!u.IsManual);
                        if (ImGui.Button(FontAwesomeIcon.Calendar.ToIconString() + $"##birthday_{u.Username}"))
                        {
                            _editBirthdayOpen = true;
                            _editBirthdayUser = u.Username;
                            _editBirthdayUserSnapshot = string.Empty;
                            _editBirthdayStatus = string.Empty;
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(!u.IsManual ? "Only manual entries can be edited" : "Edit birthday"); ImGui.EndTooltip(); }

                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBaseAct + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        ImGui.BeginDisabled(!u.IsManual || isOwnerRow || isSelfRow);
                        if (ImGui.Button(FontAwesomeIcon.Link.ToIconString() + $"##link_{u.Username}"))
                        {
                            _inviteInlineOpen = false;
                            _manualLinkInlineOpen = true;
                            _manualLinkManualUid = u.Uid ?? string.Empty;
                            _manualLinkTargetUid = string.Empty;
                            _manualLinkStatus = string.Empty;
                            _focusManualLinkTarget = true;
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(!u.IsManual ? "Only manual entries can be linked" : (isOwnerRow ? "Owner cannot be linked" : (isSelfRow ? "You cannot link yourself" : "Link this manual entry to a real user UID"))); ImGui.EndTooltip(); }
                    }
                    if (_rowStatus.TryGetValue(u.Username, out var rs) && !string.IsNullOrEmpty(rs))
                    {
                        ImGui.SameLine();
                        ImGui.TextUnformatted(rs);
                    }
                }
                ImGui.PopID();
                rowIndex++;
            }
            ImGui.EndTable();
        }

        if (_confirmOpen)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Confirm owner role change");
            ImGui.SameLine();
            if (ImGui.Button("Confirm##owner_change_confirm"))
            {
                _confirmStatus = "Saving...";
                var uname = _confirmUser;
                var jobs = _confirmNewJobs;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.UpdateStaffUserJobsAsync(uname, jobs);
                    _confirmStatus = ok ? "Saved" : (app.GetLastServerMessage() ?? "Save failed");
                    if (ok)
                    {
                        _confirmOpen = false;
                        _rowStatus[uname] = "Saved";
                        var k = app.CurrentClubId + "|" + uname;
                        _dirtyKeys.Remove(k);
                    }
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##owner_change_cancel"))
            {
                _confirmOpen = false;
                _confirmStatus = string.Empty;
            }
            if (!string.IsNullOrEmpty(_confirmStatus)) ImGui.TextUnformatted(_confirmStatus);
            ImGui.Separator();
        }
        
    }

    private void DrawInviteByUidModal(VenuePlusApp app)
    {
        if (_openInviteByUidRequested)
        {
            ImGui.OpenPopup("Add Staff by UID");
            _openInviteByUidRequested = false;
            _inviteModalOpen = true;
            _inviteStatus = string.Empty;
            _inviteUid = string.Empty;
        }
        if (_inviteModalOpen && ImGui.BeginPopupModal("Add Staff by UID", ref _inviteModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("User UID", ref _inviteUid, 64);
            ImGui.SameLine();
            var canInvite = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_inviteUid) || !canInvite);
            if (ImGui.Button("Add##invite_modal_submit"))
            {
                _inviteStatus = "Submitting...";
                var uid = _inviteUid;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.InviteStaffByUidAsync(uid, Array.Empty<string>());
                    _inviteStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Invite failed");
                    if (ok) _inviteModalOpen = false;
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close##invite_modal_close")) { _inviteModalOpen = false; _inviteStatus = string.Empty; }
            if (!string.IsNullOrEmpty(_inviteStatus)) { ImGui.TextUnformatted(_inviteStatus); }
            ImGui.EndPopup();
        }
    }

    public void SetUsersFromServer(VenuePlusApp app, StaffUser[] users)
    {
        _users = users ?? Array.Empty<StaffUser>();
        var clubId = app.CurrentClubId;
        var present = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var u in _users)
        {
            var key = clubId + "|" + u.Username;
            present.Add(key);
            if (!_selectedJobsByClub.ContainsKey(key) || !_dirtyKeys.Contains(key))
            {
                _selectedJobsByClub[key] = new HashSet<string>(NormalizeJobs(GetJobsFromUser(u)), StringComparer.Ordinal);
            }
            _rowStatus[u.Username] = string.Empty;
        }
        var toRemove = new System.Collections.Generic.List<string>();
        foreach (var k in _selectedJobsByClub.Keys)
        {
            if (!present.Contains(k)) toRemove.Add(k);
        }
        foreach (var k in toRemove) _selectedJobsByClub.Remove(k);
        var toRemoveStatus = new System.Collections.Generic.List<string>();
        foreach (var uname in _rowStatus.Keys)
        {
            var key = clubId + "|" + uname;
            if (!present.Contains(key)) toRemoveStatus.Add(uname);
        }
        foreach (var uname in toRemoveStatus) _rowStatus.Remove(uname);
    }

    public void ApplyUserJobUpdate(VenuePlusApp app, string username, string job, string[] jobs)
    {
        var key = app.CurrentClubId + "|" + username;
        if (!_dirtyKeys.Contains(key))
        {
            _selectedJobsByClub[key] = new HashSet<string>(NormalizeJobs(jobs, job), StringComparer.Ordinal);
            for (int i = 0; i < _users.Length; i++)
            {
                if (string.Equals(_users[i].Username, username, System.StringComparison.Ordinal))
                {
                    var jobsArr = NormalizeJobs(jobs, job);
                    _users[i].Jobs = jobsArr;
                    _users[i].Job = GetPrimaryJob(app.GetJobRightsCache(), jobsArr);
                    break;
                }
            }
        }
    }

    public void SetJobOptions(string[] jobs)
    {
        if (jobs != null && jobs.Length > 0) _jobOptions = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Distinct(jobs, System.StringComparer.Ordinal));
    }

    public void CloseInviteInline()
    {
        _inviteInlineOpen = false;
        _manualLinkInlineOpen = false;
        _inviteStatus = string.Empty;
        _inviteUid = string.Empty;
        _manualDisplayName = string.Empty;
        _manualJobsInline.Clear();
        _manualAddStatus = string.Empty;
        _manualLinkManualUid = string.Empty;
        _manualLinkTargetUid = string.Empty;
        _manualLinkStatus = string.Empty;
    }

    private static StaffUser[] ApplyFilter(StaffUser[] users, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return users;
        var f = filter.Trim();
        var list = new System.Collections.Generic.List<StaffUser>(users.Length);
        foreach (var u in users)
        {
            var hitUser = u.Username?.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0;
            var hitJob = u.Job?.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hitJob && u.Jobs != null)
            {
                for (int i = 0; i < u.Jobs.Length; i++)
                {
                    if (u.Jobs[i].IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0) { hitJob = true; break; }
                }
            }
            if (hitUser || hitJob) list.Add(u);
        }
        return list.ToArray();
    }

    private static string[] GetJobsFromUser(StaffUser user)
    {
        if (user.Jobs != null && user.Jobs.Length > 0) return user.Jobs;
        return string.IsNullOrWhiteSpace(user.Job) ? Array.Empty<string>() : new[] { user.Job };
    }

    private static string[] NormalizeJobs(IEnumerable<string> jobs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            if (string.IsNullOrWhiteSpace(job)) continue;
            set.Add(job);
        }
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

    private static string[] NormalizeJobs(HashSet<string> jobs)
    {
        if (jobs.Count == 0) { jobs.Add("Unassigned"); }
        var arr = new string[jobs.Count];
        int idx = 0;
        foreach (var j in jobs)
        {
            arr[idx] = j;
            idx++;
        }
        Array.Sort(arr, StringComparer.Ordinal);
        return arr;
    }

    private static void NormalizeJobSet(HashSet<string> jobs)
    {
        if (jobs.Count == 0) { jobs.Add("Unassigned"); return; }
        if (jobs.Count > 1 && jobs.Contains("Unassigned")) jobs.Remove("Unassigned");
    }

    private static bool HasOwner(string[] jobs)
    {
        for (int i = 0; i < jobs.Length; i++)
        {
            if (string.Equals(jobs[i], "Owner", System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string GetPrimaryJob(Dictionary<string, JobRightsInfo>? rightsMap, string[] jobs)
    {
        if (HasOwner(jobs)) return "Owner";
        string best = "Unassigned";
        int bestRank = 0;
        if (rightsMap == null) return jobs.Length > 0 ? jobs[0] : best;
        for (int i = 0; i < jobs.Length; i++)
        {
            if (!rightsMap.TryGetValue(jobs[i], out var r)) continue;
            if (r.Rank > bestRank)
            {
                bestRank = r.Rank;
                best = jobs[i];
            }
        }
        if (string.Equals(best, "Unassigned", System.StringComparison.Ordinal))
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                if (!string.Equals(jobs[i], "Unassigned", System.StringComparison.Ordinal)) { best = jobs[i]; break; }
            }
        }
        return best;
    }

    private static string FormatJobs(string[] jobs)
    {
        if (jobs.Length == 0) return "Unassigned";
        if (jobs.Length == 1) return jobs[0];
        var total = 0;
        for (int i = 0; i < jobs.Length; i++) total += jobs[i].Length + 2;
        var sb = new System.Text.StringBuilder(total);
        for (int i = 0; i < jobs.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(jobs[i]);
        }
        return sb.ToString();
    }

    private static bool HasManageJobs(Dictionary<string, JobRightsInfo>? rightsMap, string[] jobs)
    {
        if (rightsMap == null) return false;
        for (int i = 0; i < jobs.Length; i++)
        {
            if (rightsMap.TryGetValue(jobs[i], out var r) && r.ManageJobs) return true;
        }
        return false;
    }
}
