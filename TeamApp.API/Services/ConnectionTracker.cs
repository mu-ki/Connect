using System.Collections.Concurrent;

namespace TeamApp.API.Services;

public class ConnectionTracker
{
    // Maps AdUpn -> Hashset of ConnectionIds (one user can have multiple concurrent connections/tabs open)
    private readonly ConcurrentDictionary<string, HashSet<string>> _onlineUsers = new(StringComparer.OrdinalIgnoreCase);

    public bool UserConnected(string userUpn, string connectionId)
    {
        bool isFirstConnection = false;
        
        _onlineUsers.AddOrUpdate(userUpn, 
            _ => 
            {
                isFirstConnection = true;
                return new HashSet<string> { connectionId };
            },
            (_, connections) =>
            {
                lock(connections)
                {
                    connections.Add(connectionId);
                }
                return connections;
            });

        return isFirstConnection;
    }

    public bool UserDisconnected(string userUpn, string connectionId)
    {
        bool isOffline = false;
        
        if (_onlineUsers.TryGetValue(userUpn, out var connections))
        {
            lock(connections)
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    isOffline = true;
                }
            }

            if (isOffline)
            {
                _onlineUsers.TryRemove(userUpn, out _);
            }
        }

        return isOffline;
    }

    public bool IsUserOnline(string userUpn)
    {
        return _onlineUsers.ContainsKey(userUpn);
    }

    public IEnumerable<string> GetAllOnlineUsers()
    {
        return _onlineUsers.Keys;
    }
}
