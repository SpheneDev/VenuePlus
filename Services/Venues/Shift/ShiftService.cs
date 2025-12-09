using System;
using System.Linq;
using VenuePlus.State;

namespace VenuePlus.Services;

internal sealed class ShiftService
{
    private ShiftEntry[] _entries = Array.Empty<ShiftEntry>();

    public ShiftEntry[] GetAll()
    {
        return _entries;
    }

    public void ReplaceAll(ShiftEntry[]? entries)
    {
        _entries = (entries ?? Array.Empty<ShiftEntry>()).OrderBy(e => e.StartAt).ToArray();
    }

    public void AddOrUpdate(ShiftEntry entry)
    {
        var list = new System.Collections.Generic.List<ShiftEntry>(_entries);
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
        _entries = Array.Empty<ShiftEntry>();
    }
}

