using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;

namespace VenuePlus.UI.Modals;

public sealed class StaffLoginModal
{
    private const int StatusPollDelayMs = 120;
    private const int StatusPollMaxTicks = 60;
    private string _staffPassInput = string.Empty;
    private bool _rememberStaff;
    private string _status = string.Empty;
    private bool _closeRequested;

    public void Draw(VenuePlusApp app)
    {
        app.UpdateCurrentCharacterCache();
        if (ImGui.BeginPopupModal("Login", flags: ImGuiWindowFlags.AlwaysAutoResize))
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
            ImGui.InputText("Password", ref _staffPassInput, 64, ImGuiInputTextFlags.Password);
            ImGui.Checkbox("Remember Login Password", ref _rememberStaff);
            var autoEnabled = app.AutoLoginEnabled;
            if (ImGui.Checkbox("Enable Auto Login", ref autoEnabled))
            {
                app.SetAutoLoginEnabledAsync(autoEnabled).GetAwaiter().GetResult();
                if (autoEnabled) app.SetRememberStaffLoginAsync(true).GetAwaiter().GetResult();
            }
            ImGui.BeginDisabled(!app.RemoteConnected);
            if (ImGui.Button("Login"))
            {
                if (_rememberStaff)
                {
                    app.SetRememberStaffLoginAsync(true).GetAwaiter().GetResult();
                }
                if (autoEnabled)
                {
                    app.SetAutoLoginEnabledAsync(true).GetAwaiter().GetResult();
                    app.SetRememberStaffLoginAsync(true).GetAwaiter().GetResult();
                }
                _status = "Authenticating...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.StaffLoginAsync(string.Empty, _staffPassInput);
                    if (ok)
                    {
                        if (app.AccessLoading)
                        {
                            _status = "Loading profile...";
                            for (int i = 0; i < StatusPollMaxTicks; i++)
                            {
                                await System.Threading.Tasks.Task.Delay(StatusPollDelayMs);
                                if (!app.AccessLoading) break;
                            }
                        }
                        _staffPassInput = string.Empty;
                        _closeRequested = true;
                        _status = string.Empty;
                    }
                    else
                    {
                        _status = "Login failed";
                    }
                });
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Login with entered credentials"); ImGui.EndTooltip(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _staffPassInput = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
            if (_closeRequested)
            {
                _closeRequested = false;
                ImGui.CloseCurrentPopup();
            }
            if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
            ImGui.EndPopup();
        }
    }
}
