using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;
using Dalamud.Interface;
using VenuePlus.Helpers;

namespace VenuePlus.UI.Modals;

public sealed class ManageUsersModal
{
    private string _manageUserName = string.Empty;
    private string _manageUserPassword = string.Empty;
    private string _status = string.Empty;
    private string _generated = string.Empty;

    public void Draw(VenuePlusApp app)
    {
        if (ImGui.BeginPopupModal("Manage Users", flags: ImGuiWindowFlags.AlwaysAutoResize))
        {
            var hasPriv = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
            ImGui.TextUnformatted(hasPriv ? "Authorization: active" : "Authorization required: Owner or Staff with rights");
            ImGui.Separator();
            ImGui.PushItemWidth(260f);
            ImGui.InputText("Username", ref _manageUserName, 64);
            ImGui.SameLine(); IconDraw.IconText(FontAwesomeIcon.QuestionCircle); if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Enter CharacterName@Homeworld"); ImGui.EndTooltip(); }
            ImGui.InputText("Password", ref _manageUserPassword, 64, ImGuiInputTextFlags.Password);
            ImGui.SameLine(); IconDraw.IconText(FontAwesomeIcon.QuestionCircle); if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Choose a secure password for this staff member"); ImGui.EndTooltip(); }
            ImGui.PopItemWidth();
            if (ImGui.Button("Generate Password"))
            {
                _generated = GeneratePassword(16);
                _manageUserPassword = _generated;
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Generate a secure random password"); ImGui.EndTooltip(); }
            ImGui.SameLine();
            if (!string.IsNullOrWhiteSpace(_generated))
            {
                ImGui.TextUnformatted($"Generated: {_generated}");
                ImGui.SameLine();
                if (ImGui.Button("Copy")) { ImGui.SetClipboardText(_generated); }
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Copy generated password to clipboard"); ImGui.EndTooltip(); }
            }
            ImGui.BeginDisabled(!hasPriv);
            if (ImGui.Button("Create/Update"))
            {
                if (string.IsNullOrWhiteSpace(_manageUserPassword))
                {
                    _generated = GeneratePassword(16);
                    _manageUserPassword = _generated;
                }
                _status = "Submitting...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.CreateUserAsync(_manageUserName, _manageUserPassword);
                    _status = ok ? "User created/updated" : "Create failed";
                });
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(hasPriv ? "Create a new staff user or update an existing one" : "Requires Owner or Manage Users rights"); ImGui.EndTooltip(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close"))
            {
                _manageUserName = string.Empty;
                _manageUserPassword = string.Empty;
                _status = string.Empty;
                _generated = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Close this dialog"); ImGui.EndTooltip(); }
            if (!string.IsNullOrEmpty(_status))
            {
                ImGui.TextUnformatted(_status);
            }
            ImGui.EndPopup();
        }
    }

    private static string GeneratePassword(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%?";
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var sb = new System.Text.StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(chars[bytes[i] % chars.Length]);
        }
        return sb.ToString();
    }
}
