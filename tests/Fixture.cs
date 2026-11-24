namespace Fixture;

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

    [DllImport("kernel32")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32")]
    private static extern uint GetConsoleOutputCP();

    [DllImport("user32")]
    private static extern bool IsWindowVisible(nint window);

    private static int Main(string[] args)
    {
        if (args[0] == "encoding")
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var encoding = System.Text.Encoding.GetEncoding((int)GetConsoleOutputCP());
            var bytes = encoding.GetBytes("Ping wird ausgeführt für google.com: äöü ÄÖÜ ß\r\n");
            Console.OpenStandardOutput().Write(bytes);
            Console.OpenStandardError().Write(bytes);
            return GetConsoleCP() == 65001 && GetConsoleOutputCP() == 65001 ? 0 : 1;
        }

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
