namespace ToolManager;

using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal sealed class ConsoleSession : IDisposable
{
    // A custom handler is not inherited by children. Each shell retains normal Ctrl+C behavior.
    private static readonly Native.Handler IgnoreCtrl = _ => true;

    private nint job;
    private nint process;

    public ConsoleSession(RunRequest request)
    {
        Native.Check(Native.AllocConsole());
        Native.Check(Native.SetConsoleCtrlHandler(IgnoreCtrl, true));
        this.job = Native.CreateJobObject(nint.Zero, null);
        Native.Check(this.job != nint.Zero);
        nint outRead = nint.Zero, outWrite = nint.Zero, errRead = nint.Zero, errWrite = nint.Zero, inRead = nint.Zero, inWrite = nint.Zero;
        Native.ProcessInfo pi = default;
        try
        {
            var limits = new Native.ExtendedLimits { Basic = new Native.BasicLimits { Flags = Native.KillOnJobClose } };
            Native.Check(Native.SetInformationJobObject(this.job, Native.ExtendedLimitInformation, ref limits, Marshal.SizeOf<Native.ExtendedLimits>()));
            var sa = new Native.Security
            {
                Length = Marshal.SizeOf<Native.Security>(),
                Inherit = 1,
            };
            Native.Check(Native.CreatePipe(out outRead, out outWrite, ref sa, 0));
            Native.Check(Native.CreatePipe(out errRead, out errWrite, ref sa, 0));
            Native.Check(Native.CreatePipe(out inRead, out inWrite, ref sa, 0));
            Native.Check(Native.SetHandleInformation(outRead, Native.HandleFlagInherit, 0));
            Native.Check(Native.SetHandleInformation(errRead, Native.HandleFlagInherit, 0));
            Native.Check(Native.SetHandleInformation(inWrite, Native.HandleFlagInherit, 0));
            var startup = new Native.Startup
            {
                Size = Marshal.SizeOf<Native.Startup>(),
                Flags = Native.UseStandardHandles,
                StandardInput = inRead,
                StandardOutput = outWrite,
                StandardError = errWrite,
            };
            Environment.SetEnvironmentVariable("TOOLMANAGER_ROOT", Store.Root);
            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            Native.Check(Native.CreateProcess(request.Shell, new StringBuilder(request.CommandLine), nint.Zero, nint.Zero, true, Native.CreateSuspended, nint.Zero, request.Directory, ref startup, out pi));
            this.process = pi.Process;
            Native.Check(Native.AssignProcessToJobObject(this.job, this.process));
            if (Native.ResumeThread(pi.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            this.Output = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, new SafePipeHandle(outRead, true)), Encoding.UTF8, true);
            outRead = nint.Zero;
            this.Error = new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, new SafePipeHandle(errRead, true)), Encoding.UTF8, true);
            errRead = nint.Zero;
            if (request.Interactive)
            {
                this.Input = new StreamWriter(new AnonymousPipeClientStream(PipeDirection.Out, new SafePipeHandle(inWrite, true)), new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\r\n",
                };
                inWrite = nint.Zero;
            }
        }
        catch
        {
            if (this.process != nint.Zero)
            {
                Native.TerminateProcess(this.process, 1);
            }

            this.Dispose();
            throw;
        }
        finally
        {
            foreach (var h in new[] { outRead, outWrite, errRead, errWrite, inRead, inWrite, pi.Thread })
            {
                if (h != nint.Zero)
                {
                    Native.CloseHandle(h);
                }
            }
        }
    }

    public StreamReader Output
    {
        get;
    }

    public StreamReader Error
    {
        get;
    }

    public StreamWriter? Input
    {
        get;
    }

    public bool Active
    {
        get
        {
            Native.Check(Native.QueryInformationJobObject(this.job, Native.BasicAccountingInformation, out var stats, Marshal.SizeOf<Native.Accounting>(), nint.Zero));
            return stats.Active != 0;
        }
    }

    public int ExitCode
    {
        get
        {
            Native.Check(Native.GetExitCodeProcess(this.process, out var result));
            return unchecked((int)result);
        }
    }

    public void Interrupt() => Native.Check(Native.GenerateConsoleCtrlEvent(0, 0));

    public void Kill() => Native.Check(Native.TerminateJobObject(this.job, Native.ForcedExitCode));

    public void Dispose()
    {
        // Closing the owning job first releases blocked pipe operations and makes repeated disposal safe.
        var ownedJob = Interlocked.Exchange(ref this.job, nint.Zero);
        if (ownedJob != nint.Zero)
        {
            Native.CloseHandle(ownedJob);
        }

        var ownedProcess = Interlocked.Exchange(ref this.process, nint.Zero);
        if (ownedProcess != nint.Zero)
        {
            Native.CloseHandle(ownedProcess);
        }

        try
        {
            this.Input?.Dispose();
        }
        catch (IOException)
        {
            // The tool can close stdin before the run is disposed.
        }

        this.Output?.Dispose();
        this.Error?.Dispose();
    }
}
