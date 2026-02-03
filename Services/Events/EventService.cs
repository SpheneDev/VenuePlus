using System;
using System.Collections.Generic;
using VenuePlus.State;

namespace VenuePlus.Services;

internal sealed class EventService : IDisposable
{
    private readonly RemoteSyncService _remote;
    private EventBindings? _bindings;
    private IEventListener? _listener;
    private bool _registered;

    public EventService(RemoteSyncService remote)
    {
        _remote = remote;
    }

    public void Register(EventBindings bindings)
    {
        if (_registered) Unregister();
        _bindings = bindings;
        _remote.SnapshotReceived += bindings.VipSnapshot;
        _remote.EntryAdded += bindings.VipAdded;
        _remote.EntryRemoved += bindings.VipRemoved;
        _remote.DjSnapshotReceived += bindings.DjSnapshot;
        _remote.DjEntryAdded += bindings.DjAdded;
        _remote.DjEntryRemoved += bindings.DjRemoved;
        _remote.ShiftSnapshotReceived += bindings.ShiftSnapshot;
        _remote.ShiftEntryAdded += bindings.ShiftAdded;
        _remote.ShiftEntryUpdated += bindings.ShiftUpdated;
        _remote.ShiftEntryRemoved += bindings.ShiftRemoved;
        _remote.JobsListReceived += bindings.JobsListReceived;
        _remote.JobRightsReceived += bindings.JobRightsReceived;
        _remote.UsersListReceived += bindings.UsersListReceived;
        _remote.UsersDetailsReceived += bindings.UsersDetailsReceived;
        _remote.UserJobUpdated += bindings.UserJobUpdated;
        _remote.OwnerAccessChanged += bindings.OwnerAccessChanged;
        _remote.MembershipRemoved += bindings.MembershipRemoved;
        _remote.MembershipAdded += bindings.MembershipAdded;
        _remote.ClubLogoReceived += bindings.ClubLogoReceived;
        _remote.ConnectionChanged += bindings.ConnectionChanged;
        _remote.ServerAnnouncementReceived += bindings.ServerAnnouncementReceived;
        _registered = true;
    }

    public void Register(IEventListener listener)
    {
        if (_registered) Unregister();
        _listener = listener;
        _remote.SnapshotReceived += listener.OnSnapshotReceived;
        _remote.EntryAdded += listener.OnEntryAdded;
        _remote.EntryRemoved += listener.OnEntryRemoved;
        _remote.DjSnapshotReceived += listener.OnDjSnapshotReceived;
        _remote.DjEntryAdded += listener.OnDjEntryAdded;
        _remote.DjEntryRemoved += listener.OnDjEntryRemoved;
        _remote.ShiftSnapshotReceived += listener.OnShiftSnapshotReceived;
        _remote.ShiftEntryAdded += listener.OnShiftEntryAdded;
        _remote.ShiftEntryUpdated += listener.OnShiftEntryUpdated;
        _remote.ShiftEntryRemoved += listener.OnShiftEntryRemoved;
        _remote.JobsListReceived += listener.OnJobsListReceived;
        _remote.JobRightsReceived += listener.OnJobRightsReceived;
        _remote.UsersListReceived += listener.OnUsersListReceived;
        _remote.UsersDetailsReceived += listener.OnUsersDetailsReceived;
        _remote.UserJobUpdated += listener.OnUserJobUpdated;
        _remote.OwnerAccessChanged += listener.OnOwnerAccessChanged;
        _remote.MembershipRemoved += listener.OnMembershipRemoved;
        _remote.MembershipAdded += listener.OnMembershipAdded;
        _remote.ClubLogoReceived += listener.OnClubLogoReceived;
        _remote.ConnectionChanged += listener.OnConnectionChanged;
        _remote.ServerAnnouncementReceived += listener.OnServerAnnouncementReceived;
        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered) return;

        if (_bindings != null)
        {
            var b = _bindings;
            _remote.SnapshotReceived -= b.VipSnapshot;
            _remote.EntryAdded -= b.VipAdded;
            _remote.EntryRemoved -= b.VipRemoved;
            _remote.DjSnapshotReceived -= b.DjSnapshot;
            _remote.DjEntryAdded -= b.DjAdded;
            _remote.DjEntryRemoved -= b.DjRemoved;
            _remote.ShiftSnapshotReceived -= b.ShiftSnapshot;
            _remote.ShiftEntryAdded -= b.ShiftAdded;
            _remote.ShiftEntryUpdated -= b.ShiftUpdated;
            _remote.ShiftEntryRemoved -= b.ShiftRemoved;
            _remote.JobsListReceived -= b.JobsListReceived;
            _remote.JobRightsReceived -= b.JobRightsReceived;
            _remote.UsersListReceived -= b.UsersListReceived;
            _remote.UsersDetailsReceived -= b.UsersDetailsReceived;
            _remote.UserJobUpdated -= b.UserJobUpdated;
            _remote.OwnerAccessChanged -= b.OwnerAccessChanged;
            _remote.MembershipRemoved -= b.MembershipRemoved;
            _remote.MembershipAdded -= b.MembershipAdded;
            _remote.ClubLogoReceived -= b.ClubLogoReceived;
            _remote.ConnectionChanged -= b.ConnectionChanged;
            _remote.ServerAnnouncementReceived -= b.ServerAnnouncementReceived;
            _bindings = null;
        }

        if (_listener != null)
        {
            var l = _listener;
            _remote.SnapshotReceived -= l.OnSnapshotReceived;
            _remote.EntryAdded -= l.OnEntryAdded;
            _remote.EntryRemoved -= l.OnEntryRemoved;
            _remote.DjSnapshotReceived -= l.OnDjSnapshotReceived;
            _remote.DjEntryAdded -= l.OnDjEntryAdded;
            _remote.DjEntryRemoved -= l.OnDjEntryRemoved;
            _remote.ShiftSnapshotReceived -= l.OnShiftSnapshotReceived;
            _remote.ShiftEntryAdded -= l.OnShiftEntryAdded;
            _remote.ShiftEntryUpdated -= l.OnShiftEntryUpdated;
            _remote.ShiftEntryRemoved -= l.OnShiftEntryRemoved;
            _remote.JobsListReceived -= l.OnJobsListReceived;
            _remote.JobRightsReceived -= l.OnJobRightsReceived;
            _remote.UsersListReceived -= l.OnUsersListReceived;
            _remote.UsersDetailsReceived -= l.OnUsersDetailsReceived;
            _remote.UserJobUpdated -= l.OnUserJobUpdated;
            _remote.OwnerAccessChanged -= l.OnOwnerAccessChanged;
            _remote.MembershipRemoved -= l.OnMembershipRemoved;
            _remote.MembershipAdded -= l.OnMembershipAdded;
            _remote.ClubLogoReceived -= l.OnClubLogoReceived;
            _remote.ConnectionChanged -= l.OnConnectionChanged;
            _remote.ServerAnnouncementReceived -= l.OnServerAnnouncementReceived;
            _listener = null;
        }

        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
    }
}

 

 
