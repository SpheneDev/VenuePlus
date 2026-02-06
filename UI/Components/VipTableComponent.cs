using System;
using System.Globalization;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenuePlus.Helpers;
using VenuePlus.Plugin;
using VenuePlus.State;

namespace VenuePlus.UI.Components;

public sealed class VipTableComponent
{
    private int _sortCol;
    private bool _sortAsc = true;
    private bool _openAddForm;
    private string _pendingName = string.Empty;
    private string _pendingWorld = string.Empty;
    private VenuePlus.State.VipDuration _pendingDuration = VenuePlus.State.VipDuration.FourWeeks;
    private string _addStatus = string.Empty;
    private string? _editKey;
    private string _editHomeWorld = string.Empty;
    private int _pageIndex;
    private string _pageFilter = string.Empty;

    public void OpenAddForm()
    {
        _pendingName = string.Empty;
        _pendingWorld = string.Empty;
        _pendingDuration = VenuePlus.State.VipDuration.FourWeeks;
        _addStatus = string.Empty;
        _openAddForm = true;
    }

    public void CloseAddForm()
    {
        _openAddForm = false;
        _pendingName = string.Empty;
        _pendingWorld = string.Empty;
        _addStatus = string.Empty;
    }
    public void Draw(VenuePlusApp app, string filter)
    {
        var canAddVipGlobal = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanAddVip);
        if (!string.Equals(_pageFilter, filter, StringComparison.Ordinal))
        {
            _pageFilter = filter ?? string.Empty;
            _pageIndex = 0;
        }
        if (_openAddForm && canAddVipGlobal)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Add VIP");
            ImGui.PushItemWidth(280f);
            ImGui.InputText("Character Name", ref _pendingName, 128);
            ImGui.SameLine(); IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle, "Enter the exact in-game character name for the VIP");
            ImGui.InputText("Homeworld", ref _pendingWorld, 64);
            ImGui.SameLine(); IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle, "Optional. Leave empty to use 'Unknown'.");
            ImGui.PopItemWidth();
            ImGui.Separator();
            ImGui.TextUnformatted("VIP Duration:");
            bool d4 = _pendingDuration == VenuePlus.State.VipDuration.FourWeeks;
            bool d12 = _pendingDuration == VenuePlus.State.VipDuration.TwelveWeeks;
            bool dLife = _pendingDuration == VenuePlus.State.VipDuration.Lifetime;
            if (ImGui.RadioButton("1 Month", d4)) _pendingDuration = VenuePlus.State.VipDuration.FourWeeks;
            ImGui.SameLine();
            if (ImGui.RadioButton("3 Months", d12)) _pendingDuration = VenuePlus.State.VipDuration.TwelveWeeks;
            ImGui.SameLine();
            if (ImGui.RadioButton("Unlimited", dLife)) _pendingDuration = VenuePlus.State.VipDuration.Lifetime;
            ImGui.SameLine(); IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.Infinity);
            ImGui.Separator();
            ImGui.BeginDisabled(!canAddVipGlobal || string.IsNullOrWhiteSpace(_pendingName));
            if (ImGui.Button("Add"))
            {
                var world = string.IsNullOrWhiteSpace(_pendingWorld) ? "Unknown" : _pendingWorld.Trim();
                app.AddVip(_pendingName.Trim(), world, _pendingDuration);
                _openAddForm = false;
                _addStatus = string.Empty;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) { _openAddForm = false; _addStatus = string.Empty; }
            if (!string.IsNullOrEmpty(_addStatus)) { ImGui.TextUnformatted(_addStatus); }
            ImGui.Separator();
        }
        var items = app.GetActive().ToArray();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            items = items.Where(e => e.CharacterName.Contains(f, StringComparison.OrdinalIgnoreCase)
                                   || e.HomeWorld.Contains(f, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        const int pageSize = 15;
        var totalCount = items.Length;
        var totalPages = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        if (_pageIndex >= totalPages) _pageIndex = totalPages - 1;
        if (_pageIndex < 0) _pageIndex = 0;
        ImGui.BeginDisabled(_pageIndex <= 0);
        if (ImGui.Button("Prev##vip_page_prev")) { _pageIndex--; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Page {_pageIndex + 1} / {totalPages}");
        ImGui.SameLine();
        ImGui.BeginDisabled(_pageIndex >= totalPages - 1);
        if (ImGui.Button("Next##vip_page_next")) { _pageIndex++; }
        ImGui.EndDisabled();
        ImGui.Separator();

        System.Array.Sort(items, (a, b) =>
        {
            int r = 0;
            switch (_sortCol)
            {
                case 0:
                    r = string.Compare(a.CharacterName, b.CharacterName, System.StringComparison.OrdinalIgnoreCase);
                    break;
                case 1:
                    r = string.Compare(a.HomeWorld, b.HomeWorld, System.StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    {
                        double va = a.Duration == VipDuration.Lifetime ? double.MaxValue : (a.ExpiresAt.HasValue ? (a.ExpiresAt.Value.ToUniversalTime() - DateTimeOffset.UtcNow).TotalMinutes : 0.0);
                        double vb = b.Duration == VipDuration.Lifetime ? double.MaxValue : (b.ExpiresAt.HasValue ? (b.ExpiresAt.Value.ToUniversalTime() - DateTimeOffset.UtcNow).TotalMinutes : 0.0);
                        r = va.CompareTo(vb);
                    }
                    break;
                case 3:
                    r = a.Duration.CompareTo(b.Duration);
                    break;
            }
            return _sortAsc ? r : -r;
        });

        var startIndex = _pageIndex * pageSize;
        var endIndex = Math.Min(items.Length, startIndex + pageSize);
        var editKeyInPage = false;
        if (!string.IsNullOrWhiteSpace(_editKey))
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                if (string.Equals(items[i].Key, _editKey, StringComparison.Ordinal))
                {
                    editKeyInPage = true;
                    break;
                }
            }
        }

        var style = ImGui.GetStyle();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        float actionsWidth = 0f;
        int actionsCount = 0;
        var canRemoveGlobal = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanRemoveVip);
        var canSetDurationGlobal = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditVipDuration);
        var canEditHomeWorldGlobal = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditVipHomeWorld);
        if (canRemoveGlobal)
        {
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Trash.ToIconString()).X + style.FramePadding.X * 2f;
            actionsCount++;
        }
        if (canSetDurationGlobal)
        {
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Calendar.ToIconString()).X + style.FramePadding.X * 2f;
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.CalendarAlt.ToIconString()).X + style.FramePadding.X * 2f;
            actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Infinity.ToIconString()).X + style.FramePadding.X * 2f;
            actionsCount += 3;
        }
        if (canEditHomeWorldGlobal)
        {
            if (editKeyInPage)
            {
                actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Save.ToIconString()).X + style.FramePadding.X * 2f;
                actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X + style.FramePadding.X * 2f;
                actionsCount += 2;
            }
            else
            {
                actionsWidth += ImGui.CalcTextSize(FontAwesomeIcon.Edit.ToIconString()).X + style.FramePadding.X * 2f;
                actionsCount++;
            }
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        if (actionsCount > 1) actionsWidth += style.ItemSpacing.X * (actionsCount - 1);
        var showActions = canRemoveGlobal || canSetDurationGlobal || canEditHomeWorldGlobal;
        if (showActions && actionsWidth <= 0f) actionsWidth = ImGui.GetFrameHeight() * 1.5f;

        var sampleRem = "999d 23h";
        float remainingWidth = ImGui.CalcTextSize(sampleRem).X + style.FramePadding.X * 2f;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("vip_table", showActions ? 5 : 4, tableFlags))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 170f);
            ImGui.TableSetupColumn("Homeworld", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("Remaining", ImGuiTableColumnFlags.WidthFixed, remainingWidth);
            ImGui.TableSetupColumn("Duration");
            if (showActions)
            {
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            }
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Name", 0, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Homeworld", 1, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(2);
            TableSortUi.DrawSortableHeader("Remaining", 2, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(3);
            TableSortUi.DrawSortableHeader("Duration", 3, ref _sortCol, ref _sortAsc);
            if (showActions)
            {
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted("Actions");
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                var e = items[i];
                ImGui.TableNextRow();
                var rowH = ImGui.GetFrameHeight();
                var textH = ImGui.GetTextLineHeight();
                var dy = (rowH - textH) / 2f;

                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                ImGui.TextUnformatted(e.CharacterName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    var createdStr = e.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                    var expiresStr = e.Duration == VipDuration.Lifetime ? "--" : (e.ExpiresAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "--");
                    ImGui.TextUnformatted($"Created: {createdStr}");
                    ImGui.TextUnformatted($"Expires: {expiresStr}");
                    ImGui.EndTooltip();
                }
                ImGui.TableSetColumnIndex(1);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                var isEditing = _editKey != null && string.Equals(_editKey, e.Key, StringComparison.Ordinal);
                if (isEditing)
                {
                    ImGui.PushItemWidth(-1f);
                    ImGui.InputText($"##edit_world_{e.Key}", ref _editHomeWorld, 64);
                    ImGui.PopItemWidth();
                }
                else
                {
                    ImGui.TextUnformatted(e.HomeWorld);
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                if (e.Duration == VipDuration.Lifetime)
                {
                    ImGui.TextUnformatted("Lifetime");
                }
                else if (e.ExpiresAt.HasValue)
                {
                    var rem = e.ExpiresAt.Value.ToUniversalTime() - DateTimeOffset.UtcNow;
                    if (rem <= TimeSpan.Zero)
                    {
                        ImGui.TextUnformatted("Expired");
                    }
                    else
                    {
                        var days = (int)Math.Floor(rem.TotalDays);
                        var hours = rem.Hours;
                        var mins = rem.Minutes;
                        var txt = days > 0 ? $"{days}d {hours}h" : $"{hours}h {mins}m";
                        ImGui.TextUnformatted(txt);
                    }
                }
                else
                {
                    ImGui.TextUnformatted("--");
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
                if (e.Duration == VipDuration.Lifetime)
                {
                    IconDraw.IconText(FontAwesomeIcon.Infinity);
                }
                else
                {
                    var label = e.Duration == VipDuration.FourWeeks ? "1 month" : "3 months";
                    ImGui.TextUnformatted(label);
                }
                if (showActions)
                {
                    ImGui.TableSetColumnIndex(4);
                    var hasRemove = canRemoveGlobal;
                    var hasAdd = canSetDurationGlobal;
                    var hasEditHome = canEditHomeWorldGlobal;
                    var homeWorld = e.HomeWorld ?? string.Empty;
                    ImGui.BeginGroup();
                    bool any = false;
                    var yBase = ImGui.GetCursorPosY();
                    float centerY;
                    if (hasRemove)
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        var ctrlDown = ImGui.GetIO().KeyCtrl;
                        ImGui.BeginDisabled(!ctrlDown);
                        if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + $"##rm_{e.Key}"))
                        {
                            app.RemoveVip(e.CharacterName, homeWorld);
                        }
                        ImGui.EndDisabled();
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(ctrlDown ? "Remove this VIP from the list" : "Hold Ctrl to remove this VIP"); ImGui.EndTooltip(); }
                        any = true;
                    }
                    if (hasEditHome)
                    {
                        if (any) ImGui.SameLine();
                        if (!isEditing)
                        {
                            ImGui.PushFont(UiBuilder.IconFont);
                            ImGui.SetWindowFontScale(0.9f);
                            centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                            ImGui.SetCursorPosY(centerY);
                            if (ImGui.Button(FontAwesomeIcon.Edit.ToIconString() + $"##edit_{e.Key}"))
                            {
                                _editKey = e.Key;
                                _editHomeWorld = homeWorld;
                            }
                            ImGui.SetWindowFontScale(1f);
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Edit homeworld"); ImGui.EndTooltip(); }
                            any = true;
                        }
                        else
                        {
                            var canSave = !string.IsNullOrWhiteSpace(_editHomeWorld);
                            ImGui.BeginDisabled(!canSave);
                            ImGui.PushFont(UiBuilder.IconFont);
                            ImGui.SetWindowFontScale(0.9f);
                            centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                            ImGui.SetCursorPosY(centerY);
                            if (ImGui.Button(FontAwesomeIcon.Save.ToIconString() + $"##save_{e.Key}"))
                            {
                                var ok = app.UpdateVipHomeWorld(e.CharacterName, homeWorld, _editHomeWorld.Trim());
                                if (ok) { _editKey = null; _editHomeWorld = string.Empty; }
                            }
                            ImGui.SetWindowFontScale(1f);
                            ImGui.PopFont();
                            ImGui.EndDisabled();
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(canSave ? "Save homeworld changes" : "Enter a homeworld"); ImGui.EndTooltip(); }
                            any = true;
                            if (any) ImGui.SameLine();
                            ImGui.PushFont(UiBuilder.IconFont);
                            ImGui.SetWindowFontScale(0.9f);
                            centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                            ImGui.SetCursorPosY(centerY);
                            if (ImGui.Button(FontAwesomeIcon.Times.ToIconString() + $"##cancel_{e.Key}"))
                            {
                                _editKey = null;
                                _editHomeWorld = string.Empty;
                            }
                            ImGui.SetWindowFontScale(1f);
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Cancel edit"); ImGui.EndTooltip(); }
                        }
                    }
                    if (hasAdd)
                    {
                        if (any) ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        if (ImGui.Button(FontAwesomeIcon.Calendar.ToIconString() + $"##4w_{e.Key}"))
                        {
                            app.AddVip(e.CharacterName, homeWorld, VipDuration.FourWeeks);
                        }
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Set VIP duration to 1 month"); ImGui.EndTooltip(); }
                        any = true;

                        if (any) ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        if (ImGui.Button(FontAwesomeIcon.CalendarAlt.ToIconString() + $"##12w_{e.Key}"))
                        {
                            app.AddVip(e.CharacterName, homeWorld, VipDuration.TwelveWeeks);
                        }
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Set VIP duration to 3 months"); ImGui.EndTooltip(); }
                        any = true;

                        if (any) ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.SetWindowFontScale(0.9f);
                        centerY = yBase + (rowH - ImGui.GetFrameHeight()) / 2f;
                        ImGui.SetCursorPosY(centerY);
                        if (ImGui.Button(FontAwesomeIcon.Infinity.ToIconString() + $"##life_{e.Key}"))
                        {
                            app.AddVip(e.CharacterName, homeWorld, VipDuration.Lifetime);
                        }
                        ImGui.SetWindowFontScale(1f);
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Set VIP duration to Unlimited"); ImGui.EndTooltip(); }
                    }
                    ImGui.EndGroup();
                }
            }
            ImGui.EndTable();
        }
    }
}
