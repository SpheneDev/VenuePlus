namespace VenuePlus.Configuration;

public sealed class CharacterProfile
{
    public string? RemoteClubId { get; set; }
    public bool AutoLoginEnabled { get; set; }
    public bool RememberStaffLogin { get; set; }
    public string? SavedStaffUsername { get; set; }
    public string? SavedStaffPasswordEnc { get; set; }
}
