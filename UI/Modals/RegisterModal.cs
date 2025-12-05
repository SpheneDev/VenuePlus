using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class RegisterModal
{
    private bool _open;
    private string _password = string.Empty;
    private string _status = string.Empty;
    private System.DateTimeOffset _submittedAt;
    private bool _delayCloseRequested;

    public void Open()
    {
        _password = string.Empty;
        _status = string.Empty;
        _open = true;
        _submittedAt = System.DateTimeOffset.MinValue;
        _delayCloseRequested = false;
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
                    var n = name; var w = world; var p = _password;
                    _delayCloseRequested = true;
                    _submittedAt = System.DateTimeOffset.UtcNow;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.RegisterCharacterAsync(n, w, p);
                        _status = ok ? "Registration successful" : "Registration failed";
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
                    _delayCloseRequested = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                if (_delayCloseRequested && System.DateTimeOffset.UtcNow >= _submittedAt.AddSeconds(2)) _open = false;
                ImGui.EndPopup();
            }
        }
    }
}
