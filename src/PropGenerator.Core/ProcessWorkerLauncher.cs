using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PropGenerator;

/// <summary>
/// Starts the worker as a plain child process, <c>&lt;python&gt; -I &lt;workerPy&gt;</c>. On Windows it is put in its
/// own Job Object, which caps the memory of the worker and its painting processes together (2 GB by default, as the
/// systemd scope does on Linux) and kills whatever is left in it once the worker has ended. Elsewhere (Linux without
/// a systemd user session) there is no cap; the painter's processes still end with the worker (floor_atlas.py
/// watches its parent).
/// </summary>
/// <param name="python">The Python to run (<see cref="PainterPython.Find(string)"/>).</param>
/// <param name="memoryCap">The Windows Job Object's memory limit in bytes; 2 GB if null.</param>
public sealed partial class ProcessWorkerLauncher(string python, long? memoryCap = null) : IWorkerLauncher
{
    /// <summary>The Python this launcher runs.</summary>
    public string Python { get; } = python ?? throw new ArgumentNullException(nameof(python));

    /// <summary>The memory cap (bytes) of the worker and its painting processes on Windows.</summary>
    public long MemoryCap { get; } = memoryCap ?? 2L << 30;

    /// <inheritdoc/>
    public Process Start(string workerPy)
    {
        var psi = new ProcessStartInfo(Python)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(workerPy);
        Process p = Process.Start(psi) ?? throw new InvalidOperationException($"{Python} did not start.");
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Job.Contain(p, MemoryCap);
            }
            catch
            {
                p.Kill(entireProcessTree: true);
                p.Dispose();
                throw;
            }
        }

        return p;
    }

    /// <summary>A Windows Job Object holding one worker: a memory cap on everything in it, killed when closed.</summary>
    [SupportedOSPlatform("windows")]
    private static partial class Job
    {
        private const int ExtendedLimitInformation = 9;
        private const uint LimitJobMemory = 0x200;
        private const uint LimitKillOnJobClose = 0x2000;

        /// <summary>
        /// Puts the process (and so every process it starts after) in a new job capped at <paramref name="cap"/>
        /// bytes. The job's handle is closed once the process has exited, which kills anything still in the job.
        /// The worker has not started its painting processes yet: they start on the first paint, after this.
        /// </summary>
        public static void Contain(Process p, long cap)
        {
            var job = CreateJobObjectW(0, null);
            if (job.IsInvalid)
            {
                throw new InvalidOperationException($"CreateJobObject failed ({Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                var info = new ExtendedLimit
                {
                    Basic = new BasicLimit { LimitFlags = LimitJobMemory | LimitKillOnJobClose },
                    JobMemoryLimit = (nuint)cap,
                };
                if (!SetInformationJobObject(job, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimit>()))
                {
                    throw new InvalidOperationException($"SetInformationJobObject failed ({Marshal.GetLastPInvokeError()}).");
                }

                if (!AssignProcessToJobObject(job, p.SafeHandle))
                {
                    throw new InvalidOperationException($"AssignProcessToJobObject failed ({Marshal.GetLastPInvokeError()}).");
                }
            }
            catch
            {
                job.Dispose();
                throw;
            }

            // One handle per worker, closed when it ends: nothing accumulates however often the worker restarts.
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => job.Dispose();
        }

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial JobHandle CreateJobObjectW(nint attributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(JobHandle job, int infoClass, ref ExtendedLimit info, uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimit
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimit
        {
            public BasicLimit Basic;
            public IoCounters Io;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        /// <summary>A job object's handle.</summary>
        internal sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public JobHandle()
                : base(ownsHandle: true)
            {
            }

            protected override bool ReleaseHandle() => CloseHandle(handle);
        }
    }
}
