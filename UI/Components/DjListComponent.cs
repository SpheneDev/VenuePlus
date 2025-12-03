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
    }

    public void Draw(VenuePlusApp app)
    {
        var canManage = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##dj_filter", "Search DJs by name or link", ref _filter, 256);
        ImGui.PopItemWidth();
        if (_openAddForm && canManage)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Add DJ");
            ImGui.PushItemWidth(220f);
            ImGui.InputText("Name", ref _pendingName, 128);
            ImGui.InputText("Twitch URL", ref _pendingTwitch, 256);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_pendingName));
            if (ImGui.Button("Add"))
            {
                _status = "Submitting...";
                var name = _pendingName.Trim();
                var twitch = (_pendingTwitch ?? string.Empty).Trim();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.AddDjAsync(name, twitch);
                    _status = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                    if (ok)
                    {
                        _pendingName = string.Empty;
                        _pendingTwitch = string.Empty;
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
        var rmIcon = IconDraw.ToIconStringFromKey("Trash");
        ImGui.PushFont(UiBuilder.IconFont);
        float actionsWidth = ImGui.CalcTextSize(copyIcon).X + style.FramePadding.X * 2f
                           + ImGui.CalcTextSize(openIcon).X + style.FramePadding.X * 2f
                           + ImGui.CalcTextSize(rmIcon).X + style.FramePadding.X * 2f
                           + style.ItemSpacing.X * 2f;
        ImGui.PopFont();
        int actionsCount = 2;

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable | ImGuiTableFlags.NoSavedSettings;
        var showActions = actionsCount > 0;
        var widthActions = showActions ? actionsWidth + style.ItemSpacing.X * (actionsCount - 1) : 0f;
        var availX = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginTable("dj_table", showActions ? 3 : 2, flags, new System.Numerics.Vector2(availX, Math.Max(160f, ImGui.GetContentRegionAvail().Y - 28f))))
        {
            ImGui.TableSetupColumn("DJ Name");
            ImGui.TableSetupColumn("Twitch");
            if (showActions) ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, widthActions);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            TableSortUi.DrawSortableHeader("DJ Name", 0, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Twitch", 1, ref _sortCol, ref _sortAsc);
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

            foreach (var e in list)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(e.DjName);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(e.TwitchLink) ? "" : e.TwitchLink);
                if (showActions)
                {
                    ImGui.TableSetColumnIndex(2);
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
                    if (canManage)
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
