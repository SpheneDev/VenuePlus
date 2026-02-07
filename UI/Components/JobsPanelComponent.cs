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
    private bool _editEditVipHomeWorld;
    private bool _editManageUsers;
    private bool _editDeleteStaffMember;
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
    private bool _addEditVipHomeWorld;
    private bool _addManageUsers;
    private bool _addDeleteStaffMember;
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
    private static readonly System.Collections.Generic.Dictionary<string, string[]> _roleIconCategoryIcons = BuildRoleIconCategoryIcons();
    private static readonly System.Collections.Generic.Dictionary<string, string> _roleIconCategoryMap = BuildRoleIconCategoryMap();
    private static readonly string[] _roleIconCategories = BuildRoleIconCategories();
    private static readonly string[] _roleIconKeys = BuildRoleIconKeys();
    private string _iconFilter = string.Empty;
    private string _iconCategory = "All";
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
            _addEditVipHomeWorld = false;
            _addManageUsers = false;
            _addDeleteStaffMember = false;
            _addManageJobs = false;
            _addManageVenueSettings = false;
            _addEditVipDuration = false;
            _addAddDj = false;
            _addRemoveDj = false;
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
                var yBase = ImGui.GetCursorPosY();
                var isOwnerJob = string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal);
                var isUnassignedJob = string.Equals(jobLabel, "Unassigned", System.StringComparison.Ordinal);
                int rankEdit = 1;
                if (_rights.TryGetValue(jobLabel, out var infoRankEdit)) rankEdit = infoRankEdit.Rank;
                else if (string.Equals(jobLabel, "Owner", System.StringComparison.Ordinal)) rankEdit = 10;
                else if (string.Equals(jobLabel, "Unassigned", System.StringComparison.Ordinal)) rankEdit = 0;
                var isActorRole = HasJob(app.CurrentStaffJobs, jobLabel);
                var higherRankEditBlocked = !isOwner && !isActorRole && rankEdit > actorRank;
                if (!isUnassignedJob)
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    var centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                    ImGui.SetCursorPosY(centerY);
                    var disableEdit = (isOwnerJob && !(app.IsOwnerCurrentClub || string.Equals(app.CurrentStaffJob, "Owner", System.StringComparison.Ordinal))) || higherRankEditBlocked;
                    ImGui.BeginDisabled(disableEdit);
                    if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString() + $"##edit_{jobLabel}"))
                    {
                        _openEditJob = jobLabel;
                        _editJobNameInput = jobLabel;
                        _editAddVip = GetRight(jobLabel, "addVip");
                        _editRemoveVip = GetRight(jobLabel, "removeVip");
                        _editEditVipHomeWorld = GetRight(jobLabel, "editVipHomeWorld");
                        _editManageUsers = GetRight(jobLabel, "manageUsers");
                        _editDeleteStaffMember = GetRight(jobLabel, "deleteStaffMember");
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
                if (ImGui.BeginTable("add_perm_table", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("VIP Permissions");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted("Staff Permissions");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted("Venue Permissions");
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Checkbox("Add VIP", ref _addAddVip)) { }
                    if (ImGui.Checkbox("Remove VIP", ref _addRemoveVip)) { }
                    if (ImGui.Checkbox("Edit VIP Duration", ref _addEditVipDuration)) { }
                    if (ImGui.Checkbox("Edit VIP Homeworld", ref _addEditVipHomeWorld)) { }
                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Checkbox("Manage Staff", ref _addManageUsers)) { }
                    if (ImGui.Checkbox("Delete Staff Member", ref _addDeleteStaffMember)) { }
                    if (ImGui.Checkbox("Add DJ", ref _addAddDj)) { }
                    if (ImGui.Checkbox("Remove DJ", ref _addRemoveDj)) { }
                    ImGui.TableSetColumnIndex(2);
                    ImGui.BeginDisabled(!isOwner);
                    if (ImGui.Checkbox("Manage Roles", ref _addManageJobs)) { }
                    ImGui.EndDisabled();
                    ImGui.BeginDisabled(!isOwner);
                    if (ImGui.Checkbox("Manage Venue Settings", ref _addManageVenueSettings)) { }
                    ImGui.EndDisabled();
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
                var btnWAddIcon = ImGui.CalcTextSize("Browse").X + ImGui.GetStyle().FramePadding.X * 2f;
                var inputWAddIcon = System.MathF.Max(140f, System.MathF.Min(260f, ImGui.GetContentRegionAvail().X - btnWAddIcon - 8f));
                var iconKeyAdd = _addIconKey ?? string.Empty;
                ImGui.SetNextItemWidth(inputWAddIcon);
                ImGui.InputText("##add_role_icon", ref iconKeyAdd, 64);
                if (ImGui.IsItemDeactivatedAfterEdit()) _addIconKey = iconKeyAdd.Trim();
                ImGui.SameLine();
                if (ImGui.Button("Browse##role_icon_add"))
                {
                    _iconFilter = string.Empty;
                    ImGui.OpenPopup("role_icon_browser_add");
                }
                var maxPopupWAdd = System.MathF.Max(320f, ImGui.GetIO().DisplaySize.X - 80f);
                var maxPopupHAdd = System.MathF.Max(240f, ImGui.GetIO().DisplaySize.Y - 120f);
                var minPopupWAdd = System.MathF.Min(520f, maxPopupWAdd);
                var minPopupHAdd = System.MathF.Min(360f, maxPopupHAdd);
                ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(minPopupWAdd, minPopupHAdd), new System.Numerics.Vector2(maxPopupWAdd, maxPopupHAdd));
                if (ImGui.BeginPopup("role_icon_browser_add"))
                {
                    ImGui.TextUnformatted("Category");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(180f);
                    var iconCategoryAdd = _iconCategory ?? "All";
                    if (ImGui.BeginCombo("##role_icon_category_add", iconCategoryAdd))
                    {
                        for (int c = 0; c < _roleIconCategories.Length; c++)
                        {
                            var category = _roleIconCategories[c];
                            var selected = string.Equals(iconCategoryAdd, category, System.StringComparison.OrdinalIgnoreCase);
                            if (ImGui.Selectable(category, selected)) _iconCategory = category;
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    var filterIcons = _iconFilter ?? string.Empty;
                    ImGui.InputTextWithHint("##role_icon_filter_add", "Search icons", ref filterIcons, 64);
                    _iconFilter = filterIcons ?? string.Empty;
                    ImGui.Separator();
                    var cols = 10;
                    var cell = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                    var childW = System.MathF.Max(240f, System.MathF.Min(ImGui.GetContentRegionAvail().X, maxPopupWAdd - 24f));
                    var childH = System.MathF.Max(200f, System.MathF.Min(ImGui.GetContentRegionAvail().Y, maxPopupHAdd - 120f));
                    if (ImGui.BeginChild("##role_icon_browser_list_add", new System.Numerics.Vector2(childW, childH), false, ImGuiWindowFlags.HorizontalScrollbar))
                    {
                        int shown = 0;
                        for (int k = 0; k < _roleIconKeys.Length; k++)
                        {
                            var key = _roleIconKeys[k];
                            if (!string.Equals(_iconCategory, "All", System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (!_roleIconCategoryMap.TryGetValue(key, out var cat) || !string.Equals(cat, _iconCategory, System.StringComparison.OrdinalIgnoreCase)) continue;
                            }
                            if (!string.IsNullOrWhiteSpace(_iconFilter) && key.IndexOf(_iconFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var iconStrPick = IconDraw.ToIconStringFromKey(key);
                            ImGui.PushFont(UiBuilder.IconFont);
                            var clickedPick = ImGui.Button(iconStrPick + $"##role_icon_pick_add_{k}", new System.Numerics.Vector2(cell, cell));
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.TextUnformatted(key);
                                ImGui.EndTooltip();
                            }
                            if (clickedPick)
                            {
                                _addIconKey = key;
                                ImGui.CloseCurrentPopup();
                            }
                            shown++;
                            if ((shown % cols) != 0) ImGui.SameLine();
                        }
                        ImGui.EndChild();
                    }
                    ImGui.EndPopup();
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
                    var addIconKeySafe = string.IsNullOrWhiteSpace(_addIconKey) ? "User" : _addIconKey;
                    if (name.Length > 0)
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var ok = await app.AddJobAsync(name);
                            if (ok)
                            {
                                await app.UpdateJobRightsAsync(name, _addAddVip, _addRemoveVip, _addEditVipHomeWorld, _addManageUsers, _addDeleteStaffMember, _addManageJobs, _addManageVenueSettings, _addEditVipDuration, _addAddDj, _addRemoveDj, _addEditShiftPlan, _addColorHex, addIconKeySafe, _addRank);
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
            var width = 500f;
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
                ImGui.BeginDisabled(editingOwner || editingUnassigned);
                ImGui.InputText("Role Name", ref _editJobNameInput, 64);
                ImGui.EndDisabled();
                ImGui.PopItemWidth();

                if (editingOwner)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32("#FFC76A"));
                    ImGui.TextUnformatted("Owner has all permissions; permissions are not editable.");
                    ImGui.PopStyleColor();
                }
                if (editingUnassigned)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorUtil.HexToU32("#FFC76A"));
                    ImGui.TextUnformatted("Unassigned is system-defined; permissions are not editable.");
                    ImGui.PopStyleColor();
                }

                ImGui.Separator();
                ImGui.TextUnformatted("Permissions");
                ImGui.BeginDisabled(editingOwner || editingUnassigned);
                if (ImGui.BeginTable("edit_perm_table", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted("VIP Permissions");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted("Staff Permissions");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted("Venue Permissions");
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Checkbox("Add VIP", ref _editAddVip)) { }
                    if (ImGui.Checkbox("Remove VIP", ref _editRemoveVip)) { }
                    if (ImGui.Checkbox("Edit VIP Duration", ref _editEditVipDuration)) { }
                    if (ImGui.Checkbox("Edit VIP Homeworld", ref _editEditVipHomeWorld)) { }
                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Checkbox("Manage Staff", ref _editManageUsers)) { }
                    if (ImGui.Checkbox("Delete Staff Member", ref _editDeleteStaffMember)) { }
                    if (ImGui.Checkbox("Add DJ", ref _editAddDj)) { }
                    if (ImGui.Checkbox("Remove DJ", ref _editRemoveDj)) { }
                    ImGui.TableSetColumnIndex(2);
                    var canToggleManageJobs = isOwner || _editManageJobs;
                    ImGui.BeginDisabled(!canToggleManageJobs);
                    if (ImGui.Checkbox("Manage Roles", ref _editManageJobs)) { }
                    ImGui.EndDisabled();
                    var canToggleManageVenueSettings = isOwner || _editManageVenueSettings;
                    ImGui.BeginDisabled(!canToggleManageVenueSettings);
                    if (ImGui.Checkbox("Manage Venue Settings", ref _editManageVenueSettings)) { }
                    ImGui.EndDisabled();
                    if (ImGui.Checkbox("Edit Shift Plan", ref _editEditShiftPlan)) { }
                    ImGui.EndTable();
                }
                ImGui.EndDisabled();

                ImGui.Separator();
                ImGui.TextUnformatted("Appearance");
                ImGui.BeginDisabled(editingOwner || editingUnassigned);
                var colVec4 = ColorUtil.HexToVec4(_editColorHex);
                var colVec = new System.Numerics.Vector3(colVec4.X, colVec4.Y, colVec4.Z);
                if (ImGui.ColorEdit3("Role Color", ref colVec)) { _editColorHex = ColorUtil.Vec4ToHex(new System.Numerics.Vector4(colVec.X, colVec.Y, colVec.Z, 1f)); }
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                ImGui.TextUnformatted(IconDraw.ToIconStringFromKey(_editIconKey));
                ImGui.SetWindowFontScale(1f);
                ImGui.PopFont();
                var btnWEditIcon = ImGui.CalcTextSize("Browse").X + ImGui.GetStyle().FramePadding.X * 2f;
                var inputWEditIcon = System.MathF.Max(140f, System.MathF.Min(260f, ImGui.GetContentRegionAvail().X - btnWEditIcon - 8f));
                var iconKeyEdit = _editIconKey ?? string.Empty;
                ImGui.SetNextItemWidth(inputWEditIcon);
                ImGui.InputText("##edit_role_icon", ref iconKeyEdit, 64);
                if (ImGui.IsItemDeactivatedAfterEdit()) _editIconKey = iconKeyEdit.Trim();
                ImGui.SameLine();
                if (ImGui.Button("Browse##role_icon_edit"))
                {
                    _iconFilter = string.Empty;
                    ImGui.OpenPopup("role_icon_browser_edit");
                }
                ImGui.EndDisabled();
                var maxPopupWEdit = System.MathF.Max(320f, ImGui.GetIO().DisplaySize.X - 80f);
                var maxPopupHEdit = System.MathF.Max(240f, ImGui.GetIO().DisplaySize.Y - 120f);
                var minPopupWEdit = System.MathF.Min(520f, maxPopupWEdit);
                var minPopupHEdit = System.MathF.Min(360f, maxPopupHEdit);
                ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(minPopupWEdit, minPopupHEdit), new System.Numerics.Vector2(maxPopupWEdit, maxPopupHEdit));
                if (ImGui.BeginPopup("role_icon_browser_edit"))
                {
                    ImGui.TextUnformatted("Category");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(180f);
                    var iconCategoryEdit = _iconCategory ?? "All";
                    if (ImGui.BeginCombo("##role_icon_category_edit", iconCategoryEdit))
                    {
                        for (int c = 0; c < _roleIconCategories.Length; c++)
                        {
                            var category = _roleIconCategories[c];
                            var selected = string.Equals(iconCategoryEdit, category, System.StringComparison.OrdinalIgnoreCase);
                            if (ImGui.Selectable(category, selected)) _iconCategory = category;
                            if (selected) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    var filterIcons = _iconFilter ?? string.Empty;
                    ImGui.InputTextWithHint("##role_icon_filter_edit", "Search icons", ref filterIcons, 64);
                    _iconFilter = filterIcons ?? string.Empty;
                    ImGui.Separator();
                    var cols = 10;
                    var cell = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                    var childW = System.MathF.Max(240f, System.MathF.Min(ImGui.GetContentRegionAvail().X, maxPopupWEdit - 24f));
                    var childH = System.MathF.Max(200f, System.MathF.Min(ImGui.GetContentRegionAvail().Y, maxPopupHEdit - 120f));
                    if (ImGui.BeginChild("##role_icon_browser_list_edit", new System.Numerics.Vector2(childW, childH), false, ImGuiWindowFlags.HorizontalScrollbar))
                    {
                        int shown = 0;
                        for (int k = 0; k < _roleIconKeys.Length; k++)
                        {
                            var key = _roleIconKeys[k];
                            if (!string.Equals(_iconCategory, "All", System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (!_roleIconCategoryMap.TryGetValue(key, out var cat) || !string.Equals(cat, _iconCategory, System.StringComparison.OrdinalIgnoreCase)) continue;
                            }
                            if (!string.IsNullOrWhiteSpace(_iconFilter) && key.IndexOf(_iconFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var iconStrPick = IconDraw.ToIconStringFromKey(key);
                            ImGui.PushFont(UiBuilder.IconFont);
                            var clickedPick = ImGui.Button(iconStrPick + $"##role_icon_pick_edit_{k}", new System.Numerics.Vector2(cell, cell));
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.TextUnformatted(key);
                                ImGui.EndTooltip();
                            }
                            if (clickedPick)
                            {
                                _editIconKey = key;
                                ImGui.CloseCurrentPopup();
                            }
                            shown++;
                            if ((shown % cols) != 0) ImGui.SameLine();
                        }
                        ImGui.EndChild();
                    }
                    ImGui.EndPopup();
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
                var editIconKeySafe = string.IsNullOrWhiteSpace(_editIconKey) ? "User" : _editIconKey;
                var higherRankEditBlockedSave = !isOwner && !isActorRoleEdit && existingRankEdit > actorRank;
                var disableSave = editingUnassigned || higherRankEditBlockedSave || (!isOwner && _editRank > actorRank);
                ImGui.BeginDisabled(disableSave);
                if (ImGui.Button("Save"))
                {
                    var oldName = _openEditJob;
                    var newName = (_editJobNameInput ?? string.Empty).Trim();
                    if (string.Equals(oldName, newName, System.StringComparison.Ordinal))
                    {
                        SetRight(oldName, "addVip", _editAddVip);
                        SetRight(oldName, "removeVip", _editRemoveVip);
                        SetRight(oldName, "editVipHomeWorld", _editEditVipHomeWorld);
                        SetRight(oldName, "manageUsers", _editManageUsers);
                        SetRight(oldName, "deleteStaffMember", _editDeleteStaffMember);
                        SetRight(oldName, "manageJobs", _editManageJobs);
                        SetRight(oldName, "manageVenueSettings", _editManageVenueSettings);
                        SetRight(oldName, "editVipDuration", _editEditVipDuration);
                        SetRight(oldName, "addDj", _editAddDj);
                        SetRight(oldName, "removeDj", _editRemoveDj);
                        SetRight(oldName, "editShiftPlan", _editEditShiftPlan);
                        if (_rights.TryGetValue(oldName, out var infoOld)) { infoOld.ColorHex = _editColorHex; infoOld.IconKey = editIconKeySafe; infoOld.Rank = _editRank; }
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
                                await app.UpdateJobRightsAsync(newName, _editAddVip, _editRemoveVip, _editEditVipHomeWorld, _editManageUsers, _editDeleteStaffMember, _editManageJobs, _editManageVenueSettings, _editEditVipDuration, _editAddDj, _editRemoveDj, _editEditShiftPlan, _editColorHex, editIconKeySafe, rankOld);
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

    private static string[] BuildRoleIconKeys()
    {
        var names = System.Enum.GetNames(typeof(FontAwesomeIcon));
        var map = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Length; i++) map[names[i]] = names[i];
        var list = new System.Collections.Generic.List<string>(64);
        var added = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _roleIconCategoryIcons)
        {
            var icons = entry.Value;
            for (int i = 0; i < icons.Length; i++)
            {
                if (map.TryGetValue(icons[i], out var real) && added.Add(real)) list.Add(real);
            }
        }
        list.Sort(System.StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static System.Collections.Generic.Dictionary<string, string[]> BuildRoleIconCategoryIcons()
    {
        var map = new System.Collections.Generic.Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "People", new[] { "User", "Users", "UserFriends", "UserShield", "UserTie", "HandsHelping", "Handshake" } },
            { "Authority", new[] { "Shield", "Crown", "Star", "Gem", "Medal", "Award", "Trophy" } },
            { "Music", new[] { "Music", "Guitar", "Drum", "DrumSteelpan", "Microphone", "Headphones", "CompactDisc", "RecordVinyl", "TheaterMasks", "Mask" } },
            { "Drinks", new[] { "GlassCheers", "Beer", "WineBottle", "GlassWhiskey", "Cocktail", "GlassMartini", "GlassMartiniAlt", "Coffee", "MugHot" } },
            { "Food", new[] { "Utensils", "PizzaSlice", "Hamburger", "Hotdog", "IceCream", "CandyCane", "CakeCandles", "Cheese", "AppleAlt", "Lemon", "Carrot", "Fish" } },
            { "Mood", new[] { "Heart", "GrinStars", "Smile", "Laugh", "KissWinkHeart" } },
            { "Others", new[] { "Fire", "Snowflake", "Sun", "Moon", "Bolt", "Leaf", "Feather", "Magic" } }
        };
        return map;
    }

    private static string[] BuildRoleIconCategories()
    {
        var list = new System.Collections.Generic.List<string>(_roleIconCategoryIcons.Count + 1);
        list.Add("All");
        var keys = new System.Collections.Generic.List<string>(_roleIconCategoryIcons.Keys);
        keys.Sort(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < keys.Count; i++) list.Add(keys[i]);
        return list.ToArray();
    }

    private static System.Collections.Generic.Dictionary<string, string> BuildRoleIconCategoryMap()
    {
        var names = System.Enum.GetNames(typeof(FontAwesomeIcon));
        var nameMap = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Length; i++) nameMap[names[i]] = names[i];
        var map = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _roleIconCategoryIcons)
        {
            var icons = entry.Value;
            for (int i = 0; i < icons.Length; i++)
            {
                if (nameMap.TryGetValue(icons[i], out var real) && !map.ContainsKey(real)) map[real] = entry.Key;
            }
        }
        return map;
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
                if (!_pending.TryGetValue(keyName, out var set3) || !set3.Contains("editVipHomeWorld")) { _rights[keyName].EditVipHomeWorld = incomingInfo.EditVipHomeWorld; }
                if (!_pending.TryGetValue(keyName, out var set4) || !set4.Contains("manageUsers")) { _rights[keyName].ManageUsers = incomingInfo.ManageUsers; }
                if (!_pending.TryGetValue(keyName, out var set12) || !set12.Contains("deleteStaffMember")) { _rights[keyName].DeleteStaffMember = incomingInfo.DeleteStaffMember; }
                if (!_pending.TryGetValue(keyName, out var set5) || !set5.Contains("manageJobs")) { _rights[keyName].ManageJobs = incomingInfo.ManageJobs; }
                if (!_pending.TryGetValue(keyName, out var set13) || !set13.Contains("manageVenueSettings")) { _rights[keyName].ManageVenueSettings = incomingInfo.ManageVenueSettings; }
                if (!_pending.TryGetValue(keyName, out var set7) || !set7.Contains("editVipDuration")) { _rights[keyName].EditVipDuration = incomingInfo.EditVipDuration; }
                if (!_pending.TryGetValue(keyName, out var set8) || !set8.Contains("addDj")) { _rights[keyName].AddDj = incomingInfo.AddDj; }
                if (!_pending.TryGetValue(keyName, out var set9) || !set9.Contains("editShiftPlan")) { _rights[keyName].EditShiftPlan = incomingInfo.EditShiftPlan; }
                if (!_pending.TryGetValue(keyName, out var set10) || !set10.Contains("removeDj")) { _rights[keyName].RemoveDj = incomingInfo.RemoveDj; }
                if (!_pending.TryGetValue(keyName, out var set14) || !set14.Contains("colorHex")) { _rights[keyName].ColorHex = incomingInfo.ColorHex ?? "#FFFFFF"; }
                if (!_pending.TryGetValue(keyName, out var set15) || !set15.Contains("iconKey")) { _rights[keyName].IconKey = incomingInfo.IconKey ?? "User"; }
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
            "editVipHomeWorld" => info.EditVipHomeWorld,
            "manageUsers" => info.ManageUsers,
            "deleteStaffMember" => info.DeleteStaffMember,
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
        bool editVipHomeWorld = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoHomeWorld) && infoHomeWorld.EditVipHomeWorld);
        bool manageUsers = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoUsers) && infoUsers.ManageUsers);
        bool deleteStaffMember = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoDelete) && infoDelete.DeleteStaffMember);
        bool manageJobs = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoJobs) && infoJobs.ManageJobs);
        bool manageVenue = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoVenue) && infoVenue.ManageVenueSettings);
        bool editVipDuration = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoVip) && infoVip.EditVipDuration);
        bool addDj = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoAddDj) && infoAddDj.AddDj);
        bool removeDj = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoRemDj) && infoRemDj.RemoveDj);
        bool editShiftPlan = isOwner || (!isUnassigned && _rights.TryGetValue(job, out var infoShift) && infoShift.EditShiftPlan);
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(job);
        ImGui.Separator();
        ImGui.TextUnformatted("VIP Permissions");
        DrawPermissionRow("Add VIP", addVip);
        DrawPermissionRow("Remove VIP", removeVip);
        DrawPermissionRow("Edit VIP Duration", editVipDuration);
        DrawPermissionRow("Edit VIP Homeworld", editVipHomeWorld);
        ImGui.Separator();
        ImGui.TextUnformatted("Staff Permissions");
        DrawPermissionRow("Manage Staff", manageUsers);
        DrawPermissionRow("Delete Staff Member", deleteStaffMember);
        DrawPermissionRow("Add DJ", addDj);
        DrawPermissionRow("Remove DJ", removeDj);
        ImGui.Separator();
        ImGui.TextUnformatted("Venue Permissions");
        DrawPermissionRow("Manage Roles", manageJobs);
        DrawPermissionRow("Manage Venue Settings", manageVenue);
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
            case "editVipHomeWorld": info.EditVipHomeWorld = value; break;
            case "manageUsers": info.ManageUsers = value; break;
            case "deleteStaffMember": info.DeleteStaffMember = value; break;
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
        MarkPending(job, "editVipHomeWorld");
        MarkPending(job, "manageUsers");
        MarkPending(job, "deleteStaffMember");
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
        var editHomeWorldVal = GetRight(job, "editVipHomeWorld");
        var muVal = GetRight(job, "manageUsers");
        var deleteStaffVal = GetRight(job, "deleteStaffMember");
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
                    UnmarkPending(job, "editVipHomeWorld");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "deleteStaffMember");
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
                    UnmarkPending(job, "editVipHomeWorld");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "deleteStaffMember");
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
                    UnmarkPending(job, "editVipHomeWorld");
                    UnmarkPending(job, "manageUsers");
                    UnmarkPending(job, "deleteStaffMember");
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
            var ok = await app.UpdateJobRightsAsync(job, addVal, remVal, editHomeWorldVal, muVal, deleteStaffVal, mjVal, mvVal, edVal, addDjVal, remDjVal, editShiftVal, color, icon, rankCur);
            _status = string.Empty;
            UnmarkPending(job, "addVip");
            UnmarkPending(job, "removeVip");
            UnmarkPending(job, "editVipHomeWorld");
            UnmarkPending(job, "manageUsers");
            UnmarkPending(job, "deleteStaffMember");
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
