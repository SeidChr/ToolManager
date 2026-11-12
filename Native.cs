namespace ToolManager;

using System.Runtime.InteropServices;

internal static class Native
{
    internal const uint HandleFlagInherit = 1;
    internal const uint CreateSuspended = 4;
    internal const uint KillOnJobClose = 0x2000;
    internal const uint ForcedExitCode = 137;
    internal const int UseStandardHandles = 0x100;
    internal const int ExtendedLimitInformation = 9;
    internal const int BasicAccountingInformation = 1;

    internal delegate bool Handler(uint signal);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool AllocConsole();

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool SetConsoleCtrlHandler(Handler handler, bool add);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool CreatePipe(out nint read, out nint write, ref Security security, int size);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string app, StringBuilder command, nint pa, nint ta, bool inherit, uint flags, nint environment, string directory, ref Startup startup, out ProcessInfo info);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern nint CreateJobObject(nint security, string? name);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimits info, int length);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool QueryInformationJobObject(nint job, int infoClass, out Accounting info, int length, nint returned);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool TerminateJobObject(nint job, uint code);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool TerminateProcess(nint process, uint code);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern uint ResumeThread(nint thread);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(nint process, out uint code);

    [DllImport("kernel32")]
    internal static extern bool CloseHandle(nint handle);

    internal static void Check(bool ok)
    {
        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Startup
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int CharsX;
        public int CharsY;
        public int Fill;
        public int Flags;
        public short Show;
        public short ReservedSize;
        public nint ReservedPtr;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Security
    {
        public int Length;
        public nint Descriptor;
        public int Inherit;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicLimits
    {
        public long ProcessTime;
        public long JobTime;
        public uint Flags;
        public nuint Min;
        public nuint Max;
        public uint Active;
        public nuint Affinity;
        public uint Priority;
        public uint Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemory;
        public nuint JobMemory;
        public nuint PeakProcessMemory;
        public nuint PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Accounting
    {
        public long User;
        public long Kernel;
        public long PeriodUser;
        public long PeriodKernel;
        public uint Faults;
        public uint Total;
        public uint Active;
        public uint Terminated;
    }
}
