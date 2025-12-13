namespace VenuePlus.Configuration;

  public sealed class MacroHotbarSlot
  {
     public string PresetName { get; set; } = string.Empty;
     public ChatChannel Channel { get; set; } = ChatChannel.Whisper;
     public string IconKey { get; set; } = string.Empty;
     public bool UseGameIcon { get; set; } = false;
     public int GameIconId { get; set; } = 0;
     public bool NoBackground { get; set; } = false;
     public string? ToolTipText { get; set; } = null;
     public float IconScale { get; set; } = 1.0f;
     public float IconOffsetX { get; set; } = 0.0f;
     public float IconOffsetY { get; set; } = 0.0f;
     public float IconZoomOffsetX { get; set; } = 0.0f;
     public float IconZoomOffsetY { get; set; } = 0.0f;
     public bool ShowFrame { get; set; } = false;
     public uint? FrameColor { get; set; } = null;
     public uint? IconColor { get; set; } = null;
     public uint? HoverBackgroundColor { get; set; } = null;
 }
