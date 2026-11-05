using System.Reflection;

namespace ANOX.Core;

public static class ProtocolInfo
{
    /// <summary>
    /// Bump whenever the wire format or message meaning changes in a way old clients/servers can't handle.
    /// The client sends it in the Register message; the server rejects other versions.
    /// </summary>
    public const int Version = 1;

    /// <summary>Application version (from Directory.Build.props), e.g. "1.0.0".</summary>
    public static string GetAppVersion(Assembly assembly) =>
        assembly.GetName().Version?.ToString(3) ?? "unknown";
}
