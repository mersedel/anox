using System.Collections.Generic;
using ANOX.Core;

namespace ANOX.Server;

public static class SessionManager
{
    private static readonly object _lock = new();
    
    private static Dictionary<string, ChatSession> _allChatSessions = new();
    
    public static void AddSession(Client clientA, Client clientB)
    {
        ChatSession session = new ChatSession(clientA, clientB);
    
        lock (_lock)
        {
            _allChatSessions.Add(clientA.Username, session);
            _allChatSessions.Add(clientB.Username, session);
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