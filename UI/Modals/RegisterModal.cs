using System.Numerics;
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
    private bool _recoveryCopied;
    private bool _registrationComplete;
    private string _copyStatus = string.Empty;

    public void Open()
    {
        _password = string.Empty;
        _status = string.Empty;
        _recoveryCode = string.Empty;
        _showRecovery = false;
        _recoveryCopied = false;
        _registrationComplete = false;
        _copyStatus = string.Empty;
        _open = true;
    }

    public void Draw(VenuePlusApp app)
    {
        if (!_open && _showRecovery && !_recoveryCopied)
        {
            _open = true;
        }
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
                ImGui.BeginDisabled(_registrationComplete);
                ImGui.InputText("Password", ref _password, 64, ImGuiInputTextFlags.Password);
                ImGui.EndDisabled();
                var canRegister = info.HasValue && !string.IsNullOrWhiteSpace(_password) && app.RemoteConnected && !_registrationComplete;
                ImGui.BeginDisabled(!canRegister);
                if (ImGui.Button("Register"))
                {
                    _status = "Submitting...";
                    _recoveryCode = string.Empty;
                    _showRecovery = false;
                    _recoveryCopied = false;
                    _registrationComplete = false;
                    _copyStatus = string.Empty;
                    var n = name; var w = world; var p = _password;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var result = await app.RegisterCharacterAsync(n, w, p);
                        _status = result.LoginOk ? "Registration successful" : "Registration failed";
                        _recoveryCode = result.RecoveryCode ?? string.Empty;
                        _showRecovery = result.LoginOk && !string.IsNullOrWhiteSpace(_recoveryCode);
                        _registrationComplete = result.LoginOk;
                    });
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Register this character as staff"); ImGui.EndTooltip(); }
                ImGui.SameLine();
                var canClose = !_showRecovery || _recoveryCopied;
                ImGui.BeginDisabled(!canClose);
                if (ImGui.Button("Close"))
                {
                    _password = string.Empty;
                    _status = string.Empty;
                    _recoveryCode = string.Empty;
                    _showRecovery = false;
                    _recoveryCopied = false;
                    _registrationComplete = false;
                    _copyStatus = string.Empty;
                    _open = false;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
                if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
                if (_showRecovery)
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted("Recovery Code");
                    ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), "Important");
                    ImGui.TextWrapped("Save this recovery code now. It is required to reset your password.");
                    ImGui.TextWrapped("You must copy it before closing this window.");
                    ImGui.InputText("Code", ref _recoveryCode, 64, ImGuiInputTextFlags.ReadOnly);
                    ImGui.SameLine();
                    if (ImGui.Button("Copy"))
                    {
                        ImGui.SetClipboardText(_recoveryCode);
                        _recoveryCopied = true;
                        _copyStatus = "Recovery code copied. You can close this window.";
                    }
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Copy recovery code to clipboard"); ImGui.EndTooltip(); }
                    if (!string.IsNullOrEmpty(_copyStatus)) ImGui.TextUnformatted(_copyStatus);
                }
                ImGui.EndPopup();
            }
        }
    }
}
