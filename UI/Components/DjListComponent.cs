using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenuePlus.Plugin;
using VenuePlus.State;
using VenuePlus.Helpers;

namespace VenuePlus.UI.Components;

public sealed class DjListComponent
{
    private string _filter = string.Empty;
    private string _pendingName = string.Empty;
    private string _pendingTwitch = string.Empty;
    private int _sortCol;
    private bool _sortAsc = true;
    private string _status = string.Empty;
    private bool _openAddForm;
    private string _editingName = string.Empty;
    private int _pageIndex;
    private string _pageFilter = string.Empty;

    public void OpenAddForm()
    {
        _openAddForm = true;
        _status = string.Empty;
    }

    public void CloseAddForm()
    {
        _openAddForm = false;
        _status = string.Empty;
        _pendingName = string.Empty;
        _pendingTwitch = string.Empty;
        _editingName = string.Empty;
    }

    public void Draw(VenuePlusApp app)
    {
        var canAddDjTop = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanAddDj);
        var canRemoveDj = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanRemoveDj);
        ImGui.Spacing();
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##dj_filter", "Search DJs by name or link", ref _filter, 256);
        ImGui.PopItemWidth();
        if (canAddDjTop)
        {
            var styleDj = ImGui.GetStyle();
            var btnW = ImGui.CalcTextSize("Add DJ").X + styleDj.FramePadding.X * 2f;
            var startX = ImGui.GetCursorPosX();
            var rightX = startX + ImGui.GetContentRegionAvail().X - btnW;
            ImGui.SameLine(rightX);
            if (ImGui.Button("Add DJ")) { OpenAddForm(); }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Add a new DJ"); ImGui.EndTooltip(); }
        }
        if (_openAddForm && canAddDjTop)
        {
            var editing = !string.IsNullOrWhiteSpace(_editingName);
            ImGui.Separator();
            ImGui.TextUnformatted(editing ? "Edit DJ" : "Add DJ");
            ImGui.PushItemWidth(220f);
            ImGui.BeginDisabled(editing);
            ImGui.InputTextWithHint("##dj_name", "DJ name", ref _pendingName, 128);
            ImGui.EndDisabled();
            ImGui.InputTextWithHint("##dj_twitch", "Twitch URL or name", ref _pendingTwitch, 256);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_pendingName));
            if (ImGui.Button(editing ? "Save" : "Add"))
            {
                _status = "Submitting...";
                var name = _pendingName.Trim();
                var twitch = (_pendingTwitch ?? string.Empty).Trim();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.AddDjAsync(name, twitch, null, null);
                    _status = ok ? (editing ? "Saved" : "Added") : (app.GetLastServerMessage() ?? "Add failed");
                    if (ok)
                    {
                        _pendingName = string.Empty;
                        _pendingTwitch = string.Empty;
                        _editingName = string.Empty;
                        _openAddForm = false;
                    }
                });
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close"))
            {
                _openAddForm = false;
                _status = string.Empty;
                _editingName = string.Empty;
            }
            if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
            ImGui.Separator();
        }

        ImGui.Separator();
        var list = app.GetDjEntries().ToArray();
        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            list = list.Where(e => e.DjName.Contains(f, StringComparison.OrdinalIgnoreCase)
                                 || (!string.IsNullOrWhiteSpace(e.TwitchLink) && e.TwitchLink.Contains(f, StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        var style = ImGui.GetStyle();
        var copyIcon = IconDraw.ToIconStringFromKey("Copy");
        var openIcon = IconDraw.ToIconStringFromKey("Link");
        var editIcon = IconDraw.ToIconStringFromKey("Edit");
        var rmIcon = IconDraw.ToIconStringFromKey("Trash");
        var canEditDj = canAddDjTop;
        ImGui.PushFont(UiBuilder.IconFont);
        float actionsWidth = 0f;
        int actionsCount = 0;
        void AddActionWidth(string icon)
        {
            actionsWidth += ImGui.CalcTextSize(icon).X + style.FramePadding.X * 2f;
            actionsCount++;
        }
        AddActionWidth(copyIcon);
        AddActionWidth(openIcon);
        if (canEditDj) AddActionWidth(editIcon);
        if (canRemoveDj) AddActionWidth(rmIcon);
        ImGui.PopFont();

        if (!string.Equals(_pageFilter, _filter, StringComparison.Ordinal))
        {
            _pageFilter = _filter;
            _pageIndex = 0;
        }

        if (_sortCol == 0)
        {
            list = _sortAsc ? list.OrderBy(e => e.DjName, StringComparer.Ordinal).ToArray()
                             : list.OrderByDescending(e => e.DjName, StringComparer.Ordinal).ToArray();
        }
        else if (_sortCol == 1)
        {
            list = _sortAsc ? list.OrderBy(e => e.TwitchLink ?? string.Empty, StringComparer.Ordinal).ToArray()
                             : list.OrderByDescending(e => e.TwitchLink ?? string.Empty, StringComparer.Ordinal).ToArray();
        }

        const int pageSize = 15;
        var totalCount = list.Length;
        var totalPages = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        if (_pageIndex >= totalPages) _pageIndex = totalPages - 1;
        if (_pageIndex < 0) _pageIndex = 0;
        ImGui.BeginDisabled(_pageIndex <= 0);
        if (ImGui.Button("Prev##dj_page_prev")) { _pageIndex--; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Page {_pageIndex + 1} / {totalPages}");
        ImGui.SameLine();
        ImGui.BeginDisabled(_pageIndex >= totalPages - 1);
        if (ImGui.Button("Next##dj_page_next")) { _pageIndex++; }
        ImGui.EndDisabled();
        ImGui.Separator();

        var startIndex = _pageIndex * pageSize;
        var pageCount = Math.Min(pageSize, Math.Max(0, totalCount - startIndex));
        var pageItems = new DjEntry[pageCount];
        if (pageCount > 0) Array.Copy(list, startIndex, pageItems, 0, pageCount);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable | ImGuiTableFlags.NoSavedSettings;
        var showActions = actionsCount > 0;
        var widthActions = showActions ? actionsWidth + style.ItemSpacing.X * Math.Max(0, actionsCount - 1) : 0f;
        var availX = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginTable("dj_table", showActions ? 3 : 2, flags, new System.Numerics.Vector2(availX, Math.Max(160f, ImGui.GetContentRegionAvail().Y - 28f))))
        {
            ImGui.TableSetupColumn("DJ Name", ImGuiTableColumnFlags.WidthFixed, 170f);
            ImGui.TableSetupColumn("Twitch");
            if (showActions) ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, widthActions);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("DJ Name", 0, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Twitch", 1, ref _sortCol, ref _sortAsc);
            if (showActions)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted("Actions");
            }

            foreach (var e in pageItems)
            {
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                var baseY = ImGui.GetCursorPosY();
                var textH = ImGui.GetTextLineHeight();
                var textY = baseY + (rowH - textH) / 2f;
                var buttonY = baseY + (rowH - ImGui.GetFrameHeight()) / 2f;
                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(textY);
                ImGui.TextUnformatted(e.DjName);
                ImGui.TableSetColumnIndex(1);
                ImGui.SetCursorPosY(textY);
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(e.TwitchLink) ? "" : e.TwitchLink);
                if (showActions)
                {
                    ImGui.TableSetColumnIndex(2);
                    ImGui.SetCursorPosY(buttonY);
                    bool anyPrinted = false;
                    if (!string.IsNullOrWhiteSpace(e.TwitchLink))
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        var copyClicked = ImGui.Button(copyIcon + $"##copy_{e.DjName}");
                        ImGui.PopFont();
                        if (copyClicked)
                        {
                            var url = e.TwitchLink;
                            if (!string.IsNullOrWhiteSpace(url)) ImGui.SetClipboardText(url);
                        }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Copy link"); ImGui.EndTooltip(); }
                        anyPrinted = true;

                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var openClicked = ImGui.Button(openIcon + $"##open_{e.DjName}");
                        ImGui.PopFont();
                        if (openClicked)
                        {
                            var url = e.TwitchLink;
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                            });
                        }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open Twitch link"); ImGui.EndTooltip(); }
                    }
                    if (canEditDj)
                    {
                        if (anyPrinted) ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var editClicked = ImGui.Button(editIcon + $"##edit_{e.DjName}");
                        ImGui.PopFont();
                        if (editClicked)
                        {
                            _openAddForm = true;
                            _editingName = e.DjName;
                            _pendingName = e.DjName;
                            _pendingTwitch = e.TwitchLink ?? string.Empty;
                            _status = string.Empty;
                        }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Edit DJ"); ImGui.EndTooltip(); }
                        anyPrinted = true;
                    }
                    if (canRemoveDj)
                    {
                        if (anyPrinted) ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        var rmClicked = ImGui.Button(rmIcon + $"##rm_{e.DjName}");
                        ImGui.PopFont();
                        if (rmClicked)
                        {
                            System.Threading.Tasks.Task.Run(async () => { await app.RemoveDjAsync(e.DjName); });
                        }
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Remove DJ"); ImGui.EndTooltip(); }
                    }
                }
            }
            ImGui.EndTable();
        }
    }
}
