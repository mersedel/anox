using System.Collections.Generic;
using System.Linq;

namespace ANOX.Server;

public static class ClientManager
{
    private static readonly object _lock = new();
    
    private static Dictionary<string, Client> _allClients = new();

    /// <summary>Atomic check-and-add: returns false if the name is already taken.</summary>
    public static bool TryAddClient(string username, Client client)
    {
        lock (_lock)
        {
            return _allClients.TryAdd(username, client);
        }
    }

    public static bool ContainsClient(string username)
    {
        lock (_lock)
        {
            return _allClients.ContainsKey(username);
        }
    }

    public static Client? GetClient(string username)
    {
        lock (_lock)
        {
            _allClients.TryGetValue(username, out Client? client);
            return client;
        }
    }

    public static int ClientCount
    {
        get
        {
            lock (_lock)
            {
                return _allClients.Count;
            }
        }
    }

    public static List<string> GetAllUsernames()
    {
        lock (_lock)
        {
            return _allClients.Keys.OrderBy(name => name).ToList();
        }
    }

    public static void RemoveClient(string username)
    {
        lock (_lock)
        {
            _allClients.Remove(username);
        }
    }
}