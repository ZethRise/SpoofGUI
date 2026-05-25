using System.Diagnostics;

namespace SpoofGUI.Core;

internal static class ProxyPortKiller
{
    public static void KillOwners(params int[] ports)
    {
        var wanted = ports.ToHashSet();
        var owners = FindOwners(wanted);
        if (owners.Count == 0)
        {
            AppLog.Info($"proxy port cleanup: no owners on {string.Join(", ", wanted.Order())}");
            return;
        }

        foreach (var (pid, ownedPorts) in owners.OrderBy(x => x.Key))
        {
            if (pid <= 0 || pid == Environment.ProcessId) continue;

            try
            {
                using var proc = Process.GetProcessById(pid);
                var name = proc.ProcessName;
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
                AppLog.Warn($"proxy port cleanup: killed {name} pid={pid} ports={string.Join(",", ownedPorts.Order())}");
            }
            catch (Exception e)
            {
                AppLog.Warn($"proxy port cleanup: failed pid={pid} ports={string.Join(",", ownedPorts.Order())}: {e.Message}");
            }
        }
    }

    private static Dictionary<int, HashSet<int>> FindOwners(HashSet<int> ports)
    {
        var owners = new Dictionary<int, HashSet<int>>();
        var psi = new ProcessStartInfo("netstat.exe", "-ano")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return owners;

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 4) continue;
            if (!cols[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)
                && !cols[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isTcp = cols[0].Equals("TCP", StringComparison.OrdinalIgnoreCase);
            if (isTcp && (cols.Length < 5 || !cols[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var local = cols[1];
            if (!TryGetLocalPort(local, out var port) || !ports.Contains(port)) continue;
            if (!int.TryParse(cols[^1], out var pid)) continue;

            if (!owners.TryGetValue(pid, out var set))
            {
                set = new HashSet<int>();
                owners[pid] = set;
            }
            set.Add(port);
        }

        return owners;
    }

    private static bool TryGetLocalPort(string endpoint, out int port)
    {
        port = 0;
        var idx = endpoint.LastIndexOf(':');
        if (idx < 0 || idx + 1 >= endpoint.Length) return false;
        return int.TryParse(endpoint[(idx + 1)..].TrimEnd(']'), out port);
    }
}
