using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VenuePlus.Helpers;

public static class ColorUtil
{
    public static uint HexToU32(string hex)
    {
        var v = HexToVec4(hex);
        return ImGui.ColorConvertFloat4ToU32(v);
    }

    public static Vector4 HexToVec4(string hex)
    {
        var s = (hex ?? string.Empty).Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length != 6) return new Vector4(1f, 1f, 1f, 1f);
        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }

    public static string Vec4ToHex(Vector4 v)
    {
        int r = Math.Clamp((int)Math.Round(v.X * 255f), 0, 255);
        int g = Math.Clamp((int)Math.Round(v.Y * 255f), 0, 255);
        int b = Math.Clamp((int)Math.Round(v.Z * 255f), 0, 255);
        return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
    }
}
