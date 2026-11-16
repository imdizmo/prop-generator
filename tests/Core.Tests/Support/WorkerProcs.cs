using System.Diagnostics;

namespace PropGenerator.Tests.Support;

/// <summary>Helpers for tests that start the painter's worker: paths, /proc checks.</summary>
internal static class WorkerProcs
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "painter", "worker.py")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    public static string WorkerPy() => Path.Combine(RepoRoot(), "painter", "worker.py");

    public static bool Runs(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using Process p = Process.Start(psi)!;
            if (!p.WaitForExit(30000))
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit();
                return false;
            }

            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool HasNumpy() => Runs("python3", "-c", "import numpy");

    /// <summary>True while /proc/pid exists and the process is not a zombie (a zombie holds no memory).</summary>
    public static bool Alive(int pid)
    {
        try
        {
            foreach (string line in File.ReadLines($"/proc/{pid}/status"))
            {
                if (line.StartsWith("State:", StringComparison.Ordinal))
                {
                    return !line.Contains('Z', StringComparison.Ordinal);
                }
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Every process below pid (children, their children, ...), from /proc/pid/task/*/children.</summary>
    public static List<int> Descendants(int pid)
    {
        var all = new List<int>();
        var todo = new Stack<int>();
        todo.Push(pid);
        while (todo.Count > 0)
        {
            int p = todo.Pop();
            string[] tasks;
            try
            {
                tasks = Directory.GetDirectories($"/proc/{p}/task");
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string t in tasks)
            {
                string text;
                try
                {
                    text = File.ReadAllText(Path.Combine(t, "children"));
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string c in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int child = int.Parse(c, System.Globalization.CultureInfo.InvariantCulture);
                    all.Add(child);
                    todo.Push(child);
                }
            }
        }

        return all;
    }

    public static async Task<bool> Eventually(Func<bool> condition, TimeSpan within)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > within)
            {
                return false;
            }

            await Task.Delay(25);
        }

        return true;
    }

    /// <summary>Kills (by PID, whole tree) anything a test's launcher started that is still running.</summary>
    public static void KillLeftovers(IEnumerable<int> pids)
    {
        foreach (int pid in pids)
        {
            if (!Alive(pid))
            {
                continue;
            }

            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
