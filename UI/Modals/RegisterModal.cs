using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class RegisterModal
{
    private bool _open;
    private string _password = string.Empty;
    private string _status = string.Empty;
    private string _recoveryCode = string.Empty;
    private bool _showRecovery;
    private System.DateTimeOffset _submittedAt;
    private bool _delayCloseRequested;

    public void Open()
    {
        _password = string.Empty;
        _status = string.Empty;
        _recoveryCode = string.Empty;
        _showRecovery = false;
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
                    _recoveryCode = string.Empty;
                    _showRecovery = false;
                    var n = name; var w = world; var p = _password;
                    _delayCloseRequested = true;
                    _submittedAt = System.DateTimeOffset.UtcNow;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var result = await app.RegisterCharacterAsync(n, w, p);
                        _status = result.LoginOk ? "Registration successful" : "Registration failed";
                        _recoveryCode = result.RecoveryCode ?? string.Empty;
                        _showRecovery = result.LoginOk && !string.IsNullOrWhiteSpace(_recoveryCode);
                    });
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Register this character as staff"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    _password = string.Empty;
                    _status = string.Empty;
                    _recoveryCode = string.Empty;
                    _showRecovery = false;
                    _open = false;
                    _delayCloseRequested = false;
                }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                if (_showRecovery)
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted("Recovery Code");
                    ImGui.TextWrapped("Please save this recovery code. It is required to reset your password.");
                    ImGui.InputText("Code", ref _recoveryCode, 64, ImGuiInputTextFlags.ReadOnly);
                    ImGui.SameLine();
                    if (ImGui.Button("Copy")) { ImGui.SetClipboardText(_recoveryCode); }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Copy recovery code to clipboard"); ImGui.EndTooltip(); }
                }
                if (_delayCloseRequested && !_showRecovery && System.DateTimeOffset.UtcNow >= _submittedAt.AddSeconds(2)) _open = false;
                ImGui.EndPopup();
            }
        }
    }
}
