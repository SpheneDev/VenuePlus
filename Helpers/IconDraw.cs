using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace VenuePlus.Helpers;

public static class IconDraw
{
    public static void IconText(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.SetWindowFontScale(0.9f);
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    public static FontAwesomeIcon ParseIcon(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return FontAwesomeIcon.User;
        try
        {
            if (System.Enum.TryParse<FontAwesomeIcon>(key, true, out var icon)) return icon;
        }
        catch { }
        return FontAwesomeIcon.User;
    }

    public static string ToIconStringFromKey(string key)
    {
        var icon = ParseIcon(key);
        return icon.ToIconString();
    }

    public static void IconText(FontAwesomeIcon icon, string tooltip)
    {
        IconText(icon);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
    }

    public static void IconText(FontAwesomeIcon icon, float scale, uint color)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var s = scale > 0f ? scale : 0.9f;
        ImGui.SetWindowFontScale(s);
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }
}
