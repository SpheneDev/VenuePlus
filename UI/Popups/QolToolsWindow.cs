using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class QolToolsWindow : Window
{
    private readonly VenuePlusApp _app;

    public QolToolsWindow(VenuePlusApp app) : base("QOL Tools")
    {
        _app = app;
        Size = new Vector2(380f, 320f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280f, 280f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.TextUnformatted(FontAwesomeIcon.Toolbox.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextUnformatted("Tools");
        ImGui.Separator();

        DrawToolItem(FontAwesomeIcon.CommentDots, "Macro Helper", "Compose and send macro-like chat sequences.", () => _app.OpenWhisperWindow());
        ImGui.Spacing();
        DrawToolItem(FontAwesomeIcon.List, "Macro Hotbar", "Manage available hotbars and startup visibility.", () => _app.OpenMacroHotbarManagerWindow());
        ImGui.Spacing();
        ImGui.BeginDisabled(!_app.HasStaffSession);
        DrawToolItem(FontAwesomeIcon.Users, "VIP List", "External VIP list window with sorting and search.", () => _app.OpenVipListWindow());
        ImGui.EndDisabled();
        if (_app.IsServerAdmin)
        {
            ImGui.Spacing();
            DrawToolItem(FontAwesomeIcon.UserShield, "Server Admin", "Server admin operations and diagnostics.", () => _app.OpenAdminPanelWindow());
        }
    }

    private void DrawToolItem(FontAwesomeIcon icon, string title, string description, System.Action onOpen)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.95f);
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.SameLine(0f, 6f);
        ImGui.TextUnformatted(title);
        ImGui.TextWrapped(description);
        var fullW = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button($"Open##{title}", new Vector2(fullW, 0))) onOpen();
        ImGui.Separator();
    }
}
