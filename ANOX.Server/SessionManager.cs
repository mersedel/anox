using System.Collections.Generic;
using ANOX.Core;

namespace ANOX.Server;

public static class SessionManager
{
    private static readonly object _lock = new();
    
    private static Dictionary<string, ChatSession> _allChatSessions = new();
    
    public static bool TryAddSession(Client clientA, Client clientB, out string errorReason)
    {
        if (!ClientManager.ContainsClient(clientA.Username) || !ClientManager.ContainsClient(clientB.Username))
        {
            errorReason = "Receiver not found!";
            return false;
        }

        if (ContainsSession(clientA.Username) || ContainsSession(clientB.Username))
        {
            errorReason = "User already in session!";
            return false;
        }
        
        ChatSession session = new ChatSession(clientA, clientB);
    
        lock (_lock)
        {
            _allChatSessions.Add(clientA.Username, session);
            _allChatSessions.Add(clientB.Username, session);

            errorReason = string.Empty;
            return true;
        }
    }
    
    public static bool ContainsSession(string username)
    {
        lock (_lock)
        {
            return _allChatSessions.ContainsKey(username);
        }
    }
    
    public static ChatSession GetSession(string username)
    {
        lock (_lock)
        {
            return _allChatSessions[username];
        }
    }
    
    public static void RemoveSession(string username)
    {
        lock (_lock)
        {
            if (_allChatSessions.TryGetValue(username, out ChatSession session))
            {
                _allChatSessions.Remove(session.ClientA.Username);
                _allChatSessions.Remove(session.ClientB.Username);
            }
        }
    }
}