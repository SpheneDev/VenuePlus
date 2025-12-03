using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace VenuePlus.Helpers;

public static class TableSortUi
{
    public static bool DrawSortableHeader(string label, int columnIndex, ref int sortCol, ref bool sortAsc, float arrowScale = 0.85f)
    {
        var clicked = ImGui.Selectable(label, false, ImGuiSelectableFlags.DontClosePopups);
        if (clicked)
        {
            sortAsc = sortCol == columnIndex ? !sortAsc : true;
            sortCol = columnIndex;
        }
        if (sortCol == columnIndex)
        {
            ImGui.SameLine(0f, 4f);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1f));
            ImGui.SetWindowFontScale(arrowScale);
            ImGui.TextUnformatted(sortAsc ? "▲" : "▼");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
        }
        return clicked;
    }
}
