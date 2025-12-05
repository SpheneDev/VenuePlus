using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace VenuePlus.Helpers;

public static class Chat
{
    private const int MaxChatBytes = 500;

    public static unsafe void SendMessageUnsafe(byte[] data)
    {
        if (data == null || data.Length == 0) throw new ArgumentException("Empty message", nameof(data));
        if (data.Length > MaxChatBytes) throw new ArgumentException("Message exceeds 500 bytes", nameof(data));

        var entry = Utf8String.FromSequence(data);
        UIModule.Instance()->ProcessChatBoxEntry(entry);
        entry->Dtor(true);
    }

    public static unsafe void SendMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Empty message", nameof(text));
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxChatBytes) throw new ArgumentException("Message exceeds 500 bytes", nameof(text));

        var sanitized = Utf8String.FromString(text);
        sanitized->SanitizeString((AllowedEntities)0x27F);
        var okLen = sanitized->ToString().Length;
        sanitized->Dtor(true);
        if (okLen != text.Length) throw new ArgumentException("Message contains invalid characters", nameof(text));

        SendMessageUnsafe(bytes);
    }
}

