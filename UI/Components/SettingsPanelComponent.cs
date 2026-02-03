using Dalamud.Bindings.ImGui;
using VenuePlus.Plugin;
using System;
using System.Numerics;
using System.Collections.Generic;
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
    private VenuePlus.State.StaffUser[]? _ownerTransferUsers;
    private string _ownerTransferSelectedUser = string.Empty;
    private string _ownerTransferStatus = string.Empty;
    private string _ownerTransferUserFilter = string.Empty;
    private bool _ownerTransferLoading;
    private string _ownerTransferLoadedClub = string.Empty;

    public void ResetStatusMessages()
    {
        _staffPassStatus = string.Empty;
        _dissolveStatus = string.Empty;
        _logoUploadStatus = string.Empty;
        _joinPasswordStatus = string.Empty;
        _dissolveConfirm = false;
        _ownerTransferSelectedUser = string.Empty;
        _ownerTransferStatus = string.Empty;
        _ownerTransferUserFilter = string.Empty;
    }

    public void Draw(VenuePlusApp app)
    {
        var canView = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        if (!canView) return;

        ImGui.Separator();
        ImGui.TextUnformatted("Venue Settings");
        ImGui.Separator();

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openLogo = ImGui.CollapsingHeader("Venue Logo", ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (openLogo)
        {
            DrawClubLogo(app);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openMaint = ImGui.CollapsingHeader("Maintenance", ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (openMaint)
        {
            DrawMaintenanceActions(app);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openMember = ImGui.CollapsingHeader("Membership", ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (openMember)
        {
            DrawMembershipActions(app);
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openLinks = ImGui.CollapsingHeader("Public Access Links", ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (openLinks)
        {
            DrawPublicAccessLinks(app);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Header, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
        var openOwnerTransfer = ImGui.CollapsingHeader("Owner Transfer", ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor(3);
        if (openOwnerTransfer)
        {
            DrawOwnerTransfer(app);
        }

        if (app.IsOwnerCurrentClub)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Header, 0u);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0u);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0u);
            var openDanger = ImGui.CollapsingHeader("Danger Zone", ImGuiTreeNodeFlags.None);
            ImGui.PopStyleColor(3);
            if (openDanger)
            {
                DrawDissolveSection(app);
            }
        }
    }


    private void DrawMaintenanceActions(VenuePlusApp app)
    {
        ImGui.TextDisabled("Routine tasks to keep your venue clean and up to date.");
        ImGui.Spacing();
        var canManageSettings = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        var canPurge = canManageSettings && (app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanRemoveVip));
        ImGui.BeginDisabled(!canPurge);
        if (ImGui.Button("Purge Expired VIPs")) { app.PurgeExpired(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Remove expired VIP entries from the venue"); ImGui.EndTooltip(); }
        ImGui.EndDisabled();

        
    }

    private void DrawMembershipActions(VenuePlusApp app)
    {
        var canManageSettings = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        ImGui.TextDisabled("Control how new staff members join your venue.");
        ImGui.Spacing();
        ImGui.BeginDisabled(!canManageSettings);
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
        
    }


    private void DrawClubLogo(VenuePlusApp app)
    {
        var canManageSettings = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        var canUpload = canManageSettings;
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
        var canManageSettings = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        var canDissolve = app.IsOwnerCurrentClub && canManageSettings;
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
            var ctrlPressed = ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
            ImGui.BeginDisabled(!ctrlPressed);
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
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(ctrlPressed ? "Permanently delete this venue" : "Hold Ctrl to confirm");
                ImGui.EndTooltip();
            }
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
        var canLinks = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanManageVenueSettings);
        if (!canLinks)
        {
            ImGui.TextDisabled("Manage venue settings required.");
            return;
        }
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
        if (app.IsOwnerCurrentClub && ImGui.Button("Regenerate Access Key"))
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

    private void DrawOwnerTransfer(VenuePlusApp app)
    {
        if (!app.IsOwnerCurrentClub)
        {
            ImGui.TextDisabled("Owner only.");
            return;
        }
        var clubId = app.CurrentClubId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clubId))
        {
            ImGui.TextUnformatted("No active venue selected.");
            return;
        }
        if (!string.Equals(_ownerTransferLoadedClub, clubId, StringComparison.Ordinal))
        {
            _ownerTransferLoadedClub = clubId;
            _ownerTransferUsers = null;
            _ownerTransferSelectedUser = string.Empty;
            _ownerTransferUserFilter = string.Empty;
            _ownerTransferStatus = string.Empty;
        }
        if (_ownerTransferUsers == null && !_ownerTransferLoading)
        {
            RequestOwnerTransferUsers(app, clubId);
        }
        ImGui.TextDisabled("Transfer current venue ownership to another staff member.");
        ImGui.Spacing();
        if (_ownerTransferLoading)
        {
            ImGui.TextUnformatted("Loading staff list...");
        }
        if (_ownerTransferUsers != null)
        {
            var eligibleUsers = GetEligibleOwnerTransferUsers(_ownerTransferUsers, app.CurrentStaffUsername, _ownerTransferUserFilter);
            ImGui.TextUnformatted($"Venue: {clubId}");
            ImGui.PushItemWidth(200f);
            ImGui.InputTextWithHint("##owner_transfer_filter_settings", "Search by username", ref _ownerTransferUserFilter, 128);
            ImGui.PopItemWidth();
            if (eligibleUsers.Length == 0)
            {
                ImGui.TextDisabled("No eligible users found.");
            }
            else
            {
                for (int i = 0; i < eligibleUsers.Length; i++)
                {
                    var uname = eligibleUsers[i].Username ?? string.Empty;
                    var selected = string.Equals(uname, _ownerTransferSelectedUser, StringComparison.Ordinal);
                    if (ImGui.Selectable(uname, selected))
                    {
                        _ownerTransferSelectedUser = uname;
                        TriggerOwnerTransfer(app, clubId, uname);
                    }
                }
            }
        }
        if (!string.IsNullOrEmpty(_ownerTransferStatus)) ImGui.TextUnformatted(_ownerTransferStatus);
    }

    private void RequestOwnerTransferUsers(VenuePlusApp app, string clubId)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return;
        if (_ownerTransferLoading) return;
        _ownerTransferLoading = true;
        _ownerTransferStatus = "Loading staff list...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            await WaitForClubAsync(app, clubId);
            var users = await app.FetchStaffUsersDetailedAsync();
            _ownerTransferUsers = users ?? Array.Empty<VenuePlus.State.StaffUser>();
            _ownerTransferLoading = false;
            _ownerTransferStatus = users == null ? "No staff data available" : $"Loaded {_ownerTransferUsers.Length} users";
        });
    }

    private void TriggerOwnerTransfer(VenuePlusApp app, string clubId, string username)
    {
        if (string.IsNullOrWhiteSpace(clubId) || string.IsNullOrWhiteSpace(username)) return;
        if (_ownerTransferLoading) return;
        _ownerTransferLoading = true;
        _ownerTransferStatus = "Transferring owner...";
        System.Threading.Tasks.Task.Run(async () =>
        {
            await WaitForClubAsync(app, clubId);
            var users = _ownerTransferUsers ?? Array.Empty<VenuePlus.State.StaffUser>();
            var target = FindStaffUser(users, username);
            var selfName = app.CurrentStaffUsername;
            var selfUser = FindStaffUser(users, selfName);
            if (target == null)
            {
                _ownerTransferStatus = "User not found";
                _ownerTransferLoading = false;
                return;
            }
            var targetJobs = AddOwnerJob(BuildJobsFromUser(target));
            var okTarget = await app.UpdateStaffUserJobsAsync(target.Username, targetJobs);
            if (!okTarget)
            {
                _ownerTransferStatus = app.GetLastServerMessage() ?? "Owner transfer failed";
                _ownerTransferLoading = false;
                return;
            }
            if (selfUser != null)
            {
                var selfJobs = RemoveOwnerJob(BuildJobsFromUser(selfUser));
                var okSelf = await app.UpdateStaffUserJobsAsync(selfUser.Username, selfJobs);
                _ownerTransferStatus = okSelf ? "Owner transferred" : (app.GetLastServerMessage() ?? "Owner transferred, self role not updated");
            }
            else
            {
                _ownerTransferStatus = "Owner transferred";
            }
            _ownerTransferLoading = false;
        });
    }

    private static async System.Threading.Tasks.Task WaitForClubAsync(VenuePlusApp app, string clubId)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < 5)
        {
            if (string.Equals(app.CurrentClubId, clubId, StringComparison.Ordinal) && !app.AccessLoading) return;
            await System.Threading.Tasks.Task.Delay(100);
        }
    }

    private static VenuePlus.State.StaffUser[] GetEligibleOwnerTransferUsers(VenuePlus.State.StaffUser[] users, string? selfName, string filter)
    {
        var list = new List<VenuePlus.State.StaffUser>();
        var f = filter?.Trim() ?? string.Empty;
        for (int i = 0; i < users.Length; i++)
        {
            var u = users[i];
            if (u == null) continue;
            var uname = u.Username ?? string.Empty;
            if (string.IsNullOrWhiteSpace(uname)) continue;
            if (u.IsManual) continue;
            if (string.Equals(uname, selfName, StringComparison.Ordinal)) continue;
            if (!string.IsNullOrWhiteSpace(f) && uname.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
            list.Add(u);
        }
        return list.ToArray();
    }

    private static VenuePlus.State.StaffUser? FindStaffUser(VenuePlus.State.StaffUser[] users, string? username)
    {
        if (users == null || string.IsNullOrWhiteSpace(username)) return null;
        for (int i = 0; i < users.Length; i++)
        {
            var u = users[i];
            if (u == null) continue;
            if (string.Equals(u.Username, username, StringComparison.Ordinal)) return u;
        }
        return null;
    }

    private static string[] BuildJobsFromUser(VenuePlus.State.StaffUser user)
    {
        if (user == null) return new[] { "Unassigned" };
        var jobs = user.Jobs ?? Array.Empty<string>();
        if (jobs.Length > 0) return jobs;
        if (!string.IsNullOrWhiteSpace(user.Job)) return new[] { user.Job };
        return new[] { "Unassigned" };
    }

    private static string[] AddOwnerJob(string[] jobs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            if (!string.IsNullOrWhiteSpace(j)) set.Add(j);
        }
        set.Add("Owner");
        if (set.Count == 0) set.Add("Owner");
        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    private static string[] RemoveOwnerJob(string[] jobs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < jobs.Length; i++)
        {
            var j = jobs[i];
            if (string.Equals(j, "Owner", StringComparison.Ordinal)) continue;
            if (!string.IsNullOrWhiteSpace(j)) set.Add(j);
        }
        if (set.Count == 0) set.Add("Unassigned");
        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }
}
