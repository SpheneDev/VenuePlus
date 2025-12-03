using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class RegisterClubModal
{
    private bool _open;
    private string _clubId = string.Empty;
    private string _status = string.Empty;

    public void Open()
    {
        _clubId = string.Empty;
        _status = string.Empty;
        _open = true;
    }

    public void Draw(VenuePlusApp app)
    {
        if (_open)
        {
            ImGui.OpenPopup("Register Venue");
            if (ImGui.BeginPopupModal("Register Venue", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText("Venue Id", ref _clubId, 64);
                
                var canRegister = !string.IsNullOrWhiteSpace(_clubId);
                ImGui.BeginDisabled(!canRegister);
                if (ImGui.Button("Create"))
                {
                    _status = "Submitting...";
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.RegisterClubAsync(_clubId);
                        _status = ok ? "Venue created" : (app.GetLastServerMessage() ?? "Venue name already exists. Please choose another name.");
                        if (ok) _open = false;
                    });
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(canRegister ? "Create a new venue" : "Enter a venue id"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    _open = false;
                    _status = string.Empty;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                ImGui.EndPopup();
            }
        }
    }
}
