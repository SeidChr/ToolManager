namespace ToolManager;

using System.Windows;
using ToolManager.Diagnostics;
using ToolManager.Execution;
using ToolManager.Persistence;
using ToolManager.Startup;
using ToolManager.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--startup-task")
        {
            if (args.Length != 3 || args[1] is not ("add" or "remove" or "reset"))
            {
                return 1;
            }

            try
            {
                new StartupTask(args[2]).Change(args[1]);
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, AppIdentity.Name + " startup task", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        if (args.FirstOrDefault() == "--runner")
        {
            if (args.Length != 3)
            {
                return 1;
            }

            try
            {
                return Runner.MainAsync(args[1], args[2]).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // A helper startup failure is reported by the manager, never by a desktop crash dialog.
                return 1;
            }
        }

        if (args.FirstOrDefault() == "--self-test")
        {
            return SelfTest.RunAsync().GetAwaiter().GetResult();
        }

        if (args.FirstOrDefault() == "--render-self-test")
        {
            return UiSmoke.Run();
        }

        var hidden = args.Contains("--hidden");
        using var mutex = new Mutex(true, AppIdentity.InstanceName, out var first);
        if (!first)
        {
            if (!hidden)
            {
                MessageBox.Show(AppIdentity.Name + " is already running. Open it from the notification area.", AppIdentity.Name);
            }

            return 0;
        }

        // .NET 10 Fluent theme selection is still marked experimental by WPF.
#pragma warning disable WPF0001
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            ThemeMode = ThemeMode.Light,
        };
#pragma warning restore WPF0001
        try
        {
            var manager = new RunManager(Store.Load());
            var window = new MainWindow(manager);
            app.MainWindow = window;
            app.DispatcherUnhandledException += (_, e) =>
            {
                MessageBox.Show(e.Exception.Message, AppIdentity.Name + " error");
                e.Handled = true;
            };
            app.Startup += async (_, _) =>
            {
                if (!hidden)
                {
                    window.ShowPopup();
                }

                await manager.AutostartAsync(args.Contains("--autostart"));
            };
            app.Run();
            return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message + "\n\nConfiguration: " + Store.ConfigPath, AppIdentity.Name + " could not start");
            return 1;
        }
    }
}
