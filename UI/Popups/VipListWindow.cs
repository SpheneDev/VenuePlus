using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Helpers;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class VipListWindow : Window
{
    private readonly VenuePlusApp _app;
    private string _filter = string.Empty;
    private int _sortCol;
    private bool _sortAsc = true;

    public VipListWindow(VenuePlusApp app) : base("VIP List")
    {
        _app = app;
        Size = new Vector2(380f, 420f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(187f, 160f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        if (!_app.HasStaffSession)
        {
            IsOpen = false;
            _filter = string.Empty;
            return;
        }
        ImGui.PushItemWidth(200f);
        ImGui.InputTextWithHint("##vip_list_filter", "Search by name or homeworld", ref _filter, 256);
        ImGui.PopItemWidth();
        ImGui.Separator();

        var items = _app.GetActive().ToArray();
        var f = _filter?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(f))
        {
            items = items.Where(e => e.CharacterName.Contains(f, StringComparison.OrdinalIgnoreCase)
                                   || e.HomeWorld.Contains(f, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        ImGui.BeginChild("vip_list_table_scroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("vip_list_popup_table", 2, flags))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Homeworld");
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Name", 0, ref _sortCol, ref _sortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Homeworld", 1, ref _sortCol, ref _sortAsc);

            Array.Sort(items, (a, b) =>
            {
                int r = 0;
                switch (_sortCol)
                {
                    default:
                    case 0:
                        r = string.Compare(a.CharacterName, b.CharacterName, StringComparison.OrdinalIgnoreCase);
                        break;
                    case 1:
                        r = string.Compare(a.HomeWorld, b.HomeWorld, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                return _sortAsc ? r : -r;
            });

            foreach (var e in items)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(e.CharacterName);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(e.HomeWorld);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }
}
