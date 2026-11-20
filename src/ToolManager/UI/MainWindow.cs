namespace ToolManager.UI;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using ToolManager.Execution;
using ToolManager.Models;
using ToolManager.UI.Placement;
using Forms = System.Windows.Forms;

internal sealed class MainWindow : Window
{
    private readonly RunManager manager;
    private readonly StackPanel rows = new();
    private readonly Button startAutostart;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool exiting;
    private bool openingSettings;
    private string signature = string.Empty;
    private PixelPoint? trayAnchor;

    public MainWindow(RunManager manager, bool preview = false)
    {
        this.manager = manager;
        this.Background = SystemColors.WindowBrush;
        this.Foreground = SystemColors.WindowTextBrush;
        this.Icon = Ui.AppIcon;
        this.Title = AppIdentity.Name;
        this.ResizeMode = ResizeMode.CanMinimize;
        this.ShowInTaskbar = true;

        // A new WPF window defaults to Collapsed, which skips measurement.
        this.Visibility = Visibility.Hidden;
        var root = new DockPanel { Margin = new Thickness(10) };
        var header = new DockPanel();
        var settings = Ui.Button("Settings", async () => await this.OpenSettingsAsync());
        DockPanel.SetDock(settings, Dock.Right);
        header.Children.Add(settings);
        this.startAutostart = Ui.Button("Start autostart tools", async () => await this.StartAutostartAsync());
        DockPanel.SetDock(this.startAutostart, Dock.Right);
        header.Children.Add(this.startAutostart);
        header.Children.Add(Ui.Text(AppIdentity.Name, 22));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        footer.Children.Add(Ui.Button("Exit manager", async () => await this.ExitAsync()));
        footer.Children.Add(Ui.Button("Hide", this.Hide));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer
        {
            Content = this.rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        this.Content = root;
        this.trayIcon = Ui.TrayIcon();
        this.tray = new Forms.NotifyIcon
        {
            Icon = this.trayIcon,
            Text = AppIdentity.Name,
            Visible = !preview,
        };
        this.tray.MouseDown += (_, _) => this.trayAnchor = PopupPlacement.Cursor();
        this.tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                this.Dispatcher.Invoke(this.Toggle);
            }
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => this.Dispatcher.Invoke(this.ShowPopup));
        menu.Items.Add("Settings", null, (_, _) => this.Dispatcher.Invoke(async () => await this.OpenSettingsAsync()));
        menu.Items.Add("Exit", null, (_, _) => this.Dispatcher.Invoke(async () => await this.ExitAsync()));
        this.tray.ContextMenuStrip = menu;
        this.Closing += (_, e) =>
        {
            if (!this.exiting)
            {
                e.Cancel = true;
                this.Hide();
            }
        };
        this.timer.Tick += (_, _) => this.Refresh();
        this.timer.Start();
        this.Refresh();
    }

    internal void ShowPopup()
    {
        var anchor = this.trayAnchor ?? PopupPlacement.PrimaryTaskbarAnchor();
        this.WindowState = WindowState.Normal;
        var location = PopupPlacement.Detect(anchor);
        this.PositionPopup(location);
        this.Show();
        this.PositionPopup(location);
        this.Activate();
    }

    internal PixelRect PositionPopup(PopupLocation location)
    {
        var scale = location.Dpi / 96d;
        var limit = PopupPlacement.Calculate(location, int.MaxValue, int.MaxValue, (int)Math.Ceiling(PopupPlacement.GapDip * scale));

        new WindowInteropHelper(this).EnsureHandle();
        this.Width = double.NaN;
        this.Height = double.NaN;

        this.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        this.Width = Math.Min(limit.Width / scale, Math.Max(620, Math.Ceiling(this.DesiredSize.Width)));
        this.Measure(new Size(this.Width, double.PositiveInfinity));
        this.Height = Math.Min(limit.Height / scale, Math.Max(120, Math.Ceiling(this.DesiredSize.Height)));
        return PopupPlacement.Position(this, location);
    }

    private void Toggle()
    {
        if (this.IsVisible && this.WindowState != WindowState.Minimized)
        {
            this.Hide();
        }
        else
        {
            this.ShowPopup();
        }
    }

    private string State(ToolEntry t)
    {
        var current = this.manager.Current(t.Id);
        if (t.Behavior == "Cron" && current is null)
        {
            return this.manager.ScheduleState(t.Id) ?? "Ready";
        }

        var run = current ?? this.manager.History.LastOrDefault(r => r.ToolId == t.Id);
        if (run is null)
        {
            return t.Behavior == "Custom" ? "Unknown" : "Ready";
        }

        if (run.Active && run.StopDeadline is DateTime deadline)
        {
            return $"Stopping {Math.Max(0, (int)Math.Ceiling((deadline - DateTime.Now).TotalSeconds))}s";
        }

        if (t.Behavior == "Custom" && !run.Active)
        {
            return RunManager.CustomTargetState(run);
        }

        return run.State;
    }

    private void Refresh()
    {
        var visible = this.manager.Config.Tools.Where(t => t.Visible).ToArray();
        this.startAutostart.IsEnabled = !this.openingSettings && !this.exiting && visible.Any(this.manager.CanAutostart);
        var next = string.Join("|", visible.Select(t => t.Id + t.Name + this.State(t) + this.manager.IsActive(t.Id))) + this.manager.History.Count;
        if (next == this.signature)
        {
            return;
        }

        this.signature = next;
        this.rows.Children.Clear();
        if (visible.Length == 0)
        {
            this.rows.Children.Add(Ui.Text("No tools configured for this PC. Add a tool in Settings."));
        }

        foreach (var tool in visible)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            var running = this.manager.IsActive(tool.Id);
            var externalRunning = tool.Behavior == "Custom" && this.State(tool) == "Running";
            var start = Ui.Button("Start", async () => await this.Act(tool, "Run"));
            start.IsEnabled = !running && !externalRunning;
            buttons.Children.Add(start);
            var stop = Ui.Button("Stop", async () => await this.Act(tool, running ? "Cancel" : "Stop"));
            stop.IsEnabled = (running || externalRunning) && this.manager.Current(tool.Id)?.State != "Stopping" && this.manager.ScheduleState(tool.Id) != "Stopping";
            buttons.Children.Add(stop);
            if (tool.Behavior == "Custom" && !running)
            {
                if (!string.IsNullOrWhiteSpace(tool.StatusCommand))
                {
                    buttons.Children.Add(Ui.Button("Check", async () => await this.Act(tool, "Check")));
                }
            }

            buttons.Children.Add(Ui.Button("Output", () => OutputWindow.ShowForTool(this.manager, tool)));
            var label = new StackPanel();
            var title = Ui.Text(tool.Name, 16);
            if (tool.Autostart)
            {
                title.Inlines.InsertBefore(title.Inlines.FirstInline, new Run("\u00bb ") { ToolTip = "Autostart enabled" });
                AutomationProperties.SetName(title, tool.Name + ", autostart enabled");
            }

            label.Children.Add(title);
            label.Children.Add(Ui.Text(this.State(tool), 12));
            row.Children.Add(label);
            this.rows.Children.Add(row);
        }
    }

    private async Task Act(ToolEntry tool, string action)
    {
        try
        {
            if (action == "Cancel")
            {
                await this.manager.StopAsync(tool.Id);
            }
            else if (action == "Run")
            {
                await this.manager.StartToolAsync(tool);
            }
            else
            {
                await this.manager.StartAsync(tool, action);
            }
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }

        this.signature = string.Empty;
        this.Refresh();
    }

    private async Task StartAutostartAsync()
    {
        if (this.openingSettings || this.exiting || !this.IsEnabled)
        {
            return;
        }

        try
        {
            await this.manager.AutostartAsync(true);
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }

        this.Refresh();
    }

    private async Task OpenSettingsAsync()
    {
        if (this.openingSettings || this.exiting || !this.IsEnabled)
        {
            return;
        }

        this.openingSettings = true;
        this.rows.IsEnabled = false;
        try
        {
            var active = this.manager.History.Where(r => r.Active).ToArray();
            if (this.manager.HasActiveTools)
            {
                const string prompt = "Stop all active runs and schedules and open Settings? Each run receives Ctrl+C and has its configured grace period before forced termination. External targets controlled by custom scripts may continue running.";
                if (MessageBox.Show(prompt, "Open Settings", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                {
                    return;
                }

                await this.manager.StopAllAsync();
                if (active.Any(run => run.State == "Failed"))
                {
                    throw new InvalidOperationException("A run reported an error while stopping. See its output for details.");
                }
            }

            var dialog = new SettingsWindow(this.manager.Config) { Owner = this.IsVisible ? this : null };
            if (dialog.ShowDialog() == true)
            {
                this.manager.Config = dialog.Result;
                this.manager.Prune();
                this.signature = string.Empty;
                this.Refresh();
                if (this.IsVisible && this.WindowState == WindowState.Normal)
                {
                    this.PositionPopup(PopupPlacement.Detect(this.trayAnchor ?? PopupPlacement.PrimaryTaskbarAnchor()));
                }
            }
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }
        finally
        {
            this.rows.IsEnabled = true;
            this.openingSettings = false;
        }
    }

    private async Task ExitAsync()
    {
        if (this.exiting || this.openingSettings || !this.IsEnabled)
        {
            return;
        }

        if (this.manager.HasActiveTools && MessageBox.Show("Stop all active runs and schedules and exit? Each run receives Ctrl+C, then its configured timeout before forced termination. External targets controlled by custom scripts are not stopped automatically.", "Exit Tool Manager", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
        {
            return;
        }

        this.IsEnabled = false;
        await this.manager.StopAllAsync();
        this.exiting = true;
        this.timer.Stop();
        this.tray.Dispose();
        this.trayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
