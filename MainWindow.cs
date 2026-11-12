namespace ToolManager;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal sealed class MainWindow : Window
{
    private readonly RunManager manager;
    private readonly StackPanel rows = new();
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool exiting;
    private string signature = string.Empty;
    private PixelPoint? trayAnchor;

    public MainWindow(RunManager manager, bool preview = false)
    {
        this.manager = manager;
        this.Background = SystemColors.WindowBrush;
        this.Foreground = SystemColors.WindowTextBrush;
        this.Icon = Ui.AppIcon;
        this.Title = "Tool Manager";
        this.Width = 620;
        this.Height = 430;
        this.MinWidth = 460;
        this.MinHeight = 250;
        this.ShowInTaskbar = true;
        var root = new DockPanel { Margin = new Thickness(10) };
        var header = new DockPanel();
        var settings = Ui.Button("Settings", this.OpenSettings);
        DockPanel.SetDock(settings, Dock.Right);
        header.Children.Add(settings);
        header.Children.Add(Ui.Text("Tool Manager", 22));
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
            Text = "Tool Manager",
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
        menu.Items.Add("Settings", null, (_, _) => this.Dispatcher.Invoke(this.OpenSettings));
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
        this.Loaded += (_, _) => this.Position();
    }

    private void Position()
    {
        this.trayAnchor ??= PopupPlacement.Cursor();
        PopupPlacement.Position(this, this.trayAnchor.Value);
    }

    private void Toggle()
    {
        if (this.IsVisible && this.IsActive)
        {
            this.Hide();
        }
        else
        {
            this.ShowPopup();
        }
    }

    private void ShowPopup()
    {
        this.WindowState = WindowState.Normal;
        this.Position();
        this.Show();
        this.Position();
        this.Activate();
    }

    private string State(ToolEntry t)
    {
        var run = this.manager.Current(t.Id) ?? this.manager.History.LastOrDefault(r => r.ToolId == t.Id);
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
            return run.Action == "Check" && run.State == "Finished" ? "Running" : run.Action == "Check" && run.ExitCode == 1 ? "Stopped" : "Unknown";
        }

        return run.State;
    }

    private void Refresh()
    {
        var visible = this.manager.Config.Tools.Where(t => t.Visible).ToArray();
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
            stop.IsEnabled = (running || externalRunning) && this.manager.Current(tool.Id)?.State != "Stopping";
            buttons.Children.Add(stop);
            if (tool.Behavior == "Custom" && !running)
            {
                if (!string.IsNullOrWhiteSpace(tool.StatusCommand))
                {
                    buttons.Children.Add(Ui.Button("Check", async () => await this.Act(tool, "Check")));
                }
            }

            buttons.Children.Add(Ui.Button("Output", () => new OutputWindow(this.manager, tool).Show()));
            var label = new StackPanel();
            label.Children.Add(Ui.Text(tool.Name, 16));
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

    private void OpenSettings()
    {
        if (this.manager.History.Any(r => r.Active))
        {
            MessageBox.Show("Stop active runs before changing configuration.", "Settings");
            return;
        }

        var dialog = new SettingsWindow(this.manager.Config) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            this.manager.Config = dialog.Result;
            this.manager.Prune();
            this.signature = string.Empty;
            this.Refresh();
        }
    }

    private async Task ExitAsync()
    {
        if (this.exiting)
        {
            return;
        }

        if (this.manager.History.Any(r => r.Active) && MessageBox.Show("Stop all active runs and exit? Each run receives Ctrl+C, then its configured timeout before forced termination. External targets controlled by custom scripts are not stopped automatically.", "Exit Tool Manager", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
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
