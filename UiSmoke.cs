namespace ToolManager;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
                var icons = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/ToolManager.ico"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
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
                var anchor = PopupPlacement.Cursor();
                var detected = PopupPlacement.Detect(anchor);
                var nativeBounds = PopupPlacement.Position(main, anchor);
                if (nativeBounds.Left < detected.Monitor.Left || nativeBounds.Right > detected.Monitor.Right || nativeBounds.Top < detected.Monitor.Top || nativeBounds.Bottom > detected.Monitor.Bottom)
                {
                    throw new Exception("Live popup is outside its target monitor.");
                }

                if (detected.Taskbar is PixelRect bar && nativeBounds.Left < bar.Right && nativeBounds.Right > bar.Left && nativeBounds.Top < bar.Bottom && nativeBounds.Bottom > bar.Top)
                {
                    throw new Exception("Live popup covers the taskbar.");
                }

                checks.Add("PASS Hidden native WPF window placed on the live target monitor outside the detected taskbar: " + nativeBounds);
                await Capture(main, "main");
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
                var settings = new SettingsWindow(config);
                settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var startupButtons = ((System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)((System.Windows.Controls.DockPanel)settings.Content).Children[1]).Content).Children.OfType<System.Windows.Controls.StackPanel>().First().Children.OfType<System.Windows.Controls.Button>().ToArray();
                var exists = await Task.Run(() => new StartupTask().Exists());
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!startupButtons.Any(button => button.IsEnabled) && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(30);
                }

                if (startupButtons.Length != 2 || startupButtons[0].IsEnabled != !exists || startupButtons[1].IsEnabled != exists)
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

                checks.Add("PASS Console output and input use fixed black templates without hover/focus triggers.");
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
