// CI only; compiled by PowerShell Add-Type, never referenced by the product/test project.
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class OwnedTestJob
{
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    struct Limits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Accounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, Total, Active, Terminated;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Startup
    {
        public uint Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct StartupEx { public Startup Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObjectW(IntPtr security, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int kind, ref Limits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int kind, out Accounting accounting, uint size, IntPtr returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    static extern bool QueryPids(IntPtr job, int kind, IntPtr data, uint size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref IntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value,
        IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string app, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string cwd,
        ref StartupEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool member);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    public sealed class Receipt
    {
        public uint Pid, ExitCode, TotalProcesses, ResidueBeforeReap, ActiveAfterReap;
        public bool TimedOut, Reaped, RunnerNested;
        public long ElapsedMilliseconds;
        public uint[] ObservedMembers;
    }
    static void Check(bool ok, string operation)
    {
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
    static Accounting Counts(IntPtr job)
    {
        Check(QueryInformationJobObject(job, 1, out var a, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero),
            "QueryInformationJobObject");
        return a;
    }

    // Windows 10+ JOB_LIST assigns membership AT creation, before even a suspended thread exists.
    // No breakaway/silent-breakaway limits, no inherited handles, no CREATE_BREAKAWAY_FROM_JOB.
    // A runner's outer job remains intact. Incompatible nested-job/sandbox constraints BLOCK;
    // never retry outside the job or change browser flags. Descendants inherit this job even
    // when they create nested sandbox jobs. Kill-on-close covers coordinator cancellation too.
    public static Receipt Run(string exe, string encodedCommand, string cwd, int seconds, string journal)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || seconds < 2)
            throw new PlatformNotSupportedException("64-bit Windows 10+ owned test job required");
        var clock = Stopwatch.StartNew();
        var result = new Receipt();
        IntPtr job = IntPtr.Zero, attributes = IntPtr.Zero, jobValue = IntPtr.Zero;
        var process = new ProcessInfo();
        bool initialized = false;
        var members = new HashSet<uint>();
        IntPtr inventory = Marshal.AllocHGlobal(4096);
        Action<string> log = text => File.AppendAllText(journal, clock.ElapsedMilliseconds + "ms " + text + "\n");
        try
        {
            Check(IsProcessInJob(Process.GetCurrentProcess().Handle, IntPtr.Zero, out result.RunnerNested),
                "runner job query");
            job = CreateJobObjectW(IntPtr.Zero, null); // unnamed, non-inheritable, only this owner holds it
            Check(job != IntPtr.Zero, "CreateJobObject");
            var limits = new Limits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE only
            Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Limits>()), "job limits");
            IntPtr bytes = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            if (bytes == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "attribute size");
            attributes = Marshal.AllocHGlobal(bytes);
            Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes), "initialize attributes");
            initialized = true;
            jobValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobValue, job);
            Check(UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x2000D), jobValue,
                new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero), "JOB_LIST unsupported");
            var startup = new StartupEx { Attributes = attributes };
            startup.Startup.Size = (uint)Marshal.SizeOf<StartupEx>();
            var command = new StringBuilder("\"" + exe + "\" -NoLogo -NoProfile -NonInteractive -EncodedCommand " + encodedCommand);
            // EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED. Assignment failure creates no child.
            Check(CreateProcessW(exe, command, IntPtr.Zero, IntPtr.Zero, false, 0x80004,
                IntPtr.Zero, cwd, ref startup, out process), "atomic owned CreateProcess (nested job compatibility)");
            result.Pid = process.Pid;
            Check(IsProcessInJob(process.Process, job, out var member), "root membership query");
            if (!member || Counts(job).Active != 1) throw new InvalidOperationException("root membership not proven");
            log("assigned-before-execution pid=" + result.Pid + " runner-nested=" + result.RunnerNested);
            Check(ResumeThread(process.Thread) != uint.MaxValue, "ResumeThread");
            // Reserve the final second of the external bound for reaping, including post-Fact work.
            long deadline = seconds * 1000L;
            uint wait;
            do
            {
                // Bounded job-only inventory, not a PID/name sweep. Overflow blocks rather than truncates.
                Check(QueryPids(job, 3, inventory, 4096, IntPtr.Zero), "job PID inventory");
                int count = Marshal.ReadInt32(inventory, 4);
                if (count > 511) throw new InvalidOperationException("job inventory overflow");
                for (int i = 0; i < count; ++i)
                {
                    uint pid = checked((uint)Marshal.ReadInt64(inventory, 8 + i * 8));
                    if (members.Add(pid)) log("owned-member=" + pid);
                }
                wait = WaitForSingleObject(process.Process, 10);
            } while (wait == 258 && clock.ElapsedMilliseconds < deadline - 1000);
            if (wait == uint.MaxValue) Check(false, "root wait");
            result.TimedOut = wait == 258;
            if (!result.TimedOut) Check(GetExitCodeProcess(process.Process, out result.ExitCode), "exit code");
            var before = Counts(job);
            result.TotalProcesses = before.Total;
            result.ResidueBeforeReap = before.Active;
            log("root-finished timeout=" + result.TimedOut + " exit=" + result.ExitCode + " residue=" + before.Active);
            // Always reap: a completed/failed Fact does NOT imply completion of discarded InitWebAsync.
            Check(TerminateJobObject(job, 125), "terminate owned job");
            while (Counts(job).Active != 0 && clock.ElapsedMilliseconds < deadline) Thread.Sleep(10);
            result.ActiveAfterReap = Counts(job).Active;
            result.Reaped = result.ActiveAfterReap == 0 && clock.ElapsedMilliseconds <= deadline;
            result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
            result.ObservedMembers = new List<uint>(members).ToArray();
            log("reaped=" + result.Reaped + " active=" + result.ActiveAfterReap + " total=" + result.TotalProcesses);
            if (!result.Reaped) throw new InvalidOperationException("BLOCKED: job reap not proven inside external bound");
            return result;
        }
        finally
        {
            // Error paths also attempt owned reap; closing remains the last-resort kernel guarantee.
            if (job != IntPtr.Zero)
            {
                try
                {
                    if (!result.Reaped && process.Process != IntPtr.Zero)
                    {
                        Check(TerminateJobObject(job, 125), "exceptional owned termination");
                        while (Counts(job).Active != 0 && clock.ElapsedMilliseconds < seconds * 1000L)
                            Thread.Sleep(10);
                        log("exceptional-reap active=" + Counts(job).Active);
                    }
                }
                finally { Check(CloseHandle(job), "close owned job"); }
            }
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (jobValue != IntPtr.Zero) Marshal.FreeHGlobal(jobValue);
            Marshal.FreeHGlobal(inventory);
        }
    }
}
