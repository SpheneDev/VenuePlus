using System.Collections.Generic;

namespace VenuePlus.Configuration;

public sealed class MacroHotbarConfig
{
    public string Name { get; set; } = "Macro Hotbar";
    public bool ShowOnStartup { get; set; } = true;
    public bool Locked { get; set; } = false;
    public List<MacroHotbarSlot> Slots { get; set; } = new List<MacroHotbarSlot>();
}
