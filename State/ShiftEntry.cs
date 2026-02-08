using System;

namespace VenuePlus.State
{
    public sealed class ShiftEntry
    {
        public Guid Id { get; set; }
        public Guid? EventId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? DjName { get; set; }
        public string? AssignedUid { get; set; }
        public string? Job { get; set; }
        public DateTimeOffset StartAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset EndAt { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
    }
}
