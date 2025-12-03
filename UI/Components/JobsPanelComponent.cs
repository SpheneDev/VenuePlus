using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenuePlus.Helpers;
using VenuePlus.Plugin;
using VenuePlus.State;

namespace VenuePlus.UI.Components;

public sealed class JobsPanelComponent
{
    private string[] _jobs = System.Array.Empty<string>();
    private System.Collections.Generic.Dictionary<string, JobRightsInfo> _rights = new();
    private string _status = string.Empty;
    private string _newJobName = string.Empty;
    private string _selectedJob = string.Empty;
    private System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> _pending = new();
    private string _filter = string.Empty;
    private string _openEditJob = string.Empty;
    private bool _editWindowOpen;
    private bool _sortAsc = true;
    private int _sortColRoles;
    private string _editJobNameInput = string.Empty;
    private bool _editAddVip;
    private bool _editRemoveVip;
    private bool _editManageUsers;
    private bool _editManageJobs;
    private bool _editEditVipDuration;
    private bool _addWindowOpen;
    private string _newRoleNameInput = string.Empty;
    private bool _addAddVip;
    private bool _addRemoveVip;
    private bool _addManageUsers;
    private bool _addManageJobs;
    private bool _addEditVipDuration;
    private string _addColorHex = "#FFFFFF";
    private string _addIconKey = "User";
    private string _editColorHex = "#FFFFFF";
    private string _editIconKey = "User";
    private readonly string[] _iconOptions = new[] { "User", "Shield", "Star", "Beer", "Music", "GlassCheers", "Heart", "Sun", "Moon", "Crown", "Gem", "Fire", "Snowflake" };

