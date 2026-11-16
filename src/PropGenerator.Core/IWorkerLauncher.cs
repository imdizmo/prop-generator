using System.Diagnostics;

namespace PropGenerator;

/// <summary>Starts the painter's worker process.</summary>
public interface IWorkerLauncher
{
    /// <summary>
    /// Starts the worker with stdin, stdout and stderr redirected. The returned process belongs to the caller, who
    /// stops it and disposes it.
    /// </summary>
    /// <param name="workerPy">The full path of painter/worker.py.</param>
    /// <returns>The started process. Its PID must be the worker's own (or a process whose death ends the worker).</returns>
    Process Start(string workerPy);
}
