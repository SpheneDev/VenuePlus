using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;
using Dalamud.Interface;
using VenuePlus.State;

namespace VenuePlus.UI.Modals;

public sealed class AddVipModal
{
    private string _pendingName = string.Empty;
    private string _pendingWorld = string.Empty;
    private VipDuration _pendingDuration = VipDuration.FourWeeks;
    private bool _open;

    public void Open()
    {
        _pendingName = string.Empty;
        _pendingWorld = string.Empty;
        _pendingDuration = VipDuration.FourWeeks;
        _open = true;
    }

    public void Draw(VenuePlusApp app)
    {
        if (_open)
        {
            ImGui.OpenPopup("Add VIP");
            if (ImGui.BeginPopupModal("Add VIP", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushItemWidth(280f);
                ImGui.InputText("Character Name", ref _pendingName, 128);
                ImGui.SameLine(); ImGui.PushFont(UiBuilder.IconFont); ImGui.TextUnformatted(FontAwesomeIcon.QuestionCircle.ToIconString()); ImGui.PopFont(); if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Enter the exact in-game character name for the VIP"); ImGui.EndTooltip(); }
                ImGui.InputText("Homeworld", ref _pendingWorld, 64);
                ImGui.SameLine(); ImGui.PushFont(UiBuilder.IconFont); ImGui.TextUnformatted(FontAwesomeIcon.QuestionCircle.ToIconString()); ImGui.PopFont(); if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Optional. Leave empty to use 'Unknown'."); ImGui.EndTooltip(); }
                ImGui.PopItemWidth();
                ImGui.Separator();
                ImGui.TextUnformatted("VIP Duration:");
                bool d4 = _pendingDuration == VipDuration.FourWeeks;
                bool d12 = _pendingDuration == VipDuration.TwelveWeeks;
                bool dLife = _pendingDuration == VipDuration.Lifetime;
                if (ImGui.RadioButton("1 Month", d4)) _pendingDuration = VipDuration.FourWeeks;
                ImGui.SameLine();
                if (ImGui.RadioButton("3 Months", d12)) _pendingDuration = VipDuration.TwelveWeeks;
                ImGui.SameLine();
                if (ImGui.RadioButton("Unlimited", dLife)) _pendingDuration = VipDuration.Lifetime;
                ImGui.SameLine(); ImGui.PushFont(UiBuilder.IconFont); ImGui.TextUnformatted(FontAwesomeIcon.Infinity.ToIconString()); ImGui.PopFont();

                ImGui.Separator();
                var canAdd = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanAddVip);
                ImGui.BeginDisabled(!canAdd || string.IsNullOrWhiteSpace(_pendingName));
                if (ImGui.Button("Add"))
                {
                    var world = string.IsNullOrWhiteSpace(_pendingWorld) ? "Unknown" : _pendingWorld.Trim();
                    app.AddVip(_pendingName.Trim(), world, _pendingDuration);
                    _open = false;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(canAdd ? "Add VIP with entered details" : "Requires Owner or Add VIP rights"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _open = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close without adding a VIP entry"); ImGui.EndTooltip(); }
                ImGui.EndPopup();
            }
        }
    }
}
