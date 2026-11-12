namespace ToolManager;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal sealed class OutputWindow : Window
{
    private readonly RunManager manager;
    private readonly ToolEntry tool;
    private readonly ComboBox runs = new()
    {
        Margin = new Thickness(4),
        MinWidth = 300,
    };

    private readonly TextBox output = new()
    {
        Background = Brushes.Black,
        Foreground = Brushes.Gainsboro,
        CaretBrush = Brushes.White,
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(4),
    };

    private readonly CheckBox follow = new()
    {
        Content = "Follow output",
        IsChecked = true,
        Margin = new Thickness(8),
    };

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly TextBlock detail = Ui.Text(string.Empty);
    private readonly TextBox input = Ui.Input(string.Empty);
    private readonly Button send;
    private bool sending;
    private string ids = string.Empty;

    public OutputWindow(RunManager manager, ToolEntry tool)
    {
        this.manager = manager;
        this.Background = SystemColors.WindowBrush;
        this.Foreground = SystemColors.WindowTextBrush;
        this.tool = tool;
        this.Icon = Ui.AppIcon;
        this.Title = tool.Name + " - Output";
        this.Width = 820;
        this.Height = 510;
        Ui.ConsoleTextBox(this.output);
        Ui.ConsoleTextBox(this.input);
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(this.runs, Dock.Top);
        root.Children.Add(this.runs);
        DockPanel.SetDock(this.detail, Dock.Top);
        root.Children.Add(this.detail);
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        footer.Children.Add(this.follow);
        footer.Children.Add(Ui.Button("Copy", () =>
        {
            try
            {
                Clipboard.SetText(this.output.Text);
            }
            catch (Exception e)
            {
                Ui.Error(e);
            }
        }));
        footer.Children.Add(Ui.Button("Close", this.Close));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var entry = new DockPanel();
        this.send = Ui.Button("Send", async () => await this.SendAsync());
        DockPanel.SetDock(this.send, Dock.Right);
        entry.Children.Add(this.send);
        this.input.Background = Brushes.Black;
        this.input.Foreground = Brushes.Gainsboro;
        this.input.CaretBrush = Brushes.White;
        this.input.MaxLength = RunRequest.MaximumInputCharacters;
        this.input.ToolTip = "Send a line to the selected live run. Enable keep-open shell in tool settings.";
        System.Windows.Automation.AutomationProperties.SetName(this.input, "Command or input line");
        this.input.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && this.send.IsEnabled)
            {
                e.Handled = true;
                await this.SendAsync();
            }
        };
        entry.Children.Add(this.input);
        DockPanel.SetDock(entry, Dock.Bottom);
        root.Children.Add(entry);
        root.Children.Add(this.output);
        this.Content = root;
        this.runs.SelectionChanged += (_, _) => this.UpdateOutput();
        this.timer.Tick += (_, _) => this.Refresh();
        this.Closed += (_, _) => this.timer.Stop();
        this.timer.Start();
        this.Refresh();
    }

    private void Refresh()
    {
        var records = this.manager.History.Where(r => r.ToolId == this.tool.Id).OrderByDescending(r => r.Started).ToArray();
        var next = string.Join('|', records.Select(r => r.Id + r.State));
        if (this.ids != next)
        {
            var selected = (this.runs.SelectedItem as RunRecord)?.Id;
            this.ids = next;
            this.runs.ItemsSource = records;
            this.runs.SelectedItem = records.FirstOrDefault(r => r.Id == selected) ?? records.FirstOrDefault();
        }

        this.UpdateOutput();
    }

    private async Task SendAsync()
    {
        if (this.sending || this.runs.SelectedItem is not RunRecord run)
        {
            return;
        }

        this.sending = true;
        this.input.IsEnabled = this.send.IsEnabled = false;
        try
        {
            await this.manager.SendInputAsync(run, this.input.Text);
            this.input.Clear();
            this.input.Focus();
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }
        finally
        {
            this.sending = false;
            this.UpdateOutput();
        }
    }

    private void UpdateOutput()
    {
        var run = this.runs.SelectedItem as RunRecord;
        this.input.IsEnabled = this.send.IsEnabled = !this.sending && run is { Active: true, Interactive: true, State: "Running" };
        this.detail.Text = run is null ? "No runs yet. History is kept in memory until the manager exits." : $"{run.Action} | {run.State} | Duration {(run.Ended ?? DateTime.Now) - run.Started:hh\\:mm\\:ss}" + (run.ExitCode is int c ? $" | Exit code {c}" : string.Empty);
        var text = run?.Output ?? string.Empty;
        if (text != this.output.Text)
        {
            this.output.Text = text;
            if (this.follow.IsChecked == true)
            {
                this.output.ScrollToEnd();
            }
        }
    }
}
