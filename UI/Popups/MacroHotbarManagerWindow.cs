using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class MacroHotbarManagerWindow : Window
{
    private readonly VenuePlusApp _app;
    private string _newName = string.Empty;
    private System.Collections.Generic.Dictionary<int, string> _editNames = new System.Collections.Generic.Dictionary<int, string>();
    private int _lastCount = -1;
    private System.Collections.Generic.Dictionary<int, float> _editPosX = new System.Collections.Generic.Dictionary<int, float>();
    private System.Collections.Generic.Dictionary<int, float> _editPosY = new System.Collections.Generic.Dictionary<int, float>();

    public MacroHotbarManagerWindow(VenuePlusApp app) : base("Macro Hotbar Manager")
    {
        _app = app;
        Size = new Vector2(420f, 360f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320f, 280f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        var count = _app.GetMacroHotbarCountUnsafe();
        if (count != _lastCount) { _editNames.Clear(); _lastCount = count; }
        var openIndices = _app.GetOpenMacroHotbarIndicesUnsafe();
        ImGui.TextUnformatted("Available Hotbars");
        ImGui.Separator();
        ImGui.Separator();
        for (int i = 0; i < count; i++)
        {
            var name = _app.GetMacroHotbarName(i);
            var edit = _editNames.TryGetValue(i, out var n) ? n : name;
            ImGui.SetNextItemWidth(220f);
            ImGui.InputText($"##mhb_name_{i}", ref edit, 64);
            _editNames[i] = edit;
            if (ImGui.IsItemEdited())
            {
                _ = _app.RenameMacroHotbarAsync(i, edit);
            }
            ImGui.SameLine();
            var isStartup = System.Array.IndexOf(openIndices, i) >= 0;
            if (ImGui.Checkbox($"Show on startup##mhb_start_{i}", ref isStartup))
            {
                _ = _app.SetMacroHotbarOpenStateAsync(i, isStartup);
                if (isStartup) _app.OpenMacroHotbarWindowAt(i);
                else _app.CloseMacroHotbarWindowAt(i);
            }
            ImGui.SameLine();
            if (ImGui.Button($"Remove##mhb_remove_{i}"))
            {
                _ = _app.RemoveMacroHotbarAtAsync(i);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"Open##mhb_open_{i}"))
            {
                _app.OpenMacroHotbarWindowAt(i);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"Position##mhb_pos_menu_{i}"))
            {
                ImGui.OpenPopup($"mhb_pos_popup_{i}");
            }
            var popupId = $"mhb_pos_popup_{i}";
            if (ImGui.BeginPopup(popupId))
            {
                var posOpt = _app.GetMacroHotbarWindowPositionAt(i);
                var disp = ImGui.GetIO().DisplaySize;
                var px = posOpt?.X ?? (_editPosX.TryGetValue(i, out var xv) ? xv : 0f);
                var py = posOpt?.Y ?? (_editPosY.TryGetValue(i, out var yv) ? yv : 0f);
                ImGui.TextUnformatted("Position X:"); ImGui.SameLine(); ImGui.SetNextItemWidth(160f);
                if (ImGui.SliderFloat($"##mhb_pos_x_{i}", ref px, 0f, disp.X))
                {
                    px = System.Math.Clamp(px, 0f, disp.X);
                    _editPosX[i] = px;
                    _app.SetMacroHotbarPositionAt(i, new Vector2(px, py));
                }
                ImGui.SameLine();
                ImGui.TextUnformatted("Y:"); ImGui.SameLine(); ImGui.SetNextItemWidth(160f);
                if (ImGui.SliderFloat($"##mhb_pos_y_{i}", ref py, 0f, disp.Y))
                {
                    py = System.Math.Clamp(py, 0f, disp.Y);
                    _editPosY[i] = py;
                    _app.SetMacroHotbarPositionAt(i, new Vector2(px, py));
                }
                if (ImGui.Button($"Reset Position##mhb_pos_reset_{i}"))
                {
                    _app.ResetMacroHotbarPositionAt(i);
                }
                ImGui.EndPopup();
            }
            ImGui.Separator();
        }

        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##mhb_new_name", "New hotbar name (optional)", ref _newName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add Hotbar"))
        {
            var nameUse = string.IsNullOrWhiteSpace(_newName) ? null : _newName.Trim();
            _ = _app.AddMacroHotbarAsync(nameUse);
            _newName = string.Empty;
        }
        
    }
}
