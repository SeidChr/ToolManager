namespace ToolManager;

using System.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--startup-task")
        {
            if (args.Length != 3 || args[1] is not ("add" or "remove"))
            {
                return 1;
            }

            try
            {
                new StartupTask(args[2]).Change(args[1] == "add");
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Startup task", MessageBoxButton.OK, MessageBoxImage.Error);
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

        using var mutex = new Mutex(true, "Local\\PortableToolManager-" + Environment.UserName, out var first);
        if (!first)
        {
            MessageBox.Show("Tool Manager is already running. Open it from the notification area.", "Tool Manager");
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
            app.DispatcherUnhandledException += (_, e) =>
            {
                MessageBox.Show(e.Exception.Message, "Tool Manager error");
                e.Handled = true;
            };
            app.Run(window);
            return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message + "\n\nConfiguration: " + Store.ConfigPath, "Tool Manager could not start");
            return 1;
        }
    }
}
