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

    public void TriggerRefresh(VenuePlusApp app)
    {
        _status = string.Empty;
        System.Threading.Tasks.Task.Run(async () =>
        {
            var list = await app.ListStaffUsersDetailedAsync();
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
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##staff_filter", "Search by username or job", ref _filter, 128);
        ImGui.PopItemWidth();
        var visible = ApplyFilter(_users, _filter);
        var canInvite = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
        if (!canInvite)
        {
            _inviteInlineOpen = false;
            _manualLinkInlineOpen = false;
            _focusManualLinkTarget = false;
        }
        if (canInvite)
        {
            ImGui.SameLine();
            var styleStaff = ImGui.GetStyle();
            var btnWStaff = ImGui.CalcTextSize("Add Staff").X + styleStaff.FramePadding.X * 2f;
            var startXStaff = ImGui.GetCursorPosX();
            var rightXStaff = startXStaff + ImGui.GetContentRegionAvail().X - btnWStaff;
            ImGui.SameLine(rightXStaff);
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
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_manualDisplayName));
            if (ImGui.Button("Create##manual_add_inline"))
            {
                _manualAddStatus = "Submitting...";
                var name = _manualDisplayName;
                var jobsInline = NormalizeJobs(_manualJobsInline);
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.CreateManualStaffEntryAsync(name, jobsInline);
                    _manualAddStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                    if (ok)
                    {
                        _manualDisplayName = string.Empty;
                        _manualJobsInline.Clear();
                        _manualJobsInline.Add("Unassigned");
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
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Link.ToIconString()).X + style.FramePadding.X * 2f; actionsCount++;
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (actionsCount > 1) actionsWidth += style.ItemSpacing.X * (actionsCount - 1);
        var showActions = actionsCount > 0;
        if (showActions && actionsWidth <= 0f) actionsWidth = ImGui.GetFrameHeight() * 1.5f;

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("staff_table", showActions ? 3 : 2, flags))
        {
            ImGui.TableSetupColumn("Username");
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
            foreach (var u in visible)
            {
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                var textH = ImGui.GetTextLineHeight();
                var dy = (rowH - textH) / 2f;
                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                ImGui.TextUnformatted(u.Username);
                var showTooltip = ImGui.IsItemHovered();
                if (u.IsManual)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("Manual");
                    showTooltip = showTooltip || ImGui.IsItemHovered();
                }
                if (showTooltip)
                {
                    ImGui.BeginTooltip();
                    var createdStr = u.CreatedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "--";
                    ImGui.TextUnformatted($"Added: {createdStr}");
                    if (!string.IsNullOrWhiteSpace(u.Uid)) ImGui.TextUnformatted($"UID: {u.Uid}");
                    if (u.IsManual) ImGui.TextUnformatted("Manual Entry");
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
            var currentName = FormatJobs(currentDisplayArr);
                ImGui.PushItemWidth(160f);
                var yBase = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(yBase + (rowH - ImGui.GetFrameHeight()) / 2f);
                var fontSize = ImGui.GetFontSize();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                var iconH = ImGui.GetFrameHeight();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                var padY = iconH > fontSize ? (iconH - fontSize) / 2f : ImGui.GetStyle().FramePadding.Y;
                var origPadX = ImGui.GetStyle().FramePadding.X;
                float padX = origPadX;
                var rightsCachePre2 = app.GetJobRightsCache();
                string iconPreviewText = string.Empty;
                uint iconPreviewColor = 0u;
                float iconW = 0f;
            if (rightsCachePre2 != null && rightsCachePre2.TryGetValue(currentPrimary, out var infoPre2))
                {
                    iconPreviewText = VenuePlus.Helpers.IconDraw.ToIconStringFromKey(infoPre2.IconKey);
                    iconPreviewColor = VenuePlus.Helpers.ColorUtil.HexToU32(infoPre2.ColorHex);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    iconW = ImGui.CalcTextSize(iconPreviewText).X;
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                    padX = origPadX + iconW + ImGui.GetStyle().ItemSpacing.X;
                }
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(padX, padY));
            var isOwnerJob = HasOwner(currentArr);
                ImGui.BeginDisabled(!canAct || (isOwnerJob && !app.IsOwnerCurrentClub));
                if (ImGui.BeginCombo($"##job_{u.Username}", currentName))
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
                var rectMin = ImGui.GetItemRectMin();
                var rectMax = ImGui.GetItemRectMax();
                ImGui.PopStyleVar();
                if (!string.IsNullOrEmpty(iconPreviewText))
                {
                    var draw = ImGui.GetWindowDrawList();
                    var itemH = rectMax.Y - rectMin.Y;
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    var iconSizeY = ImGui.CalcTextSize(iconPreviewText).Y;
                    var yIcon = rectMin.Y + (itemH - iconSizeY) / 2f;
                    var xIcon = rectMin.X + origPadX;
                    draw.AddText(new System.Numerics.Vector2(xIcon, yIcon), iconPreviewColor, iconPreviewText);
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                }
                ImGui.PopItemWidth();
                ImGui.SameLine();
                var centerYIcon = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                ImGui.SetCursorPosY(centerYIcon);
                IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle);
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
