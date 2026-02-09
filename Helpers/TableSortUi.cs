using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VenuePlus.Helpers;

public static class TableSortUi
{
    public static bool DrawSortableHeader(string label, int columnIndex, ref int sortCol, ref bool sortAsc, float arrowScale = 0.85f)
    {
        var arrow = sortCol == columnIndex ? (sortAsc ? " ▲" : " ▼") : string.Empty;
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var clicked = ImGui.Selectable(label + arrow, false, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (clicked)
        {
            sortAsc = sortCol == columnIndex ? !sortAsc : true;
            sortCol = columnIndex;
        }
        return clicked;
    }

    public static void DrawHeaderTextCentered(string label)
    {
        var size = ImGui.CalcTextSize(label);
        var avail = ImGui.GetContentRegionAvail().X;
        var offset = MathF.Max(0f, (avail - size.X) * 0.5f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.TextUnformatted(label);
    }
}
