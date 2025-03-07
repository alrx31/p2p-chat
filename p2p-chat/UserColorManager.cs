using System.Collections.Concurrent;

namespace p2p_chat;

public static class UserColorManager
{
    private static readonly ConsoleColor[] _availableColors = 
    {
        ConsoleColor.Blue,
        ConsoleColor.Green,
        ConsoleColor.Magenta,
        ConsoleColor.Yellow,
        ConsoleColor.Cyan,
        ConsoleColor.Red,
        ConsoleColor.DarkYellow,
        ConsoleColor.DarkCyan
    };

    private static readonly ConcurrentDictionary<string, ConsoleColor> _userColors = new();

    public static ConsoleColor GetColorForUser(string username)
    {
        username = username.Split(' ')[1];
        //Console.WriteLine(username);
        username = username.Substring(1, username.Length-2);
        //Console.WriteLine(username);
        
        return _userColors.GetOrAdd(username, key => 
        {
            int hash = Math.Abs(key.GetHashCode());
            return _availableColors[hash % _availableColors.Length];
        });
    }

    public static void ResetColorForUser(string username)
    {
        _userColors.TryRemove(username, out _);
    }
}