    public void Draw(VenuePlusApp app)
    {
        if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
        ImGui.Spacing();
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##roles_filter", "Search roles", ref _filter, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.SameLine();
        if (ImGui.Button("Add Role"))
        {
            _newRoleNameInput = string.Empty;
            _addAddVip = false;
            _addRemoveVip = false;
            _addManageUsers = false;
            _addManageJobs = false;
            _addWindowOpen = true;
        }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new role"); ImGui.EndTooltip(); }
        ImGui.Separator();
        var jobs = ApplyFilter(_jobs, _filter);
        var style = ImGui.GetStyle();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        float actionsWidth = 0f;
        actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Cog.ToIconString()).X + style.FramePadding.X * 2f;
        actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Trash.ToIconString()).X + style.FramePadding.X * 2f;
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        actionsWidth += style.ItemSpacing.X;
        var tflags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("roles_table", 2, tflags))
        {
            ImGui.TableSetupColumn("Role");
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Role", 0, ref _sortColRoles, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted("Actions");
            System.Array.Sort(jobs, (a, b) =>
            {
                var r = string.Compare(a ?? string.Empty, b ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
                return _sortAsc ? r : -r;
            });
            foreach (var j in jobs)
            {
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                var textH = ImGui.GetTextLineHeight();
                var dy = (rowH - textH) / 2f;
                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                if (_rights.TryGetValue(j, out var info0))
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32(info0.ColorHex));
                    ImGui.SetWindowFontScale(0.9f);
                    ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(info0.IconKey));
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopStyleColor();
                    ImGui.PopFont();
                    ImGui.SameLine();
                }
                ImGui.TextUnformatted(j);
                ImGui.TableSetColumnIndex(1);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                var yBase = ImGui.GetCursorPosY();
                var centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                ImGui.SetCursorPosY(centerY);
                var isOwnerJob = string.Equals(j, "Owner", System.StringComparison.Ordinal);
                var disableEdit = isOwnerJob && !(app.IsOwnerCurrentClub || string.Equals(app.CurrentStaffJob, "Owner", System.StringComparison.Ordinal));
                ImGui.BeginDisabled(disableEdit);
                if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + $"##edit_{j}"))
                {
                    _openEditJob = j;
                    _editJobNameInput = j;
                    _editAddVip = GetRight(j, "addVip");
                    _editRemoveVip = GetRight(j, "removeVip");
                    _editManageUsers = GetRight(j, "manageUsers");
                    _editManageJobs = GetRight(j, "manageJobs");
                    _editEditVipDuration = GetRight(j, "editVipDuration");
                    if (_rights.TryGetValue(j, out var infoInit)) { _editColorHex = infoInit.ColorHex; _editIconKey = infoInit.IconKey; }
                    _editWindowOpen = true;
                }
                ImGui.EndDisabled();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(disableEdit ? (isOwnerJob ? "Only the venue owner can edit Owner style" : "Requires Manage Roles rights or venue owner") : "Edit role rights"); ImGui.EndTooltip(); }

                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                ImGui.SetCursorPosY(centerY);
                var canDelete = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageJobs);
                var disableDelete = isOwnerJob || !canDelete;
                ImGui.BeginDisabled(disableDelete);
                if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + $"##del_{j}"))
                {
                    var name = j;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.DeleteJobAsync(name);
                        if (ok)
                        {
                            var list = new System.Collections.Generic.List<string>(_jobs.Length);
                            for (int i = 0; i < _jobs.Length; i++) { if (!string.Equals(_jobs[i], name, System.StringComparison.Ordinal)) list.Add(_jobs[i]); }
                            _jobs = list.ToArray();
                            if (_selectedJob == name) _selectedJob = _jobs.Length > 0 ? _jobs[0] : string.Empty;
                        }
                    });
                }
                ImGui.EndDisabled();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(disableDelete ? (isOwnerJob ? "Owner role cannot be deleted" : "Requires Manage Roles rights or venue owner") : "Delete role"); ImGui.EndTooltip(); }
            }
            ImGui.EndTable();
        }

        if (_addWindowOpen)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(380f, 0f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Add Role", ref _addWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushItemWidth(240f);
                ImGui.InputText("Role Name", ref _newRoleNameInput, 64);
                ImGui.PopItemWidth();
                ImGui.Separator();
                if (ImGui.Checkbox("Add VIP", ref _addAddVip)) { }
                ImGui.SameLine();
                if (ImGui.Checkbox("Remove VIP", ref _addRemoveVip)) { }
                if (ImGui.Checkbox("Manage Users", ref _addManageUsers)) { }
                ImGui.SameLine();
                if (ImGui.Checkbox("Manage Roles", ref _addManageJobs)) { }
                if (ImGui.Checkbox("Edit VIP Duration", ref _addEditVipDuration)) { }
                var colVecAdd4 = ColorUtil.HexToVec4(_addColorHex);
                var colVecAdd = new System.Numerics.Vector3(colVecAdd4.X, colVecAdd4.Y, colVecAdd4.Z);
                if (ImGui.ColorEdit3("Role Color", ref colVecAdd)) { _addColorHex = ColorUtil.Vec4ToHex(new System.Numerics.Vector4(colVecAdd.X, colVecAdd.Y, colVecAdd.Z, 1f)); }
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(_addIconKey));
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                if (ImGui.BeginCombo("Role Icon", _addIconKey))
                {
                    foreach (var opt in _iconOptions)
                    {
                        bool sel = string.Equals(opt, _addIconKey, System.StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(opt, sel)) _addIconKey = opt;
                    }
                    ImGui.EndCombo();
                }
                ImGui.Separator();
                if (ImGui.Button("Save"))
                {
                    var name = (_newRoleNameInput ?? string.Empty).Trim();
                    if (name.Length > 0)
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await app.AddJobAsync(name);
                            if (ok)
                            {
                                await app.UpdateJobRightsAsync(name, _addAddVip, _addRemoveVip, _addManageUsers, _addManageJobs, _addEditVipDuration, _addColorHex, _addIconKey);
                                _selectedJob = name;
                            }
                        });
                    }
                    _addWindowOpen = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create role with selected rights"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _addWindowOpen = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close without saving"); ImGui.EndTooltip(); }
            }
            ImGui.End();
        }

        if (_editWindowOpen)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(360f, 0f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Edit Role Rights", ref _editWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushItemWidth(240f);
                var editingOwner = string.Equals(_openEditJob, "Owner", System.StringComparison.Ordinal);
                ImGui.BeginDisabled(editingOwner);
                ImGui.InputText("Role Name", ref _editJobNameInput, 64);
                ImGui.EndDisabled();
                ImGui.PopItemWidth();
                ImGui.Separator();
                if (editingOwner)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32("#FFC76A"));
                    ImGui.TextUnformatted("Owner has all permissions; permissions are not editable.");
                    ImGui.PopStyleColor();
                }
                ImGui.BeginDisabled(editingOwner);
                if (ImGui.Checkbox("Add VIP", ref _editAddVip)) { }
                ImGui.SameLine();
                if (ImGui.Checkbox("Remove VIP", ref _editRemoveVip)) { }
                if (ImGui.Checkbox("Manage Users", ref _editManageUsers)) { }
                ImGui.SameLine();
                if (ImGui.Checkbox("Manage Roles", ref _editManageJobs)) { }
                if (ImGui.Checkbox("Edit VIP Duration", ref _editEditVipDuration)) { }
                ImGui.EndDisabled();
                var colVec4 = ColorUtil.HexToVec4(_editColorHex);
                var colVec = new System.Numerics.Vector3(colVec4.X, colVec4.Y, colVec4.Z);
                if (ImGui.ColorEdit3("Role Color", ref colVec)) { _editColorHex = ColorUtil.Vec4ToHex(new System.Numerics.Vector4(colVec.X, colVec.Y, colVec.Z, 1f)); }
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(_editIconKey));
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                if (ImGui.BeginCombo("Role Icon", _editIconKey))
                {
                    foreach (var opt in _iconOptions)
                    {
                        bool sel = string.Equals(opt, _editIconKey, System.StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(opt, sel)) _editIconKey = opt;
                    }
                    ImGui.EndCombo();
                }
                ImGui.Separator();
                if (ImGui.Button("Save"))
                {
                    var oldName = _openEditJob;
                    var newName = (_editJobNameInput ?? string.Empty).Trim();
                    if (string.Equals(oldName, newName, System.StringComparison.Ordinal))
                    {
                        SetRight(oldName, "addVip", _editAddVip);
                        SetRight(oldName, "removeVip", _editRemoveVip);
                        SetRight(oldName, "manageUsers", _editManageUsers);
                        SetRight(oldName, "manageJobs", _editManageJobs);
                        SetRight(oldName, "editVipDuration", _editEditVipDuration);
                        if (_rights.TryGetValue(oldName, out var infoOld)) { infoOld.ColorHex = _editColorHex; infoOld.IconKey = _editIconKey; }
                        SaveImmediate(app, oldName);
                        _editWindowOpen = false;
                    }
                    else
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var okAdd = await app.AddJobAsync(newName);
                            if (okAdd)
                            {
                                await app.UpdateJobRightsAsync(newName, _editAddVip, _editRemoveVip, _editManageUsers, _editManageJobs, _editEditVipDuration, _editColorHex, _editIconKey);
                                var users = await app.ListStaffUsersDetailedAsync();
                                if (users != null)
                                {
                                    foreach (var u in users)
                                    {
                                        if (string.Equals(u.Job, oldName, System.StringComparison.Ordinal))
                                        {
                                            await app.UpdateStaffUserJobAsync(u.Username, newName);
                                        }
                                    }
                                }
                                await app.DeleteJobAsync(oldName);
                                _openEditJob = newName;
                                _editJobNameInput = newName;
                                _selectedJob = newName;
                            }
                        });
                        _editWindowOpen = false;
                    }
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Apply changes to this role"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _editWindowOpen = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close without saving"); ImGui.EndTooltip(); }
            }
            ImGui.End();
        }
    }

    

    public void SetJobs(string[] jobs)
    {
        _jobs = jobs != null ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Distinct(jobs, System.StringComparer.Ordinal)) : System.Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(_selectedJob) && _jobs.Length > 0) _selectedJob = _jobs[0];
        _status = string.Empty;
    }

    public void SetRights(System.Collections.Generic.Dictionary<string, JobRightsInfo>? rights)
    {
        var incoming = rights ?? new System.Collections.Generic.Dictionary<string, JobRightsInfo>();
        foreach (var kv in incoming)
        {
            if (!_rights.ContainsKey(kv.Key)) _rights[kv.Key] = new JobRightsInfo();
            var incomingInfo = kv.Value;
            if (incomingInfo != null)
            {
                if (!_pending.TryGetValue(kv.Key, out var set) || !set.Contains("addVip")) { _rights[kv.Key].AddVip = incomingInfo.AddVip; }
                if (!_pending.TryGetValue(kv.Key, out var set2) || !set2.Contains("removeVip")) { _rights[kv.Key].RemoveVip = incomingInfo.RemoveVip; }
                if (!_pending.TryGetValue(kv.Key, out var set3) || !set3.Contains("manageUsers")) { _rights[kv.Key].ManageUsers = incomingInfo.ManageUsers; }
                if (!_pending.TryGetValue(kv.Key, out var set4) || !set4.Contains("manageJobs")) { _rights[kv.Key].ManageJobs = incomingInfo.ManageJobs; }
                if (!_pending.TryGetValue(kv.Key, out var set7) || !set7.Contains("editVipDuration")) { _rights[kv.Key].EditVipDuration = incomingInfo.EditVipDuration; }
                if (!_pending.TryGetValue(kv.Key, out var set5) || !set5.Contains("colorHex")) { _rights[kv.Key].ColorHex = incomingInfo.ColorHex ?? "#FFFFFF"; }
                if (!_pending.TryGetValue(kv.Key, out var set6) || !set6.Contains("iconKey")) { _rights[kv.Key].IconKey = incomingInfo.IconKey ?? "User"; }
            }
        }
    }

    private bool GetRight(string job, string key)
    {
        if (!_rights.TryGetValue(job, out var info)) return false;
        return key switch
        {
            "addVip" => info.AddVip,
            "removeVip" => info.RemoveVip,
            "manageUsers" => info.ManageUsers,
            "manageJobs" => info.ManageJobs,
            "editVipDuration" => info.EditVipDuration,
            _ => false
        };
    }

    private void SetRight(string job, string key, bool value)
    {
        if (!_rights.TryGetValue(job, out var info)) { info = new JobRightsInfo(); _rights[job] = info; }
        switch (key)
        {
            case "addVip": info.AddVip = value; break;
            case "removeVip": info.RemoveVip = value; break;
            case "manageUsers": info.ManageUsers = value; break;
            case "manageJobs": info.ManageJobs = value; break;
            case "editVipDuration": info.EditVipDuration = value; break;
        }
    }

    private void SaveImmediate(VenuePlusApp app, string job)
    {
        MarkPending(job, "addVip");
        MarkPending(job, "removeVip");
        MarkPending(job, "manageUsers");
        MarkPending(job, "manageJobs");
        MarkPending(job, "editVipDuration");
        MarkPending(job, "colorHex");
        MarkPending(job, "iconKey");
        _status = string.Empty;
        var addVal = GetRight(job, "addVip");
        var remVal = GetRight(job, "removeVip");
        var muVal = GetRight(job, "manageUsers");
        var mjVal = GetRight(job, "manageJobs");
        var edVal = GetRight(job, "editVipDuration");
        System.Threading.Tasks.Task.Run(async () =>
        {
            var color = (_rights.TryGetValue(job, out var info) ? info.ColorHex : "#FFFFFF");
            var icon = (_rights.TryGetValue(job, out var info2) ? info2.IconKey : "User");
            var ok = await app.UpdateJobRightsAsync(job, addVal, remVal, muVal, mjVal, edVal, color, icon);
            _status = string.Empty;
            UnmarkPending(job, "addVip");
            UnmarkPending(job, "removeVip");
            UnmarkPending(job, "manageUsers");
            UnmarkPending(job, "manageJobs");
            UnmarkPending(job, "editVipDuration");
            UnmarkPending(job, "colorHex");
            UnmarkPending(job, "iconKey");
            
        });
    }

    private static string[] ApplyFilter(string[] jobs, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return jobs;
        var f = filter.Trim();
        var list = new System.Collections.Generic.List<string>(jobs.Length);
        foreach (var j in jobs)
        {
            if ((j ?? string.Empty).IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                list.Add(j ?? string.Empty);
            }
        }
        return list.ToArray();
    }

    private void MarkPending(string job, string key)
    {
        if (!_pending.TryGetValue(job, out var set)) { set = new System.Collections.Generic.HashSet<string>(); _pending[job] = set; }
        set.Add(key);
    }

    private void UnmarkPending(string job, string key)
    {
        if (_pending.TryGetValue(job, out var set)) { set.Remove(key); if (set.Count == 0) _pending.Remove(job); }
    }
}
