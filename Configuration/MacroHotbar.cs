using System;
using System.Collections.Generic;

namespace VenuePlus.Configuration;

public sealed class MacroHotbar
{
    public string Name { get; set; } = "Bar 1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Locked { get; set; } = false;
    public bool NoBackground { get; set; } = false;
    public int Rows { get; set; } = 1;
    public int Columns { get; set; } = 8;
    public float ButtonSide { get; set; } = 70f;
    public float ItemSpacingX { get; set; } = 6f;
    public float ItemSpacingY { get; set; } = 6f;
    public float IconScaleDefault { get; set; } = 1.0f;
    public bool ShowFrameDefault { get; set; } = false;
    public uint? FrameColorDefault { get; set; } = 0xFFE20080u;
    public uint? IconColorDefault { get; set; } = null;
    public uint? HoverBackgroundColorDefault { get; set; } = null;
    public uint? BackgroundColor { get; set; } = null;
    public List<MacroHotbarSlot> Slots { get; set; } = new List<MacroHotbarSlot> { new MacroHotbarSlot() };
}
