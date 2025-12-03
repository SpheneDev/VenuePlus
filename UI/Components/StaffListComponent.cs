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
    private int _sortCol;
    private bool _sortAsc = true;
    private readonly Dictionary<string, string> _selectedJobsByClub = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rowStatus = new();
    private bool _openInviteByUidRequested;
    private bool _inviteModalOpen;
    private string _inviteUid = string.Empty;
    private string _inviteStatus = string.Empty;
    private string[] _jobOptions = new[] { "Unassigned", "Greeter", "Barkeeper", "Dancer", "Escort" };
    private readonly System.Collections.Generic.HashSet<string> _dirtyKeys = new(System.StringComparer.Ordinal);
    private bool _inviteInlineOpen;

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
                    _selectedJobsByClub[key] = u.Job;
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
        ImGui.SameLine();
        var visible = ApplyFilter(_users, _filter);
        var countText = visible.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ImGui.TextUnformatted(countText);
        ImGui.SameLine();
        ImGui.BeginDisabled(!app.IsOwnerCurrentClub);
        if (ImGui.Button("Add"))
        {
            _inviteInlineOpen = true;
            _inviteStatus = string.Empty;
            _inviteUid = string.Empty;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(app.IsOwnerCurrentClub ? "Invite staff by UID" : "Only venue owner can invite staff"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        if (_inviteInlineOpen)
        {
            ImGui.Separator();
            ImGui.InputText("User UID", ref _inviteUid, 64);
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_inviteUid) || !app.IsOwnerCurrentClub);
            if (ImGui.Button("Add"))
            {
                _inviteStatus = "Submitting...";
                var uid = _inviteUid;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.InviteStaffByUidAsync(uid, null);
                    _inviteStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Invite failed");
                    if (ok) _inviteInlineOpen = false;
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) { _inviteInlineOpen = false; _inviteStatus = string.Empty; }
            if (!string.IsNullOrEmpty(_inviteStatus)) { ImGui.TextUnformatted(_inviteStatus); }
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
                        r = string.Compare(a.Job, b.Job, System.StringComparison.OrdinalIgnoreCase);
                        break;
                }
                return _sortAsc ? r : -r;
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
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    var createdStr = u.CreatedAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "--";
                    ImGui.TextUnformatted($"Added: {createdStr}");
                    ImGui.EndTooltip();
                }
                ImGui.TableSetColumnIndex(1);
                var key = app.CurrentClubId + "|" + u.Username;
                if (!_selectedJobsByClub.ContainsKey(key)) _selectedJobsByClub[key] = u.Job;
                var current = _selectedJobsByClub[key];
                var currentName = current;
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
                if (rightsCachePre2 != null && rightsCachePre2.TryGetValue(current, out var infoPre2))
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
                var isOwnerJob = string.Equals(current, "Owner", System.StringComparison.Ordinal);
                ImGui.BeginDisabled(!canAct || isOwnerJob);
                if (ImGui.BeginCombo($"##job_{u.Username}", currentName))
                {
                    if (Array.IndexOf(_jobOptions, current) < 0)
                    {
                        bool selected = true;
                        if (ImGui.Selectable(current, selected)) { _selectedJobsByClub[key] = current; _dirtyKeys.Add(key); }
                    }
                    foreach (var name in _jobOptions)
                    {
                        if (string.Equals(name, "Owner", System.StringComparison.Ordinal)) continue;
                        var rightsCache2 = app.GetJobRightsCache();
                        if (rightsCache2 != null && rightsCache2.TryGetValue(name, out var infoOpt))
                        {
                            var col2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoOpt.ColorHex);
                            var icon2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoOpt.IconKey);
                            IconDraw.IconText(icon2, 0.9f, col2);
                            ImGui.SameLine();
                        }
                        bool selected = current == name;
                        if (ImGui.Selectable(name, selected))
                        {
                            _selectedJobsByClub[key] = name;
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
                        var isOwnerRow = string.Equals(_selectedJobsByClub[key], "Owner", System.StringComparison.Ordinal);
                        var ctrlDown = ImGui.GetIO().KeyCtrl;
                        ImGui.BeginDisabled(!ctrlDown || isOwnerRow);
                        if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + $"##rm_{u.Username}"))
                        {
                            _rowStatus[u.Username] = "Removing...";
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                var ok = await app.DeleteStaffUserAsync(u.Username);
                                _rowStatus[u.Username] = ok ? "Removed" : "Remove failed";
                            });
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(isOwnerRow ? "Owner cannot be removed" : (ctrlDown ? "Remove this staff member" : "Hold Ctrl to remove this staff member")); ImGui.EndTooltip(); }

                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBaseAct + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        ImGui.BeginDisabled(isOwnerRow);
                        if (ImGui.Button(FontAwesomeIcon.Save.ToIconString() + $"##save_{u.Username}"))
                        {
                            var newJob = _selectedJobsByClub[key];
                            _rowStatus[u.Username] = "Saving...";
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                var ok = await app.UpdateStaffUserJobAsync(u.Username, newJob);
                                _rowStatus[u.Username] = ok ? "Saved" : "Save failed";
                                if (ok)
                                {
                                    var k = key;
                                    _dirtyKeys.Remove(k);
                                }
                            });
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(isOwnerRow ? "Owner job cannot be changed" : "Save changes to this staff member's job"); ImGui.EndTooltip(); }
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
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_inviteUid) || !app.IsOwnerCurrentClub);
            if (ImGui.Button("Add"))
            {
                _inviteStatus = "Submitting...";
                var uid = _inviteUid;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.InviteStaffByUidAsync(uid, null);
                    _inviteStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Invite failed");
                    if (ok) _inviteModalOpen = false;
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) { _inviteModalOpen = false; _inviteStatus = string.Empty; }
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
                _selectedJobsByClub[key] = u.Job;
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

    public void ApplyUserJobUpdate(VenuePlusApp app, string username, string job)
    {
        var key = app.CurrentClubId + "|" + username;
        if (!_dirtyKeys.Contains(key))
        {
            _selectedJobsByClub[key] = job;
            for (int i = 0; i < _users.Length; i++)
            {
                if (string.Equals(_users[i].Username, username, System.StringComparison.Ordinal))
                {
                    _users[i].Job = job;
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
        _inviteStatus = string.Empty;
        _inviteUid = string.Empty;
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
            if (hitUser || hitJob) list.Add(u);
        }
        return list.ToArray();
    }
}
