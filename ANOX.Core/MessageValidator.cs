using System;

namespace ANOX.Core;

public static class MessageValidator
{
    public const int MaxUsernameLength = 20;

    private static readonly string[] _reservedNames = { "SERVER", "ALL" };

    public static bool IsValid(string serializedMessage)
    {
        if (serializedMessage == null) return false;

        string[] parts = serializedMessage.Split('|');

        if (parts.Length != 4) return false;

        // Exact names only: Enum.TryParse also accepts numbers ("999") and flag lists ("Ping, Pong").
        if (!Enum.IsDefined(typeof(MessageType), parts[0])) return false;

        // The text is base64; reject garbage here instead of crashing in the parser.
        if (!Convert.TryFromBase64String(parts[3], new byte[parts[3].Length], out _)) return false;

        return true;
    }

    /// <summary>
    /// Usernames go into the protocol unencoded, so they must not contain '|' or line breaks.
    /// Allowed: 1-20 letters, digits, '_', '-', '.'; "SERVER" and "ALL" are reserved.
    /// </summary>
    public static bool IsValidUsername(string? username)
    {
        if (string.IsNullOrEmpty(username) || username.Length > MaxUsernameLength) return false;

        foreach (char c in username)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.') return false;
        }

        foreach (string reserved in _reservedNames)
        {
            if (string.Equals(username, reserved, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
