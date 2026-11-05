using System.Collections.Generic;
using ANOX.Core;

namespace ANOX.Server;

public static class SessionManager
{
    private static readonly object _lock = new();

    // Each session is stored under both participants' names.
    private static Dictionary<string, ChatSession> _allChatSessions = new();

    public static bool TryAddSession(Client clientA, Client clientB, out MessageErrorType error)
    {
        if (ReferenceEquals(clientA, clientB) || clientA.Username == clientB.Username)
        {
            error = MessageErrorType.CannotConnectToSelf;
            return false;
        }

        // Checks and adds happen under one lock so two simultaneous /connect requests can't both pass.
        lock (_lock)
        {
            if (!ClientManager.ContainsClient(clientA.Username) || !ClientManager.ContainsClient(clientB.Username))
            {
                error = MessageErrorType.ReceiverNotFound;
                return false;
            }

            if (_allChatSessions.ContainsKey(clientA.Username) || _allChatSessions.ContainsKey(clientB.Username))
            {
                error = MessageErrorType.AlreadyInSession;
                return false;
            }

            ChatSession session = new ChatSession(clientA, clientB);

            _allChatSessions.Add(clientA.Username, session);
            _allChatSessions.Add(clientB.Username, session);

            error = default;
            return true;
        }
    }

    public static int SessionCount
    {
        get
        {
            lock (_lock)
            {
                return _allChatSessions.Count / 2;
            }
        }
    }

    public static bool ContainsSession(string username)
    {
        lock (_lock)
        {
            return _allChatSessions.ContainsKey(username);
        }
    }

    /// <returns>The user's session, or null if they are not in one.</returns>
    public static ChatSession? GetSession(string username)
    {
        lock (_lock)
        {
            _allChatSessions.TryGetValue(username, out ChatSession? session);
            return session;
        }
    }

    /// <returns>The removed session, or null if the user was not in one.</returns>
    public static ChatSession? RemoveSession(string username)
    {
        lock (_lock)
        {
            if (!_allChatSessions.TryGetValue(username, out ChatSession? session)) return null;

            _allChatSessions.Remove(session.ClientA.Username);
            _allChatSessions.Remove(session.ClientB.Username);

            return session;
        }
    }
}
