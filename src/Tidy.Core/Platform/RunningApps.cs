using System.Diagnostics;

namespace Tidy.Core.Platform;

public static class RunningApps
{
    /// <summary>Whether any process with one of these names (no <c>.exe</c>) is running in this session.</summary>
    public static bool AnyRunning(IEnumerable<string> processNames)
    {
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var name in processNames)
        {
            var found = Process.GetProcessesByName(name);
            try
            {
                if (found.Any(p => p.SessionId == session)) return true;
            }
            finally
            {
                foreach (var p in found) p.Dispose();
            }
        }
        return false;
    }
}
