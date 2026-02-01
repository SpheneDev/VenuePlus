using System;
using System.Collections.Generic;
using VenuePlus.State;

namespace VenuePlus.Services;

internal sealed class EventBindings
{
    public Action<IReadOnlyCollection<VipEntry>> VipSnapshot { get; init; } = _ => { };
    public Action<VipEntry> VipAdded { get; init; } = _ => { };
    public Action<VipEntry> VipRemoved { get; init; } = _ => { };
    public Action<DjEntry[]?> DjSnapshot { get; init; } = _ => { };
    public Action<DjEntry> DjAdded { get; init; } = _ => { };
    public Action<DjEntry> DjRemoved { get; init; } = _ => { };
    public Action<ShiftEntry[]?> ShiftSnapshot { get; init; } = _ => { };
    public Action<ShiftEntry> ShiftAdded { get; init; } = _ => { };
    public Action<ShiftEntry> ShiftUpdated { get; init; } = _ => { };
    public Action<Guid> ShiftRemoved { get; init; } = _ => { };
    public Action<string[]> JobsListReceived { get; init; } = _ => { };
    public Action<Dictionary<string, JobRightsInfo>> JobRightsReceived { get; init; } = _ => { };
    public Action<string[]> UsersListReceived { get; init; } = _ => { };
    public Action<StaffUser[]> UsersDetailsReceived { get; init; } = _ => { };
    public Action<string, string, string[]> UserJobUpdated { get; init; } = (_, _, _) => { };
    public Action<string, string> OwnerAccessChanged { get; init; } = (_, _) => { };
    public Action<string, string> MembershipRemoved { get; init; } = (_, _) => { };
    public Action<string, string> MembershipAdded { get; init; } = (_, _) => { };
    public Action<string?> ClubLogoReceived { get; init; } = _ => { };
    public Action<bool> ConnectionChanged { get; init; } = _ => { };
}
