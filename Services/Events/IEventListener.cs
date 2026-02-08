using System;
using System.Collections.Generic;
using VenuePlus.State;

namespace VenuePlus.Services;

internal interface IEventListener
{
    void OnSnapshotReceived(IReadOnlyCollection<VipEntry> entries);
    void OnEntryAdded(VipEntry entry);
    void OnEntryRemoved(VipEntry entry);
    void OnDjSnapshotReceived(DjEntry[]? entries);
    void OnDjEntryAdded(DjEntry entry);
    void OnDjEntryRemoved(DjEntry entry);
    void OnShiftSnapshotReceived(ShiftEntry[]? entries);
    void OnShiftEntryAdded(ShiftEntry entry);
    void OnShiftEntryUpdated(ShiftEntry entry);
    void OnShiftEntryRemoved(Guid id);
    void OnEventSnapshotReceived(EventEntry[]? entries);
    void OnEventEntryAdded(EventEntry entry);
    void OnEventEntryUpdated(EventEntry entry);
    void OnEventEntryRemoved(Guid id);
    void OnJobsListReceived(string[] arr);
    void OnJobRightsReceived(Dictionary<string, JobRightsInfo> dict);
    void OnUsersListReceived(string[] arr);
    void OnUsersDetailsReceived(StaffUser[] det);
    void OnUserJobUpdated(string username, string job, string[] jobs);
    void OnOwnerAccessChanged(string owner, string clubIdEvt);
    void OnMembershipRemoved(string username, string clubIdEvt);
    void OnMembershipAdded(string username, string clubId);
    void OnClubLogoReceived(string? base64);
    void OnConnectionChanged(bool connected);
    void OnServerAnnouncementReceived(string message);
}
