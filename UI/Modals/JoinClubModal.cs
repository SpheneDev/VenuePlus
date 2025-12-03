using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class JoinClubModal
{
    private bool _open;
    private string _clubId = string.Empty;
    private string _password = string.Empty;
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
            ImGui.OpenPopup("Join Venue");
            if (ImGui.BeginPopupModal("Join Venue", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText("Venue Id", ref _clubId, 64);
                ImGui.InputTextWithHint("##join_password", "Join Password", ref _password, 64, ImGuiInputTextFlags.Password);
                
                var canJoin = !string.IsNullOrWhiteSpace(_clubId);
                ImGui.BeginDisabled(!canJoin);
                if (ImGui.Button("Join"))
                {
                    _status = "Submitting...";
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        app.SetPendingJoinPassword(_password);
                        var ok = await app.JoinClubAsync(_clubId);
                        _status = ok ? "Joined" : (app.GetLastServerMessage() ?? "Join failed");
                        if (ok) _open = false;
                    });
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(canJoin ? "Join selected venue" : "Enter a venue id"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    _open = false;
                    _status = string.Empty;
                    _password = string.Empty;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                ImGui.EndPopup();
            }
        }
    }
}
