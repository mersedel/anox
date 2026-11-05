using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ANOX.Server;

/// <summary>
/// Pending chat requests ("from" wants to chat with "to").
/// Each user can have one outgoing request; a new /connect replaces the old one.
/// Requests expire after <see cref="Timeout"/>.
/// </summary>
public static class InviteManager
{
    public record Invite(string From, string To);

    // Defaults; overwritten from server.json by Configure().
    public static TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(60);

    private static readonly object _lock = new();

    // Keyed by the requester's name.
    private static readonly Dictionary<string, (Invite Invite, Timer Timer)> _invitesByFrom = new();

    // Anti-spam: a pause before asking the same user again, and a cap on requests per window.
    public static TimeSpan SameUserCooldown { get; private set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan RateWindow { get; private set; } = TimeSpan.FromSeconds(60);
    public static int MaxRequestsPerWindow { get; private set; } = 5;

    public static void Configure(ServerConfig config)
    {
        Timeout = TimeSpan.FromSeconds(config.InviteTimeoutSeconds);
        SameUserCooldown = TimeSpan.FromSeconds(config.InviteSameUserCooldownSeconds);
        RateWindow = TimeSpan.FromSeconds(config.InviteRateWindowSeconds);
        MaxRequestsPerWindow = config.MaxInvitesPerWindow;
    }

    // Send times per requester (kept after disconnect, so reconnecting doesn't reset the limit).
    private static readonly Dictionary<string, List<(DateTime At, string To)>> _sendHistory = new();

    /// <summary>
    /// Checks the anti-spam limits and, if allowed, records the attempt.
    /// </summary>
    /// <param name="retryAfter">How long to wait when the request is not allowed.</param>
    public static bool TryRegisterAttempt(string from, string to, out TimeSpan retryAfter)
    {
        DateTime now = DateTime.UtcNow;

        lock (_lock)
        {
            if (!_sendHistory.TryGetValue(from, out var history))
            {
                history = new List<(DateTime At, string To)>();
                _sendHistory[from] = history;
            }

            history.RemoveAll(h => now - h.At >= RateWindow);

            DateTime? lastToSameUser = history.Where(h => h.To == to).Select(h => (DateTime?)h.At).LastOrDefault();

            if (lastToSameUser != null && now - lastToSameUser.Value < SameUserCooldown)
            {
                retryAfter = SameUserCooldown - (now - lastToSameUser.Value);
                return false;
            }

            if (history.Count >= MaxRequestsPerWindow)
            {
                retryAfter = RateWindow - (now - history[0].At);
                return false;
            }

            history.Add((now, to));
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>Raised (outside the lock) when a request times out.</summary>
    public static event Action<Invite>? Expired;

    /// <returns>The request this one replaced, if "from" already had a pending one.</returns>
    public static Invite? Create(string from, string to)
    {
        Invite invite = new Invite(from, to);
        Invite? replaced = null;

        lock (_lock)
        {
            if (_invitesByFrom.Remove(from, out var old))
            {
                old.Timer.Dispose();
                replaced = old.Invite;
            }

            Timer timer = new Timer(_ => OnTimeout(invite), null, Timeout, System.Threading.Timeout.InfiniteTimeSpan);
            _invitesByFrom[from] = (invite, timer);
        }

        return replaced;
    }

    public static bool Exists(string from, string to)
    {
        lock (_lock)
        {
            return _invitesByFrom.TryGetValue(from, out var entry) && entry.Invite.To == to;
        }
    }

    /// <summary>Removes the request "from" -> "to". Returns false if there was none.</summary>
    public static bool TryRemove(string from, string to)
    {
        lock (_lock)
        {
            if (!_invitesByFrom.TryGetValue(from, out var entry) || entry.Invite.To != to) return false;

            entry.Timer.Dispose();
            _invitesByFrom.Remove(from);
            return true;
        }
    }

    /// <summary>Removes every request the user sent or received (on disconnect or when they start a session).</summary>
    public static List<Invite> RemoveAllFor(string username)
    {
        lock (_lock)
        {
            List<Invite> removed = _invitesByFrom.Values
                .Where(e => e.Invite.From == username || e.Invite.To == username)
                .Select(e => e.Invite)
                .ToList();

            foreach (Invite invite in removed)
            {
                _invitesByFrom.Remove(invite.From, out var entry);
                entry.Timer.Dispose();
            }

            return removed;
        }
    }

    private static void OnTimeout(Invite invite)
    {
        // The request may have been answered or replaced in the meantime (compare by reference:
        // a re-sent request to the same user is a new Invite with its own timer).
        lock (_lock)
        {
            if (!_invitesByFrom.TryGetValue(invite.From, out var entry) || !ReferenceEquals(entry.Invite, invite)) return;

            entry.Timer.Dispose();
            _invitesByFrom.Remove(invite.From);
        }

        Expired?.Invoke(invite);
    }
}
