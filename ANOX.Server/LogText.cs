namespace ANOX.Server;

/// <summary>Makes client-supplied text safe to put into a log line.</summary>
public static class LogText
{
    /// <summary>
    /// Replaces control characters (ANSI escapes could rewrite the admin's terminal) and truncates,
    /// so a client can't forge log lines or fill the log with one huge entry.
    /// </summary>
    public static string Safe(string? text, int maxLength = 64)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        bool truncated = text.Length > maxLength;
        char[] chars = (truncated ? text.Substring(0, maxLength) : text).ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i])) chars[i] = '?';
        }

        return truncated ? new string(chars) + "..." : new string(chars);
    }
}
