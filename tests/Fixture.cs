using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Fixture
{
    private static Handler? handler;

    private delegate bool Handler(uint signal);

    [DllImport("kernel32")]
    private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);

    [DllImport("kernel32")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32")]
    private static extern bool IsWindowVisible(nint window);

    private static int Main(string[] args)
    {
        string mode = args[0], marker = args[1];
        handler = signal =>
        {
            File.WriteAllText(marker, "Ctrl+C:" + signal);
            if (mode == "cooperate")
            {
                Environment.Exit(0);
            }

            return true;
        };
        SetConsoleCtrlHandler(handler, true);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var window = GetConsoleWindow();
        Console.WriteLine("Console hidden: " + (window != nint.Zero && !IsWindowVisible(window)));
        Console.WriteLine("Fixture ready " + Environment.ProcessId);
        if (mode == "child")
        {
            var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "ignore", marker + ".child" }, UseShellExecute = false });
            File.WriteAllText(marker + ".pid", p!.Id.ToString());
        }

        while (true)
        {
            Thread.Sleep(50);
        }
    }
}
