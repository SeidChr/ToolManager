namespace ToolManager;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Xml.Linq;

internal sealed class StartupTask
{
    private readonly string userSid;
    private readonly string executable;

    public StartupTask(string? userSid = null, string? executable = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        this.userSid = new SecurityIdentifier(userSid ?? identity.User!.Value).Value;
        this.executable = Path.GetFullPath(executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running application."));
    }

    public string Name => "PortableToolManager-" + this.userSid + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.executable.ToUpperInvariant())))[..16];

    public bool Exists()
    {
        return this.WithFolder(folder =>
        {
            try
            {
                object task = folder.GetTask(this.Name);
                Marshal.FinalReleaseComObject(task);
                return true;
            }
            catch (Exception e) when (e.HResult == unchecked((int)0x80070002))
            {
                return false;
            }
        });
    }

    public async Task ChangeAsync(bool add)
    {
        try
        {
            await Task.Run(() => this.Change(add));
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005))
        {
            var start = new ProcessStartInfo(this.executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(this.executable),
            };
            start.ArgumentList.Add("--startup-task");
            start.ArgumentList.Add(add ? "add" : "remove");
            start.ArgumentList.Add(this.userSid);
            using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start the administrator helper.");
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0)
            {
                throw new InvalidOperationException("The startup task could not be changed. See the administrator helper's error message.");
            }
        }
    }

    public void Change(bool add)
    {
        this.WithFolder(folder =>
        {
            if (add)
            {
                // TASK_CREATE prevents replacing an existing task, even after a stale UI check.
                object task = folder.RegisterTask(this.Name, this.Definition(), 2, this.userSid, null, 3, null);
                Marshal.FinalReleaseComObject(task);
            }
            else
            {
                try
                {
                    folder.DeleteTask(this.Name, 0);
                }
                catch (Exception e) when (e.HResult == unchecked((int)0x80070002))
                {
                    // A task removed outside Settings is already in the requested state.
                }
            }

            return true;
        });
    }

    internal string Definition()
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name, object content) => new(ns + name, content);
        return new XElement(
            ns + "Task",
            new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Description", "Start this portable Tool Manager when its user signs in.")),
            Element("Triggers", Element("LogonTrigger", Element("UserId", this.userSid))),
            Element("Principals", new XElement(
                ns + "Principal",
                new XAttribute("id", "User"),
                Element("UserId", this.userSid),
                Element("LogonType", "InteractiveToken"),
                Element("RunLevel", "LeastPrivilege"))),
            Element("Settings", new object[]
            {
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", false),
                Element("StopIfGoingOnBatteries", false),
                Element("ExecutionTimeLimit", "PT0S"),
            }),
            new XElement(
                ns + "Actions",
                new XAttribute("Context", "User"),
                Element("Exec", new object[]
                {
                    Element("Command", this.executable),
                    Element("WorkingDirectory", Path.GetDirectoryName(this.executable)!),
                }))).ToString();
    }

    private T WithFolder<T>(Func<dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? folder = null;
        try
        {
            service.Connect();
            folder = service.GetFolder("\\");
            return action(folder);
        }
        finally
        {
            if (folder is not null)
            {
                Marshal.FinalReleaseComObject(folder);
            }

            Marshal.FinalReleaseComObject(service);
        }
    }
}
