using System;
using System.Collections.Generic;
using System.Linq;

namespace ANOX.Server;

public static class ClientManager
{
    private static readonly object _lock = new();
    
    // Case-insensitive: "Bob" and "bob" can't be online at the same time and impersonate each other.
    private static readonly Dictionary<string, Client> _allClients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Atomic check-and-add: returns false if the name is already taken.</summary>
    public static bool TryAddClient(string username, Client client)
    {
        lock (_lock)
        {
            return _allClients.TryAdd(username, client);
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

    /// <summary>True if this exact client is the one registered under its name.</summary>
    public static bool IsCurrent(Client client)
    {
        lock (_lock)
        {
            return _allClients.TryGetValue(client.Username, out Client? current) && ReferenceEquals(current, client);
        }
    }

    /// <summary>Removes the entry only if it still belongs to <paramref name="client"/>.</summary>
    public static void RemoveClient(string username, Client client)
    {
        lock (_lock)
        {
            if (_allClients.TryGetValue(username, out Client? current) && ReferenceEquals(current, client))
            {
                _allClients.Remove(username);
            }
        }
    }
}