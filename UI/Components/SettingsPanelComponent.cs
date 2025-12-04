using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface;
 
namespace VenuePlus.UI.Components;

public sealed class SettingsPanelComponent
{
    private string _staffNewPassword = string.Empty;
    private string _staffPassStatus = string.Empty;
    private string _dissolveStatus = string.Empty;
    private bool _dissolveConfirm;
    private string _publicVipUrl = string.Empty;
    private string _publicStaffUrl = string.Empty;
    private string _publicDjUrl = string.Empty;
    private bool _linksInitialized;
    private System.Threading.Tasks.Task? _linksInitTask;
    private System.Threading.Tasks.Task? _linksRegenTask;
    private string _logoFilePath = string.Empty;
    private string _logoBase64Input = string.Empty;
    private string _logoUploadStatus = string.Empty;
    private readonly FileDialogManager _fileDialogManager = new();
    private string _joinPassword = string.Empty;
    private string _joinPasswordStatus = string.Empty;
    private string _inviteUid = string.Empty;
    private string _inviteJob = string.Empty;
    private string _inviteStatus = string.Empty;
    private string[] _jobOptions = System.Array.Empty<string>();
    private string _inviteJobSelected = "Unassigned";

    public void Draw(VenuePlusApp app)
    {
        if (!app.HasStaffSession) return;

        ImGui.Separator();
        ImGui.TextUnformatted("Venue Settings");
        ImGui.Separator();

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openLogo = ImGui.CollapsingHeader("Venue Logo", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        if (openLogo)
        {
            DrawClubLogo(app);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openMaint = ImGui.CollapsingHeader("Maintenance", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        if (openMaint)
        {
            DrawMaintenanceActions(app);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openMember = ImGui.CollapsingHeader("Membership", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        if (openMember)
        {
            DrawMembershipActions(app);
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openLinks = ImGui.CollapsingHeader("Public Access Links", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        if (openLinks)
        {
            DrawPublicAccessLinks(app);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openDanger = ImGui.CollapsingHeader("Danger Zone", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);
        if (openDanger)
        {
            DrawDissolveSection(app);
        }
    }


    private void DrawMaintenanceActions(VenuePlusApp app)
    {
        ImGui.TextDisabled("Routine tasks to keep your venue clean and up to date.");
        ImGui.Spacing();
        var canPurge = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanRemoveVip);
        ImGui.BeginDisabled(!canPurge);
        if (ImGui.Button("Purge Expired VIPs")) { app.PurgeExpired(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Remove expired VIP entries from the venue"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();

        
    }

    private void DrawMembershipActions(VenuePlusApp app)
    {
        var isOwner = app.IsOwnerCurrentClub;
        ImGui.TextDisabled("Control how new staff members join your venue.");
        ImGui.Spacing();
        ImGui.BeginDisabled(!isOwner);
        ImGui.PushItemWidth(150f);
        ImGui.InputTextWithHint("##join_password_set", "Set Join Password", ref _joinPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Save Join Password"))
        {
            _joinPasswordStatus = "Submitting...";
            var pass = _joinPassword;
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await app.SetClubJoinPasswordAsync(pass);
                _joinPasswordStatus = ok ? "Join password updated" : (app.GetLastServerMessage() ?? "Update failed");
                if (ok) _joinPassword = string.Empty;
            });
        }
        if (!string.IsNullOrEmpty(_joinPasswordStatus)) ImGui.TextUnformatted(_joinPasswordStatus);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextDisabled("Invite a specific registered user by UID.");
        var canInviteUsers = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageUsers);
        if (canInviteUsers)
        {
            ImGui.PushItemWidth(150f);
            ImGui.InputTextWithHint("##invite_uid", "Target UID", ref _inviteUid, 24);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            var currentJob = string.IsNullOrWhiteSpace(_inviteJobSelected) ? "Unassigned" : _inviteJobSelected;
            ImGui.PushItemWidth(150f);
            if (ImGui.BeginCombo("##invite_job_select", currentJob))
            {
                foreach (var name in _jobOptions)
                {
                    if (string.Equals(name, "Owner", System.StringComparison.Ordinal)) continue;
                    var rightsCache2 = app.GetJobRightsCache();
                    if (rightsCache2 != null && rightsCache2.TryGetValue(name, out var infoOpt))
                    {
                        var col2 = VenuePlus.Helpers.ColorUtil.HexToU32(infoOpt.ColorHex);
                        var icon2 = VenuePlus.Helpers.IconDraw.ParseIcon(infoOpt.IconKey);
                        VenuePlus.Helpers.IconDraw.IconText(icon2, 0.9f, col2);
                        ImGui.SameLine();
                    }
                    bool selected = string.Equals(currentJob, name, System.StringComparison.Ordinal);
                    if (ImGui.Selectable(name, selected)) _inviteJobSelected = name;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_inviteUid));
            if (ImGui.Button("Invite"))
            {
                _inviteStatus = "Submitting...";
                var uid = _inviteUid; var jobSel = string.IsNullOrWhiteSpace(_inviteJobSelected) ? "Unassigned" : _inviteJobSelected;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.InviteStaffByUidAsync(uid, jobSel);
                    _inviteStatus = ok ? "Invitation sent" : (app.GetLastServerMessage() ?? "Invite failed");
                    if (ok) { _inviteUid = string.Empty; _inviteJobSelected = "Unassigned"; }
                });
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrEmpty(_inviteStatus)) ImGui.TextUnformatted(_inviteStatus);
        }
    }

    public void SetJobOptions(string[] jobs)
    {
        if (jobs != null && jobs.Length > 0) _jobOptions = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Distinct(jobs, System.StringComparer.Ordinal));
        if (_jobOptions.Length == 0) _jobOptions = new[] { "Unassigned" };
    }

    private void DrawClubLogo(VenuePlusApp app)
    {
        var canUpload = app.IsOwnerCurrentClub;
        var currentClub = app.CurrentClubId;
        var hasLogo = !string.IsNullOrWhiteSpace(app.CurrentClubLogoBase64);
        ImGui.TextUnformatted(hasLogo ? "Logo: set" : "Logo: not set");
        ImGui.Spacing();
        ImGui.TextDisabled("Upload a logo image (PNG/JPG). Max size 256x256.");
        ImGui.BeginDisabled(!canUpload);
        if (ImGui.Button("Browse..."))
        {
            _fileDialogManager.OpenFileDialog(
                "Select Logo Image",
                "Images{.png,.jpg,.jpeg,.bmp,.gif}",
                (ok, path) =>
                {
                    if (!ok) return;
                    _logoUploadStatus = "Uploading...";
                    _logoFilePath = path;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var res = await app.UpdateClubLogoFromFileAsync(path);
                        _logoUploadStatus = res ? "Logo updated" : (app.GetLastServerMessage() ?? "Upload failed");
                    });
                }
            );
        }
        ImGui.SameLine();
        var canDelete = canUpload && hasLogo;
        ImGui.BeginDisabled(!canDelete);
        if (ImGui.Button("Delete Logo"))
        {
            _logoUploadStatus = "Deleting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var res = await app.DeleteClubLogoAsync();
                _logoUploadStatus = res ? "Logo deleted" : (app.GetLastServerMessage() ?? "Delete failed");
                if (res) _logoFilePath = string.Empty;
            });
        }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(canUpload ? "Pick an image file and upload" : "Owner only"); ImGui.EndTooltip(); }
        if (!string.IsNullOrEmpty(_logoFilePath)) { ImGui.SameLine(); ImGui.TextUnformatted(_logoFilePath); }
        ImGui.Spacing();
        if (!string.IsNullOrEmpty(_logoUploadStatus)) ImGui.TextUnformatted(_logoUploadStatus);
        _fileDialogManager.Draw();
    }


    private void DrawDissolveSection(VenuePlusApp app)
    {
        ImGui.TextDisabled("Permanent deletion of this venue and related data. Proceed with caution.");
        ImGui.Spacing();
        var canDissolve = app.IsOwnerCurrentClub;
        ImGui.BeginDisabled(!canDissolve);
        if (!_dissolveConfirm)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            if (ImGui.Button("Dissolve Venue"))
            {
                _dissolveConfirm = true;
                _dissolveStatus = string.Empty;
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Delete venue, members, VIPs and roles"); ImGui.EndTooltip(); }
        }
        else
        {
            ImGui.TextUnformatted("Confirm: This action cannot be undone.");
            ImGui.SameLine();
            if (ImGui.Button("Confirm"))
            {
                _dissolveStatus = "Submitting...";
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.DeleteCurrentClubAsync();
                    _dissolveStatus = ok ? "Venue dissolved" : "Delete failed";
                    _dissolveConfirm = false;
                });
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Permanently delete this venue"); ImGui.EndTooltip(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _dissolveConfirm = false;
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Abort venue deletion"); ImGui.EndTooltip(); }
            if (!string.IsNullOrEmpty(_dissolveStatus)) ImGui.TextUnformatted(_dissolveStatus);
        }
        ImGui.EndDisabled();
    }

    private void DrawPublicAccessLinks(VenuePlusApp app)
    {
        ImGui.TextWrapped("Read-only JSON endpoints for VIP, staff and DJs of the current venue.");
        ImGui.TextWrapped("Share only with trusted tools or websites. Regenerating the access key invalidates old links.");
        ImGui.TextDisabled("Links are hidden; use the copy buttons to share safely.");
        ImGui.Spacing();
        var canLinks = app.HasStaffSession;
        if (canLinks && !_linksInitialized && (_linksInitTask == null || _linksInitTask.IsCompleted))
        {
            var clubId = app.CurrentClubId ?? string.Empty;
            _linksInitTask = System.Threading.Tasks.Task.Run(async () =>
            {
                await app.RefreshAccessKeyAsync();
                var baseUrl = app.GetServerBaseUrl();
                var key = app.CurrentAccessKey ?? string.Empty;
                var vipUrlNew = !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/viplist.json") : string.Empty;
                var staffUrlNew = !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/stafflist.json") : string.Empty;
                var djUrlNew = !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/djlist.json") : string.Empty;
                if (!string.IsNullOrWhiteSpace(vipUrlNew) && !string.IsNullOrWhiteSpace(staffUrlNew) && !string.IsNullOrWhiteSpace(djUrlNew) && string.Equals(clubId, app.CurrentClubId, System.StringComparison.Ordinal))
                {
                    _publicVipUrl = vipUrlNew;
                    _publicStaffUrl = staffUrlNew;
                    _publicDjUrl = djUrlNew;
                }
                _linksInitialized = true;
            });
        }

        var canRegenerate = app.IsOwnerCurrentClub;
        ImGui.BeginDisabled(!canRegenerate);
        if (ImGui.Button("Regenerate Access Key"))
        {
            if (_linksRegenTask == null || _linksRegenTask.IsCompleted)
            {
                var clubId = app.CurrentClubId ?? string.Empty;
                _linksRegenTask = System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.RegenerateAccessKeyAsync();
                    var baseUrl = app.GetServerBaseUrl();
                    var key = app.CurrentAccessKey ?? string.Empty;
                    var vipUrlNew = ok && !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/viplist.json") : string.Empty;
                    var staffUrlNew = ok && !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/stafflist.json") : string.Empty;
                    var djUrlNew = ok && !string.IsNullOrWhiteSpace(key) ? (baseUrl.TrimEnd('/') + "/" + key + "/djlist.json") : string.Empty;
                    if (!string.IsNullOrWhiteSpace(vipUrlNew) && !string.IsNullOrWhiteSpace(staffUrlNew) && !string.IsNullOrWhiteSpace(djUrlNew) && string.Equals(clubId, app.CurrentClubId, System.StringComparison.Ordinal))
                    {
                        _publicVipUrl = vipUrlNew;
                        _publicStaffUrl = staffUrlNew;
                        _publicDjUrl = djUrlNew;
                    }
                    _linksInitialized = true;
                });
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Owner only: generate new secure links"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();

        var hasVip = !string.IsNullOrWhiteSpace(_publicVipUrl);
        var hasStaff = !string.IsNullOrWhiteSpace(_publicStaffUrl);
        var hasDj = !string.IsNullOrWhiteSpace(_publicDjUrl);
        ImGui.BeginDisabled(!hasVip);
        if (ImGui.Button("Copy VIP Link")) { if (hasVip) ImGui.SetClipboardText(_publicVipUrl); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(hasVip ? "Copy public VIP JSON link to clipboard" : "Loading VIP link..."); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasStaff);
        if (ImGui.Button("Copy Staff Link")) { if (hasStaff) ImGui.SetClipboardText(_publicStaffUrl); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(hasStaff ? "Copy public Staff JSON link to clipboard" : "Loading Staff link..."); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasDj);
        if (ImGui.Button("Copy DJs Link")) { if (hasDj) ImGui.SetClipboardText(_publicDjUrl); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted(hasDj ? "Copy public DJs JSON link to clipboard" : "Loading DJs link..."); ImGui.EndTooltip(); }
        ImGui.EndDisabled();
    }
}
