using System;

namespace ANOX.Core;

public static class MessageValidator
{
    public static bool IsValid(string serializedMessage)
    {
        if (serializedMessage == null) return false;
        
        string[] parts = serializedMessage.Split('|');
        
        if (parts.Length != 4) return false;

        if (!Enum.TryParse<MessageType>(parts[0], out _)) return false;

        return true;
    }
}