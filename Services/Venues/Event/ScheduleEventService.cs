using System;
using System.Linq;
using VenuePlus.State;

namespace VenuePlus.Services;

internal sealed class ScheduleEventService
{
    private EventEntry[] _entries = Array.Empty<EventEntry>();

    public EventEntry[] GetAll()
    {
        return _entries;
    }

    public void ReplaceAll(EventEntry[]? entries)
    {
        _entries = (entries ?? Array.Empty<EventEntry>()).OrderBy(e => e.StartAt).ToArray();
    }

    public void AddOrUpdate(EventEntry entry)
    {
        var list = new System.Collections.Generic.List<EventEntry>(_entries);
        var idx = list.FindIndex(x => x.Id == entry.Id);
        if (idx >= 0) list[idx] = entry; else list.Add(entry);
        _entries = list.OrderBy(x => x.StartAt).ToArray();
    }

    public void Remove(Guid id)
    {
        _entries = _entries.Where(x => x.Id != id).OrderBy(x => x.StartAt).ToArray();
    }

    public void Clear()
    {
        _entries = Array.Empty<EventEntry>();
    }
}
