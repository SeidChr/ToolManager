namespace ToolManager.Diagnostics;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolManager.Execution;
using ToolManager.Models;
using ToolManager.Persistence;
using ToolManager.Startup;
using ToolManager.UI;
using ToolManager.UI.Placement;

internal static class UiSmoke
{
    public static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ui-check");
        Directory.CreateDirectory(folder);
        Store.Root = folder;
#pragma warning disable WPF0001
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            ThemeMode = ThemeMode.Light,
        };
#pragma warning restore WPF0001
        var config = new Configuration();
        var tool = new ToolEntry
        {
            Name = "Report exporter",
            Autostart = true,
            Command = "Write-Output 'Report completed'",
        };
        config.Tools.Add(tool);
        config.Tools.Add(new ToolEntry
        {
            Name = "File watcher",
            Command = "while ($true) { Start-Sleep 1 }",
            Mode = "Bundled",
        });
        var external = new ToolEntry
        {
            Name = "External helper",
            Behavior = "Custom",
            Command = "Write-Output started",
            StopCommand = "Write-Output stopped",
            StatusCommand = "exit 0",
        };
        config.Tools.Add(external);
        var manager = new RunManager(config);
        manager.History.Add(new RunRecord
        {
            ToolId = external.Id,
            Action = "Check",
            Ended = DateTime.Now,
            State = "Finished",
            ExitCode = 0,
        });
        var sample = new RunRecord
        {
            ToolId = tool.Id,
            Ended = DateTime.Now,
            State = "Finished",
            ExitCode = 0,
        };
        sample.Append("Report completed successfully.\n", 1024);
        manager.History.Add(sample);
        int result = 0;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                async Task Capture(Window window, string name)
                {
                    var surface = (FrameworkElement)window.Content;
                    var size = new Size(window.Width, window.Height);
                    surface.Measure(size);
                    surface.Arrange(new Rect(size));
                    surface.UpdateLayout();
                    await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    var background = new DrawingVisual();
                    using (var drawing = background.RenderOpen())
                    {
                        drawing.DrawRectangle(SystemColors.WindowBrush, null, new Rect(size));
                    }

                    bitmap.Render(background);
                    bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, name + ".png"));
                    encoder.Save(file);
                }

                var checks = PlacementTests.Run();
                var icons = new IconBitmapDecoder(new Uri(Ui.IconPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
                {
                    if (!icons.Frames.Any(frame => frame.PixelWidth == size && frame.PixelHeight == size))
                    {
                        throw new Exception("Missing icon resolution: " + size);
                    }
                }

                using (var trayIcon = Ui.TrayIcon())
                {
                    if (trayIcon.Width != 32)
                    {
                        throw new Exception("Tray icon did not load at native size.");
                    }
                }

                checks.Add("PASS Embedded app icon includes all nine sizes from 16 to 256px; native tray icon loads at 32px.");
                var main = new MainWindow(manager, preview: true);
                if (main.Title != AppIdentity.Name || main.IsVisible)
                {
                    throw new Exception("The tray window must start hidden with the build-specific title.");
                }

                checks.Add("PASS Constructing the tray window leaves it hidden and gives it the build-specific title.");
                await CheckInitialTrayToggleAsync(main);
                checks.Add("PASS The first tray toggle shows the native window after hidden startup, and the second hides it.");
                var anchor = PopupPlacement.Cursor();
                var detected = PopupPlacement.Detect(anchor);
                var nativeBounds = main.PositionPopup(detected);
                if (nativeBounds.Left < detected.Monitor.Left || nativeBounds.Right > detected.Monitor.Right || nativeBounds.Top < detected.Monitor.Top || nativeBounds.Bottom > detected.Monitor.Bottom)
                {
                    throw new Exception("Live popup is outside its target monitor.");
                }

                if (detected.Taskbar is PixelRect bar && nativeBounds.Left < bar.Right && nativeBounds.Right > bar.Left && nativeBounds.Top < bar.Bottom && nativeBounds.Bottom > bar.Top)
                {
                    throw new Exception("Live popup covers the taskbar.");
                }

                checks.Add("PASS Hidden native WPF window placed on the live target monitor outside the detected taskbar: " + nativeBounds);
                main.ShowActivated = false;
                main.Opacity = 0;
                main.Show();
                main.PositionPopup(detected);
                main.UpdateLayout();
                await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var toolList = ((System.Windows.Controls.DockPanel)main.Content).Children.OfType<System.Windows.Controls.ScrollViewer>().Single();
                if (toolList.ComputedVerticalScrollBarVisibility == Visibility.Visible || toolList.ExtentHeight > toolList.ViewportHeight + 0.01)
                {
                    throw new Exception($"Expected the shown popup to fit all sample rows without scrolling. Extent {toolList.ExtentHeight:F2}, viewport {toolList.ViewportHeight:F2}, window {main.ActualHeight:F2}.");
                }

                main.Hide();
                checks.Add("PASS The shown popup fits all sample rows without a vertical scrollbar.");
                await Capture(main, "main");

                var markedTitles = Descendants((FrameworkElement)main.Content).OfType<System.Windows.Controls.TextBlock>()
                    .Where(title => title.Inlines.FirstInline is System.Windows.Documents.Run { Text: "\u00bb ", ToolTip: "Autostart enabled" })
                    .Select(System.Windows.Automation.AutomationProperties.GetName);
                if (!markedTitles.SequenceEqual(new[] { "Report exporter, autostart enabled" }))
                {
                    throw new Exception("Expected the double right arrow on the autostart tool only.");
                }

                checks.Add("PASS Main view marks only the autostart tool with a double right arrow.");

                var mainButtons = Descendants((FrameworkElement)main.Content).OfType<System.Windows.Controls.Button>().ToArray();
                var starts = mainButtons.Where(button => button.Content as string == "Start").ToArray();
                var stops = mainButtons.Where(button => button.Content as string == "Stop").ToArray();
                if (starts.Length != 3 || stops.Length != 3 ||
                    !starts.Select(button => button.IsEnabled).SequenceEqual(new[] { true, true, false }) ||
                    !stops.Select(button => button.IsEnabled).SequenceEqual(new[] { false, false, true }))
                {
                    throw new Exception("Expected separate Start and Stop buttons with opposite enabled states for idle and running tools.");
                }

                checks.Add("PASS Main view keeps separate Start and Stop buttons enabled according to tool state.");
                var outputButtons = mainButtons.Where(button => button.Content as string == "Output").ToArray();
                CheckOutputWindows(manager, tool, outputButtons[0], outputButtons[1]);
                checks.Add("PASS Output reuses and restores one window per tool, and reopens it after closing.");
                var largeList = new Configuration();
                for (var index = 0; index < 100; index++)
                {
                    largeList.Tools.Add(new ToolEntry { Name = $"Tool {index}" });
                }

                var largeMain = new MainWindow(new RunManager(largeList), preview: true);
                var largeBounds = largeMain.PositionPopup(detected);
                var limit = PopupPlacement.Calculate(detected, int.MaxValue, int.MaxValue, (int)Math.Ceiling(PopupPlacement.GapDip * detected.Dpi / 96d));
                if (largeBounds.Width != nativeBounds.Width || largeBounds.Height != limit.Height || nativeBounds.Height >= largeBounds.Height)
                {
                    throw new Exception("Extra rows should grow the popup vertically to the monitor limit without widening it.");
                }

                var wideList = new Configuration();
                wideList.Tools.Add(new ToolEntry { Name = new string('W', 300) });
                var wideMain = new MainWindow(new RunManager(wideList), preview: true);
                if (wideMain.PositionPopup(detected).Width != limit.Width)
                {
                    throw new Exception("Long titles should widen the popup up to the monitor limit.");
                }

                checks.Add("PASS Main window fits the row count, widening for long names, within monitor and taskbar limits.");
                var settings = new SettingsWindow(config);
                settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var startupButtons = ((System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)((System.Windows.Controls.DockPanel)settings.Content).Children[1]).Content).Children.OfType<System.Windows.Controls.StackPanel>().First().Children.OfType<System.Windows.Controls.Button>().ToArray();
                var exists = await Task.Run(() => new StartupTask().Exists());
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!startupButtons.Any(button => button.IsEnabled) && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(30);
                }

                if (startupButtons.Length != 3 || startupButtons[0].IsEnabled != !exists || startupButtons[1].IsEnabled != exists || startupButtons[2].IsEnabled != exists)
                {
                    throw new Exception("Startup buttons do not match the live scheduler task state.");
                }

                checks.Add("PASS Startup buttons reflect the live Task Scheduler state without modifying tasks.");
                await Capture(settings, "settings");
                settings.Close();
                var editor = new ToolEditor(config, tool);
                await Capture(editor, "editor");
                editor.Close();
                var output = new OutputWindow(manager, tool);
                await Capture(output, "output");
                output.Close();
                var boxes = Descendants((FrameworkElement)output.Content).OfType<System.Windows.Controls.TextBox>().ToArray();
                if (boxes.Length != 2)
                {
                    throw new Exception("Expected output and input textboxes.");
                }

                foreach (var box in boxes)
                {
                    if (box.Template?.VisualTree is null)
                    {
                        throw new Exception("Console textbox is still using a theme hover template.");
                    }

                    var scroll = box.Template.FindName("PART_ContentHost", box) as System.Windows.Controls.ScrollViewer;
                    if (scroll?.Parent is not System.Windows.Controls.Border border || border.Background != Brushes.Black || scroll.Background != Brushes.Black)
                    {
                        throw new Exception("Console template must keep both background layers black.");
                    }
                }

                var startAutostart = mainButtons.Single(button => button.Content as string == "Start autostart tools");
                if (!startAutostart.IsEnabled)
                {
                    throw new Exception("The bulk-start button should be enabled when an idle autostart tool is visible.");
                }

                startAutostart.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (!manager.IsActive(tool.Id) || startAutostart.IsEnabled)
                {
                    throw new Exception("Bulk start should start the eligible tool and disable the button while it runs.");
                }

                await manager.StopAllAsync();
                checks.Add("PASS Bulk start launches the eligible tool and disables the button while it is active.");

                checks.Add("PASS Console output and input use fixed black templates without hover/focus triggers.");
                await CheckStartKeepsDispatcherAvailableAsync(folder);
                await CheckCronOutputAsync();
                checks.Add("PASS Cron output shows retained occurrences in order and updates when a new occurrence writes output.");
                await StatusUiTests.RunAsync();
                checks.Add("PASS Custom Status observer updates, single-line layout, authoritative states and editor choices.");
                await CronTests.RunAsync();
                checks.Add("PASS Cron validation, repeated execution, overlap prevention, and stop lifecycle.");
                checks.Add("PASS Start publishes an active run on the dispatcher before shell preparation finishes.");
                checks.Add("PASS WPF windows constructed and rendered offscreen.");
                await File.WriteAllLinesAsync(Path.Combine(folder, "result.txt"), checks);
            }
            catch (Exception e)
            {
                result = 1;
                await File.WriteAllTextAsync(Path.Combine(folder, "result.txt"), e.ToString());
            }
            finally
            {
                app.Shutdown();
            }
        });
        app.Run();
        return result;
    }

    private static void CheckOutputWindows(RunManager manager, ToolEntry tool, System.Windows.Controls.Button firstButton, System.Windows.Controls.Button secondButton)
    {
        void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        try
        {
            Click(firstButton);
            var first = Application.Current.Windows.OfType<OutputWindow>().Single();
            first.WindowState = WindowState.Minimized;
            Click(firstButton);
            var reopened = Application.Current.Windows.OfType<OutputWindow>().ToArray();
            if (reopened.Length != 1 || reopened[0] != first || first.WindowState == WindowState.Minimized)
            {
                throw new Exception("Clicking Output again must restore the existing window without making a duplicate.");
            }

            var editedTool = Store.Clone(tool);
            editedTool.Name = "Renamed report exporter";
            editedTool.Behavior = "Cron";
            OutputWindow.ShowForTool(manager, editedTool);
            var runPicker = ((System.Windows.Controls.DockPanel)first.Content).Children.OfType<System.Windows.Controls.ComboBox>().Single();
            if (Application.Current.Windows.OfType<OutputWindow>().Single() != first ||
                !first.Title.StartsWith(editedTool.Name, StringComparison.Ordinal) ||
                runPicker.Visibility != Visibility.Collapsed)
            {
                throw new Exception("An edited tool must reuse its Output window with the updated title and behavior.");
            }

            Click(secondButton);
            if (Application.Current.Windows.OfType<OutputWindow>().Count() != 2)
            {
                throw new Exception("Different tools must be able to show separate Output windows.");
            }

            first.Close();
            Click(firstButton);
            var windows = Application.Current.Windows.OfType<OutputWindow>().ToArray();
            if (windows.Length != 2 || windows.Contains(first))
            {
                throw new Exception("Closing an Output window must allow the same tool to open a new one.");
            }
        }
        finally
        {
            foreach (var window in Application.Current.Windows.OfType<OutputWindow>().ToArray())
            {
                window.Close();
            }
        }
    }

    [DllImport("user32")]
    private static extern bool IsWindowVisible(nint window);

    private static async Task CheckInitialTrayToggleAsync(MainWindow main)
    {
        main.TogglePopup();
        await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var handle = new WindowInteropHelper(main).Handle;
        if (!main.IsVisible || !IsWindowVisible(handle))
        {
            throw new Exception($"First tray toggle did not open the native window. WPF visible: {main.IsVisible}, native visible: {IsWindowVisible(handle)}, state: {main.WindowState}.");
        }

        main.TogglePopup();
        if (main.IsVisible || IsWindowVisible(handle))
        {
            throw new Exception("Second tray toggle must hide the same window.");
        }
    }

    private static async Task CheckCronOutputAsync()
    {
        var manager = new RunManager(new Configuration());
        var tool = new ToolEntry { Behavior = "Cron" };
        var first = new RunRecord { ToolId = tool.Id, Ended = DateTime.Now, State = "Finished", ExitCode = 0 };
        first.Append("first occurrence", 100);
        var second = new RunRecord { ToolId = tool.Id, Ended = DateTime.Now, State = "Failed", ExitCode = 7 };
        second.Append("second occurrence", 100);
        manager.History.AddRange([first, second]);

        var window = new OutputWindow(manager, tool);
        try
        {
            var output = ((System.Windows.Controls.DockPanel)window.Content).Children.OfType<System.Windows.Controls.TextBox>().Single();
            var firstIndex = output.Text.IndexOf("first occurrence", StringComparison.Ordinal);
            var secondIndex = output.Text.IndexOf("second occurrence", StringComparison.Ordinal);
            if (firstIndex < 0 || secondIndex <= firstIndex || !output.Text.Contains("Exit code 7", StringComparison.Ordinal))
            {
                throw new Exception("Cron output must show both occurrences in order, with the failed run's exit code.");
            }

            var third = new RunRecord { ToolId = tool.Id, State = "Running" };
            third.Append("old output", 10);
            manager.History.Add(third);
            await WaitForOutputAsync(output, "old output");
            third.Append("new output", 10);
            await WaitForOutputAsync(output, "new output");
            if (!output.Text.Contains("first occurrence", StringComparison.Ordinal) ||
                !output.Text.Contains("second occurrence", StringComparison.Ordinal) ||
                output.Text.Contains("old output", StringComparison.Ordinal))
            {
                throw new Exception("Live updates must keep both prior runs and replace truncated output from the active run.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForOutputAsync(System.Windows.Controls.TextBox output, string expected)
    {
        var elapsed = Stopwatch.StartNew();
        while (!output.Text.Contains(expected, StringComparison.Ordinal))
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("Cron output did not refresh for " + expected);
            }

            await Task.Delay(25);
        }
    }

    private static async Task CheckStartKeepsDispatcherAvailableAsync(string folder)
    {
        var config = new Configuration();
        config.Shells[0].Executable = Path.Combine(folder, "missing-" + Guid.NewGuid().ToString("N") + ".exe");
        var manager = new RunManager(config);
        var tool = new ToolEntry { ShellId = config.Shells[0].Id, Command = "exit 0" };
        var start = manager.StartAsync(tool);
        if (!start.IsCompletedSuccessfully || manager.Current(tool.Id) is not { Active: true, State: "Starting" })
        {
            throw new Exception("Start must return a pending run before shell preparation reports a failure.");
        }

        var run = await start;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (run.Active && DateTime.UtcNow < deadline)
        {
            await Task.Delay(30);
        }

        if (run.Active || run.State != "Failed" || !run.Output.Contains("Shell not found:", StringComparison.Ordinal))
        {
            throw new Exception("Expected the shell preparation failure to reach the run after the dispatcher resumed.");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }
}
