using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class RegisterModal
{
    private bool _open;
    private string _password = string.Empty;
    private string _status = string.Empty;

    public void Open()
    {
        _password = string.Empty;
        _status = string.Empty;
        _open = true;
    }

    public void Draw(VenuePlusApp app)
    {
        if (_open)
        {
            ImGui.OpenPopup("Register");
            if (ImGui.BeginPopupModal("Register", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                var info = app.GetCurrentCharacter();
                var name = info?.name ?? "--";
                var world = info?.world ?? "--";
                ImGui.TextUnformatted("Character:");
                ImGui.SameLine();
                ImGui.TextUnformatted(name);
                ImGui.TextUnformatted("Homeworld:");
                ImGui.SameLine();
                ImGui.TextUnformatted(world);
                ImGui.InputText("Password", ref _password, 64, ImGuiInputTextFlags.Password);
                var canRegister = info.HasValue && !string.IsNullOrWhiteSpace(_password) && app.RemoteConnected;
                ImGui.BeginDisabled(!canRegister);
                if (ImGui.Button("Register"))
                {
                    _status = "Submitting...";
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = info.HasValue && await app.RegisterCharacterAsync(name, world, _password);
                        _status = ok ? "Registration successful" : "Registration failed";
                        if (ok) _open = false;
                    });
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Register this character as staff"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    _password = string.Empty;
                    _status = string.Empty;
                    _open = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                ImGui.EndPopup();
            }
        }
    }
}
