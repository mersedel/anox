using System.Collections.Generic;
using System.Linq;

namespace ANOX.Server;

public static class ClientManager
{
    private static readonly object _lock = new();
    
    private static Dictionary<string, ClientSession> _allClients = new();

    public static void AddClient(string username, ClientSession session)
    {
        lock (_lock)
        {
            _allClients.Add(username, session);
        }
    }

    public static ClientSession GetClientSession(string username)
    {
        lock (_lock)
        {
            return _allClients[username];
        }
    }

    public static int Broadcast(string message, string senderUsername)
    {
        List<ClientSession> targets;
    
        lock (_lock)
        {
            targets = _allClients.Values
                .Where(s => s.Username != senderUsername)
                .ToList();
        }
    
        foreach (var session in targets)
        {
            session.Send(message);
        }
    
        return targets.Count;
    }

    public static void RemoveClient(string username)
    {
        lock (_lock)
        {
            _allClients.Remove(username);
        }
    }
}