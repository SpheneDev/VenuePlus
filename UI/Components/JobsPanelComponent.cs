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
    private bool _sortAsc = false;
    private int _sortColRoles;
    private string _editJobNameInput = string.Empty;
    private bool _editAddVip;
    private bool _editRemoveVip;
    private bool _editManageUsers;
    private bool _editManageJobs;
    private bool _editManageVenueSettings;
    private bool _editEditVipDuration;
    private bool _editAddDj;
    private bool _editRemoveDj;
    private bool _editEditShiftPlan;
    private bool _addWindowOpen;
    private string _newRoleNameInput = string.Empty;
    private bool _addAddVip;
    private bool _addRemoveVip;
    private bool _addManageUsers;
    private bool _addManageJobs;
    private bool _addManageVenueSettings;
    private bool _addEditVipDuration;
    private bool _addAddDj;
    private bool _addRemoveDj;
    private bool _addEditShiftPlan;
    private string _addColorHex = "#FFFFFF";
    private string _addIconKey = "User";
    private string _editColorHex = "#FFFFFF";
    private string _editIconKey = "User";
    private int _addRank = 1;
    private int _editRank = 1;
    private readonly string[] _iconOptions = new[] { "User", "Shield", "Star", "Beer", "Music", "GlassCheers", "Heart", "Sun", "Moon", "Crown", "Gem", "Fire", "Snowflake" };
    private int _pageIndex;
    private string _pageFilter = string.Empty;

    public void ResetStatusMessages()
    {
        _status = string.Empty;
    }

    public void Draw(VenuePlusApp app)
    {
        if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
        ImGui.Spacing();
        var isOwner = app.IsOwnerCurrentClub;
        var actorRank = GetBestRank(app.GetJobRightsCache(), app.CurrentStaffJobs, isOwner);
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##roles_filter", "Search roles", ref _filter, 128);
        ImGui.PopItemWidth();
        var styleRoles = ImGui.GetStyle();
        var btnWRoles = ImGui.CalcTextSize("Add Role").X + styleRoles.FramePadding.X * 2f;
        var startXRoles = ImGui.GetCursorPosX();
        var rightXRoles = startXRoles + ImGui.GetContentRegionAvail().X - btnWRoles;
        ImGui.SameLine(rightXRoles);
        if (ImGui.Button("Add Role"))
        {
            _newRoleNameInput = string.Empty;
            _addAddVip = false;
            _addRemoveVip = false;
            _addManageUsers = false;
            _addManageJobs = false;
            _addManageVenueSettings = false;
            _addEditShiftPlan = false;
            _addRank = 1;
            _addWindowOpen = true;
        }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new role"); ImGui.EndTooltip(); }
        ImGui.Separator();
        if (!string.Equals(_pageFilter, _filter, System.StringComparison.Ordinal))
        {
            _pageFilter = _filter;
            _pageIndex = 0;
        }
        var jobs = ApplyFilter(_jobs, _filter);
        const int pageSize = 15;
        var totalCount = jobs.Length;
        var totalPages = System.Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        if (_pageIndex >= totalPages) _pageIndex = totalPages - 1;
        if (_pageIndex < 0) _pageIndex = 0;
        ImGui.BeginDisabled(_pageIndex <= 0);
        if (ImGui.Button("Prev##roles_page_prev")) { _pageIndex--; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Page {_pageIndex + 1} / {totalPages}");
        ImGui.SameLine();
        ImGui.BeginDisabled(_pageIndex >= totalPages - 1);
        if (ImGui.Button("Next##roles_page_next")) { _pageIndex++; }
        ImGui.EndDisabled();
        ImGui.Separator();
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
        if (ImGui.BeginTable("roles_table", 3, tflags))
        {
            ImGui.TableSetupColumn("Role");
            ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Role", 0, ref _sortColRoles, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Rank", 1, ref _sortColRoles, ref _sortAsc);
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted("Actions");
            System.Array.Sort(jobs, (a, b) =>
            {
                int ra = 1;
                int rb = 1;
                if (!string.IsNullOrWhiteSpace(a))
                {
                    if (_rights.TryGetValue(a, out var ia)) ra = ia.Rank;
                    else if (string.Equals(a, "Owner", System.StringComparison.Ordinal)) ra = 10;
                    else if (string.Equals(a, "Unassigned", System.StringComparison.Ordinal)) ra = 0;
                }
                if (!string.IsNullOrWhiteSpace(b))
                {
                    if (_rights.TryGetValue(b, out var ib)) rb = ib.Rank;
                    else if (string.Equals(b, "Owner", System.StringComparison.Ordinal)) rb = 10;
                    else if (string.Equals(b, "Unassigned", System.StringComparison.Ordinal)) rb = 0;
                }
                int cmp = ra.CompareTo(rb);
                if (!_sortAsc) cmp = -cmp;
                if (cmp != 0) return cmp;
                return string.Compare(a ?? string.Empty, b ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
            });
            var startIndex = _pageIndex * pageSize;
            var endIndex = System.Math.Min(jobs.Length, startIndex + pageSize);
            for (int i = startIndex; i < endIndex; i++)
            {
                var j = jobs[i];
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                var textH = ImGui.GetTextLineHeight();
                var dy = (rowH - textH) / 2f;
                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                var jobLabel = j ?? string.Empty;
                if (_rights.TryGetValue(jobLabel, out var info0))
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
                ImGui.TextUnformatted(jobLabel);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) DrawRightsTooltip(jobLabel);
                ImGui.TableSetColumnIndex(1);
                int rankDisplay = 1;
                if (_rights.TryGetValue(jobLabel, out var infoRank)) rankDisplay = infoRank.Rank;
                else if (string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal)) rankDisplay = 10;
                else if (string.Equals(jobLabel, "Unassigned", System.StringComparison.Ordinal)) rankDisplay = 0;
                ImGui.TextUnformatted(rankDisplay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ImGui.TableSetColumnIndex(2);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                var yBase = ImGui.GetCursorPosY();
                var centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                ImGui.SetCursorPosY(centerY);
                var isOwnerJob = string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal);
                int rankEdit = 1;
                if (_rights.TryGetValue(jobLabel, out var infoRankEdit)) rankEdit = infoRankEdit.Rank;
                else if (string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal)) rankEdit = 10;
                else if (string.Equals(jobLabel, "Unassigned", System.StringComparison.Ordinal)) rankEdit = 0;
                var isActorRole = HasJob(app.CurrentStaffJobs, jobLabel);
                var higherRankEditBlocked = !isOwner && !isActorRole && rankEdit > actorRank;
                var disableEdit = (isOwnerJob && !(app.IsOwnerCurrentClub || string.Equals(app.CurrentStaffJob, "Owner", System.StringComparison.Ordinal))) || higherRankEditBlocked;
                ImGui.BeginDisabled(disableEdit);
                if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + $"##edit_{jobLabel}"))
                {
                    _openEditJob = jobLabel;
                    _editJobNameInput = jobLabel;
                    _editAddVip = GetRight(jobLabel, "addVip");
                    _editRemoveVip = GetRight(jobLabel, "removeVip");
                    _editManageUsers = GetRight(jobLabel, "manageUsers");
                    _editManageJobs = GetRight(jobLabel, "manageJobs");
                    _editManageVenueSettings = GetRight(jobLabel, "manageVenueSettings");
                    _editEditVipDuration = GetRight(jobLabel, "editVipDuration");
                    _editAddDj = GetRight(jobLabel, "addDj");
                    _editRemoveDj = GetRight(jobLabel, "removeDj");
                    _editEditShiftPlan = GetRight(jobLabel, "editShiftPlan");
                    if (_rights.TryGetValue(jobLabel, out var infoInit))
                    {
                        _editColorHex = infoInit.ColorHex;
                        _editIconKey = infoInit.IconKey;
                        var rIn = infoInit.Rank;
                        int r;
                        if (string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal)) r = 10;
                        else if (string.Equals(jobLabel, "Unassigned", System.StringComparison.Ordinal)) r = 0;
                        else r = rIn <= 0 ? 1 : (rIn > 9 ? 9 : rIn);
                        _editRank = r;
                    }
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
                if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + $"##del_{jobLabel}"))
                {
                    var name = jobLabel;
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
            var mainPos = ImGui.GetWindowPos();
            var mainSize = ImGui.GetWindowSize();
            var styleAdd = ImGui.GetStyle();
            var widthAdd = 420f;
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(widthAdd, 0f), ImGuiCond.Always);
            var offsetX = styleAdd.WindowPadding.X + styleAdd.ItemSpacing.X;
            var anchorXAdd = mainPos.X + mainSize.X + offsetX;
            var anchorYAdd = mainPos.Y + styleAdd.WindowPadding.Y;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(anchorXAdd, anchorYAdd), ImGuiCond.Always);
            if (ImGui.Begin("Add Role", ref _addWindowOpen, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32(_addColorHex));
                ImGui.SetWindowFontScale(1f);
                ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(_addIconKey));
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextUnformatted("Add Role");
                ImGui.Separator();

                ImGui.PushItemWidth(240f);
                ImGui.InputText("Role Name", ref _newRoleNameInput, 64);
                ImGui.PopItemWidth();

                ImGui.Separator();
                ImGui.TextUnformatted("Permissions");
                if (ImGui.BeginTable("add_perm_table", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Checkbox("Add VIP", ref _addAddVip)) { }
                    if (ImGui.Checkbox("Remove VIP", ref _addRemoveVip)) { }
                    if (ImGui.Checkbox("Add DJ", ref _addAddDj)) { }
                    if (ImGui.Checkbox("Remove DJ", ref _addRemoveDj)) { }
                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Checkbox("Manage Staff", ref _addManageUsers)) { }
                    ImGui.BeginDisabled(!isOwner);
                    if (ImGui.Checkbox("Manage Roles", ref _addManageJobs)) { }
                    ImGui.EndDisabled();
                    ImGui.BeginDisabled(!isOwner);
                    if (ImGui.Checkbox("Manage Venue Settings", ref _addManageVenueSettings)) { }
                    ImGui.EndDisabled();
                    if (ImGui.Checkbox("Edit VIP Duration", ref _addEditVipDuration)) { }
                    if (ImGui.Checkbox("Edit Shift Plan", ref _addEditShiftPlan)) { }
                    ImGui.EndTable();
                }

                ImGui.Separator();
                ImGui.TextUnformatted("Rank");
                ImGui.PushItemWidth(120f);
                ImGui.InputInt("Role Rank", ref _addRank);
                if (_addRank < 1) _addRank = 1;
                var maxRankAdd = 9;
                if (!isOwner)
                {
                    maxRankAdd = actorRank <= 0 ? 1 : (actorRank > 9 ? 9 : actorRank);
                }
                if (_addRank > maxRankAdd) _addRank = maxRankAdd;
                ImGui.PopItemWidth();

                ImGui.Separator();
                ImGui.TextUnformatted("Appearance");
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
                var sAdd = ImGui.GetStyle();
                var saveWAdd = ImGui.CalcTextSize("Save").X + sAdd.FramePadding.X * 2f;
                var cancelWAdd = ImGui.CalcTextSize("Cancel").X + sAdd.FramePadding.X * 2f;
                var totalWAdd = saveWAdd + cancelWAdd + sAdd.ItemSpacing.X;
                var startXAdd = ImGui.GetCursorPosX();
                var rightXAdd = startXAdd + ImGui.GetContentRegionAvail().X - totalWAdd;
                ImGui.SetCursorPosX(rightXAdd);
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
                                await app.UpdateJobRightsAsync(name, _addAddVip, _addRemoveVip, _addManageUsers, _addManageJobs, _addManageVenueSettings, _addEditVipDuration, _addAddDj, _addRemoveDj, _addEditShiftPlan, _addColorHex, _addIconKey, _addRank);
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
            var mainPos = ImGui.GetWindowPos();
            var mainSize = ImGui.GetWindowSize();
            var styleEdit = ImGui.GetStyle();
            var width = 420f;
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, 0f), ImGuiCond.Always);
            var offsetXEdit = styleEdit.WindowPadding.X + styleEdit.ItemSpacing.X;
            var anchorX = mainPos.X + mainSize.X + offsetXEdit;
            var anchorY = mainPos.Y + styleEdit.WindowPadding.Y;
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(anchorX, anchorY), ImGuiCond.Always);
            if (ImGui.Begin("Edit Role Rights", ref _editWindowOpen, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32(_editColorHex));
                ImGui.SetWindowFontScale(1f);
                ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(_editIconKey));
                ImGui.PopStyleColor();
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(_openEditJob) ? "Role" : _openEditJob);
                ImGui.Separator();

                ImGui.PushItemWidth(240f);
                var editingOwner = string.Equals(_openEditJob, "Owner", System.StringComparison.Ordinal);
                var editingUnassigned = string.Equals(_openEditJob, "Unassigned", System.StringComparison.Ordinal);
                ImGui.BeginDisabled(editingOwner);
                ImGui.InputText("Role Name", ref _editJobNameInput, 64);
                ImGui.EndDisabled();
                ImGui.PopItemWidth();

                if (editingOwner)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32("#FFC76A"));
                    ImGui.TextUnformatted("Owner has all permissions; permissions are not editable.");
                    ImGui.PopStyleColor();
                }

                ImGui.Separator();
                ImGui.TextUnformatted("Permissions");
                ImGui.BeginDisabled(editingOwner);
                if (ImGui.BeginTable("edit_perm_table", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Checkbox("Add VIP", ref _editAddVip)) { }
                    if (ImGui.Checkbox("Remove VIP", ref _editRemoveVip)) { }
                    if (ImGui.Checkbox("Add DJ", ref _editAddDj)) { }
                    if (ImGui.Checkbox("Remove DJ", ref _editRemoveDj)) { }
                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Checkbox("Manage Staff", ref _editManageUsers)) { }
                    var canToggleManageJobs = isOwner || _editManageJobs;
                    ImGui.BeginDisabled(!canToggleManageJobs);
                    if (ImGui.Checkbox("Manage Roles", ref _editManageJobs)) { }
                    ImGui.EndDisabled();
                    var canToggleManageVenueSettings = isOwner || _editManageVenueSettings;
                    ImGui.BeginDisabled(!canToggleManageVenueSettings);
                    if (ImGui.Checkbox("Manage Venue Settings", ref _editManageVenueSettings)) { }
                    ImGui.EndDisabled();
                    if (ImGui.Checkbox("Edit VIP Duration", ref _editEditVipDuration)) { }
                    if (ImGui.Checkbox("Edit Shift Plan", ref _editEditShiftPlan)) { }
                    ImGui.EndTable();
                }
                ImGui.EndDisabled();

                ImGui.Separator();
                ImGui.TextUnformatted("Appearance");
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
                ImGui.TextUnformatted("Rank");
                var disableRank = editingOwner || editingUnassigned;
                ImGui.BeginDisabled(disableRank);
                ImGui.PushItemWidth(120f);
                ImGui.InputInt("Role Rank", ref _editRank);
                if (!disableRank)
                {
                    if (_editRank < 1) _editRank = 1;
                    var maxRankEdit = 9;
                    if (!isOwner)
                    {
                        maxRankEdit = actorRank <= 0 ? 1 : (actorRank > 9 ? 9 : actorRank);
                    }
                    if (_editRank > maxRankEdit) _editRank = maxRankEdit;
                }
                ImGui.PopItemWidth();
                ImGui.EndDisabled();

                ImGui.Separator();
                var s = ImGui.GetStyle();
                var saveW = ImGui.CalcTextSize("Save").X + s.FramePadding.X * 2f;
                var cancelW = ImGui.CalcTextSize("Cancel").X + s.FramePadding.X * 2f;
                var totalW = saveW + cancelW + s.ItemSpacing.X;
                var startX = ImGui.GetCursorPosX();
                var rightX = startX + ImGui.GetContentRegionAvail().X - totalW;
                ImGui.SetCursorPosX(rightX);
                var isActorRoleEdit = HasJob(app.CurrentStaffJobs, _openEditJob);
                int existingRankEdit = _editRank;
                if (_rights.TryGetValue(_openEditJob, out var infoRankCur)) existingRankEdit = infoRankCur.Rank;
                else if (string.Equals(_openEditJob, "Owner", System.StringComparison.Ordinal)) existingRankEdit = 10;
                else if (string.Equals(_openEditJob, "Unassigned", System.StringComparison.Ordinal)) existingRankEdit = 0;
                var higherRankEditBlockedSave = !isOwner && !isActorRoleEdit && existingRankEdit > actorRank;
                var disableSave = higherRankEditBlockedSave || (!isOwner && _editRank > actorRank);
                ImGui.BeginDisabled(disableSave);
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
                        SetRight(oldName, "manageVenueSettings", _editManageVenueSettings);
                        SetRight(oldName, "editVipDuration", _editEditVipDuration);
                        SetRight(oldName, "addDj", _editAddDj);
                        SetRight(oldName, "removeDj", _editRemoveDj);
                        SetRight(oldName, "editShiftPlan", _editEditShiftPlan);
                        if (_rights.TryGetValue(oldName, out var infoOld)) { infoOld.ColorHex = _editColorHex; infoOld.IconKey = _editIconKey; infoOld.Rank = _editRank; }
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
                                var rankOld = (_rights.TryGetValue(oldName, out var infoOldR) ? infoOldR.Rank : 1);
                                await app.UpdateJobRightsAsync(newName, _editAddVip, _editRemoveVip, _editManageUsers, _editManageJobs, _editManageVenueSettings, _editEditVipDuration, _editAddDj, _editRemoveDj, _editEditShiftPlan, _editColorHex, _editIconKey, rankOld);
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
                ImGui.EndDisabled();
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
            var keyName = kv.Key ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keyName)) continue;
            if (!_rights.ContainsKey(keyName)) _rights[keyName] = new JobRightsInfo();
            var incomingInfo = kv.Value;
            if (incomingInfo != null)
            {
                if (!_pending.TryGetValue(keyName, out var set) || !set.Contains("addVip")) { _rights[keyName].AddVip = incomingInfo.AddVip; }
                if (!_pending.TryGetValue(keyName, out var set2) || !set2.Contains("removeVip")) { _rights[keyName].RemoveVip = incomingInfo.RemoveVip; }
                if (!_pending.TryGetValue(keyName, out var set3) || !set3.Contains("manageUsers")) { _rights[keyName].ManageUsers = incomingInfo.ManageUsers; }
                if (!_pending.TryGetValue(keyName, out var set4) || !set4.Contains("manageJobs")) { _rights[keyName].ManageJobs = incomingInfo.ManageJobs; }
                if (!_pending.TryGetValue(keyName, out var set11) || !set11.Contains("manageVenueSettings")) { _rights[keyName].ManageVenueSettings = incomingInfo.ManageVenueSettings; }
                if (!_pending.TryGetValue(keyName, out var set7) || !set7.Contains("editVipDuration")) { _rights[keyName].EditVipDuration = incomingInfo.EditVipDuration; }
                if (!_pending.TryGetValue(keyName, out var set8) || !set8.Contains("addDj")) { _rights[keyName].AddDj = incomingInfo.AddDj; }
                if (!_pending.TryGetValue(keyName, out var set9) || !set9.Contains("editShiftPlan")) { _rights[keyName].EditShiftPlan = incomingInfo.EditShiftPlan; }
                if (!_pending.TryGetValue(keyName, out var set10) || !set10.Contains("removeDj")) { _rights[keyName].RemoveDj = incomingInfo.RemoveDj; }
                if (!_pending.TryGetValue(keyName, out var set5) || !set5.Contains("colorHex")) { _rights[keyName].ColorHex = incomingInfo.ColorHex ?? "#FFFFFF"; }
                if (!_pending.TryGetValue(keyName, out var set6) || !set6.Contains("iconKey")) { _rights[keyName].IconKey = incomingInfo.IconKey ?? "User"; }
                if (!_pending.TryGetValue(keyName, out var setR) || !setR.Contains("rank"))
                {
                    var name = keyName;
                    var rIn = incomingInfo.Rank;
                    int r;
                    if (string.Equals(name, "Owner", System.StringComparison.Ordinal)) r = 10;
                    else if (string.Equals(name, "Unassigned", System.StringComparison.Ordinal)) r = 0;
                    else r = rIn <= 0 ? 1 : (rIn > 9 ? 9 : rIn);
                    _rights[name].Rank = r;
                }
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
            "manageVenueSettings" => info.ManageVenueSettings,
            "editVipDuration" => info.EditVipDuration,
            "addDj" => info.AddDj,
            "removeDj" => info.RemoveDj,
            "editShiftPlan" => info.EditShiftPlan,
            _ => false
        };
    }

    private void DrawRightsTooltip(string job)
    {
        if (string.IsNullOrWhiteSpace(job)) return;
        var isOwner = string.Equals(job, "Owner", System.StringComparison.Ordinal);
        var isUnassigned = string.Equals(job, "Unassigned", System.StringComparison.Ordinal);
        if (!isOwner && !isUnassigned && !_rights.TryGetValue(job, out var info))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(job);
            ImGui.Separator();
            ImGui.TextUnformatted("No rights available");
            ImGui.EndTooltip();
            return;
        }
        bool addVip = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoAdd) && infoAdd.AddVip);
        bool removeVip = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoRemove) && infoRemove.RemoveVip);
        bool manageUsers = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoUsers) && infoUsers.ManageUsers);
        bool manageJobs = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoJobs) && infoJobs.ManageJobs);
        bool manageVenue = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoVenue) && infoVenue.ManageVenueSettings);
        bool editVipDuration = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoVip) && infoVip.EditVipDuration);
        bool addDj = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoAddDj) && infoAddDj.AddDj);
        bool removeDj = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoRemDj) && infoRemDj.RemoveDj);
        bool editShiftPlan = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoShift) && infoShift.EditShiftPlan);
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(job);
        ImGui.Separator();
        ImGui.TextUnformatted("Permissions");
        DrawPermissionRow("Add VIP", addVip);
        DrawPermissionRow("Remove VIP", removeVip);
        DrawPermissionRow("Manage Staff", manageUsers);
        DrawPermissionRow("Manage Roles", manageJobs);
        DrawPermissionRow("Manage Venue Settings", manageVenue);
        DrawPermissionRow("Edit VIP Duration", editVipDuration);
        DrawPermissionRow("Add DJ", addDj);
        DrawPermissionRow("Remove DJ", removeDj);
        DrawPermissionRow("Edit Shift Plan", editShiftPlan);
        ImGui.EndTooltip();
    }

    private static void DrawPermissionRow(string label, bool enabled)
    {
        var color = enabled ? new System.Numerics.Vector4(0.2f, 0.85f, 0.2f, 1f) : new System.Numerics.Vector4(0.9f, 0.25f, 0.25f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        var icon = enabled ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
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
            case "manageVenueSettings": info.ManageVenueSettings = value; break;
            case "editVipDuration": info.EditVipDuration = value; break;
            case "addDj": info.AddDj = value; break;
            case "removeDj": info.RemoveDj = value; break;
            case "editShiftPlan": info.EditShiftPlan = value; break;
        }
    }

    private void SaveImmediate(VenuePlusApp app, string job)
    {
        MarkPending(job, "addVip");
        MarkPending(job, "removeVip");
        MarkPending(job, "manageUsers");
        MarkPending(job, "manageJobs");
        MarkPending(job, "manageVenueSettings");
        MarkPending(job, "editVipDuration");
        MarkPending(job, "addDj");
        MarkPending(job, "removeDj");
        MarkPending(job, "editShiftPlan");
        MarkPending(job, "colorHex");
        MarkPending(job, "iconKey");
        MarkPending(job, "rank");
        _status = string.Empty;
        var isOwner = app.IsOwnerCurrentClub;
        var actorRank = GetBestRank(app.GetJobRightsCache(), app.CurrentStaffJobs, isOwner);
        var isActorRole = HasJob(app.CurrentStaffJobs, job);
        var addVal = GetRight(job, "addVip");
        var remVal = GetRight(job, "removeVip");
        var muVal = GetRight(job, "manageUsers");
        var mjVal = GetRight(job, "manageJobs");
        var mvVal = GetRight(job, "manageVenueSettings");
        var edVal = GetRight(job, "editVipDuration");
        var addDjVal = GetRight(job, "addDj");
        var remDjVal = GetRight(job, "removeDj");
        var editShiftVal = GetRight(job, "editShiftPlan");
        System.Threading.Tasks.Task.Run(async () =>
        {
            var color = (_rights.TryGetValue(job, out var info) ? info.ColorHex : "#FFFFFF");
            var icon = (_rights.TryGetValue(job, out var info2) ? info2.IconKey : "User");
            var rankCur = (_rights.TryGetValue(job, out var infoR) ? infoR.Rank : 1);
            if (!isOwner)
            {
                var maxRank = actorRank <= 0 ? 1 : (actorRank > 9 ? 9 : actorRank);
                if (rankCur > maxRank) rankCur = maxRank;
                if (!isActorRole && _rights.TryGetValue(job, out var infoExisting) && infoExisting.Rank > actorRank)
                {
                    _status = "Cannot modify higher-rank role";
                    UnmarkPending(job, "addVip");
                    UnmarkPending(job, "removeVip");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "manageJobs");
                    UnmarkPending(job, "manageVenueSettings");
                    UnmarkPending(job, "editVipDuration");
                    UnmarkPending(job, "addDj");
                    UnmarkPending(job, "removeDj");
                    UnmarkPending(job, "editShiftPlan");
                    UnmarkPending(job, "colorHex");
                    UnmarkPending(job, "iconKey");
                    UnmarkPending(job, "rank");
                    return;
                }
                if (mjVal && (!(_rights.TryGetValue(job, out var infoExisting2) && infoExisting2.ManageJobs)))
                {
                    _status = "Cannot grant Manage Roles";
                    UnmarkPending(job, "addVip");
                    UnmarkPending(job, "removeVip");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "manageJobs");
                    UnmarkPending(job, "manageVenueSettings");
                    UnmarkPending(job, "editVipDuration");
                    UnmarkPending(job, "addDj");
                    UnmarkPending(job, "removeDj");
                    UnmarkPending(job, "editShiftPlan");
                    UnmarkPending(job, "colorHex");
                    UnmarkPending(job, "iconKey");
                    UnmarkPending(job, "rank");
                    return;
                }
                if (mvVal && (!(_rights.TryGetValue(job, out var infoExisting3) && infoExisting3.ManageVenueSettings)))
                {
                    _status = "Cannot grant Manage Venue Settings";
                    UnmarkPending(job, "addVip");
                    UnmarkPending(job, "removeVip");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "manageJobs");
                    UnmarkPending(job, "manageVenueSettings");
                    UnmarkPending(job, "editVipDuration");
                    UnmarkPending(job, "addDj");
                    UnmarkPending(job, "removeDj");
                    UnmarkPending(job, "editShiftPlan");
                    UnmarkPending(job, "colorHex");
                    UnmarkPending(job, "iconKey");
                    UnmarkPending(job, "rank");
                    return;
                }
            }
            var ok = await app.UpdateJobRightsAsync(job, addVal, remVal, muVal, mjVal, mvVal, edVal, addDjVal, remDjVal, editShiftVal, color, icon, rankCur);
            _status = string.Empty;
            UnmarkPending(job, "addVip");
            UnmarkPending(job, "removeVip");
            UnmarkPending(job, "manageUsers");
            UnmarkPending(job, "manageJobs");
            UnmarkPending(job, "manageVenueSettings");
            UnmarkPending(job, "editVipDuration");
            UnmarkPending(job, "addDj");
            UnmarkPending(job, "removeDj");
            UnmarkPending(job, "editShiftPlan");
            UnmarkPending(job, "colorHex");
            UnmarkPending(job, "iconKey");
            UnmarkPending(job, "rank");
            
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

    private static int GetBestRank(System.Collections.Generic.Dictionary<string, JobRightsInfo>? rightsMap, string[] jobs, bool isOwner)
    {
        if (isOwner) return 10;
        if (rightsMap == null || jobs == null || jobs.Length == 0) return 0;
        int best = 0;
        for (int i = 0; i < jobs.Length; i++)
        {
            var job = jobs[i] ?? string.Empty;
            if (string.Equals(job, "Owner", System.StringComparison.Ordinal)) { best = 10; break; }
            if (string.Equals(job, "Unassigned", System.StringComparison.Ordinal)) { if (best < 0) best = 0; continue; }
            if (rightsMap.TryGetValue(job, out var info))
            {
                var r = info.Rank;
                if (r > best) best = r;
            }
        }
        return best;
    }

    private static bool HasJob(string[] jobs, string jobName)
    {
        if (jobs == null || jobs.Length == 0) return false;
        for (int i = 0; i < jobs.Length; i++)
        {
            if (string.Equals(jobs[i], jobName, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
