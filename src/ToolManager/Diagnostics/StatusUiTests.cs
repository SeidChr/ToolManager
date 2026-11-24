namespace ToolManager.Diagnostics;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolManager.Execution;
using ToolManager.Models;
using ToolManager.UI;
using ToolManager.UI.Placement;

internal static class StatusUiTests
{
    public static async Task RunAsync()
    {
        var config = new Configuration { StopTimeoutSeconds = 1 };
        config.Shells.ForEach(shell => shell.LoadProfile = false);
        var tool = new ToolEntry
        {
            Name = "Status observer",
            ShellId = "powershell",
            CustomStatus = CustomStatusSource.ExplicitStatusLine,
            Interactive = true,
            Command = "[Console]::WriteLine('##TM[STATUS]:Ping 12 ms')",
        };
        config.Tools.Add(tool);
        var manager = new RunManager(config);
        var main = new MainWindow(manager, preview: true);
        try
        {
            var location = PopupPlacement.Detect(PopupPlacement.Cursor());
            var originalBounds = main.PositionPopup(location);
            var run = await manager.StartAsync(tool);
            await WaitAsync(() => run.CustomStatus == "Ping 12 ms");
            await DrainAsync(main);
            var status = StatusLabel(main);
            Require(status.Text == "Status: Ping 12 ms", "Observers replace Running with custom status.");
            var stop = Descendants(main).OfType<Button>().Single(button => button.Content as string == "Stop");
            Require(stop.IsEnabled, "Custom text does not disable Stop.");
            await manager.SendInputAsync(run, "[Console]::WriteLine('##TM[STATUS]:Stopped')");
            await WaitAsync(() => run.CustomStatus == "Stopped");
            await DrainAsync(main);
            Require(StatusLabel(main).Text == "Status: Stopped" && stop.IsEnabled, "Custom text matching a lifecycle state still has the prefix and does not affect controls.");
            await manager.SendInputAsync(run, "[Console]::WriteLine('##TM[STATUS]:')");
            await WaitAsync(() => run.CustomStatus.Length == 0);
            await DrainAsync(main);
            Require(StatusLabel(main).Text == "Running", "Clearing custom text restores the unprefixed lifecycle state.");
            await manager.SendInputAsync(run, "1..500 | ForEach-Object { [Console]::WriteLine('##TM[STATUS]:Burst ' + $_) }; [Console]::WriteLine('##TM[STATUS]:" + new string('W', 500) + "')");
            await WaitAsync(() => run.CustomStatus == new string('W', 120));
            await DrainAsync(main);
            Require(StatusLabel(main) == status && status.Text == "Status: " + new string('W', 120), "Status changes update the existing label without rebuilding the row.");
            Require(status.TextWrapping == TextWrapping.NoWrap && status.TextTrimming == TextTrimming.CharacterEllipsis, "Status uses a single line with ellipsis.");
            Require(main.PositionPopup(location).Width == originalBounds.Width, "Long custom status cannot widen the popup.");
            await manager.SendInputAsync(run, "[Console]::WriteLine('##TM[STATUS]:Ping 12 ms | Success 99.8%')");
            await WaitAsync(() => run.CustomStatus == "Ping 12 ms | Success 99.8%");
            await DrainAsync(main);
            var surface = (FrameworkElement)main.Content;
            surface.Measure(new Size(main.Width, main.Height));
            surface.Arrange(new Rect(0, 0, main.Width, main.Height));
            surface.UpdateLayout();
            await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap((int)main.Width, (int)main.Height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
            {
                drawing.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, main.Width, main.Height));
            }

            bitmap.Render(background);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "ui-check", "custom-status.png")))
            {
                encoder.Save(file);
            }

            await manager.StopAsync(tool.Id);
            await DrainAsync(main);
            Require(StatusLabel(main).Text.StartsWith("Stopping", StringComparison.Ordinal) || StatusLabel(main).Text is "Stopped" or "Force-stopped", "Stopping overrides custom text in the UI.");
            await WaitAsync(() => !run.Active);
            await DrainAsync(main);
            Require(StatusLabel(main).Text is "Stopped" or "Force-stopped", "The UI hides previous status after stopping.");

            tool.Behavior = "Custom";
            var check = new RunRecord { ToolId = tool.Id, Action = "Check", State = "Finished", ExitCode = 0, Ended = DateTime.Now, CustomStatus = "stale result" };
            manager.History.Add(check);
            manager.Config = config;
            await DrainAsync(main);
            Require(StatusLabel(main).Text == "Running", "Completed Custom Check uses its authoritative target state without stale text.");
            tool.Behavior = "Cron";
            tool.CronExpression = "0 0 1 1 *";
            await manager.StartToolAsync(tool);
            await DrainAsync(main);
            Require(StatusLabel(main).Text == "Scheduled", "Scheduled state overrides earlier script output.");

            var editor = new ToolEditor(config, new ToolEntry());
            var body = (FrameworkElement)editor.Content;
            body.Measure(new Size(editor.Width, editor.Height));
            body.Arrange(new Rect(0, 0, editor.Width, editor.Height));
            body.UpdateLayout();
            var choices = Descendants(body).OfType<ComboBox>().Single(box => box.Items.Cast<object>().OfType<KeyValuePair<CustomStatusSource, string>>().Select(choice => choice.Value).SequenceEqual(new[] { "None", "Last output line", "Explicit status line" }));
            Require(choices.SelectedValue is CustomStatusSource.None, "Custom Status defaults to None in the editor.");
            editor.Close();
        }
        finally
        {
            await manager.StopAllAsync();
            main.Hide();
        }
    }

    private static TextBlock StatusLabel(MainWindow main) => Descendants(main).OfType<TextBlock>().Single(label => AutomationProperties.GetName(label) == "Status observer status");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task DrainAsync(MainWindow main) => await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for UI status.");
            }

            await Task.Delay(25);
        }
    }
}
