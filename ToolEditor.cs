namespace ToolManager;

using System.Windows;
using System.Windows.Controls;

internal sealed class ToolEditor : Window
{
    public ToolEditor(Configuration config, ToolEntry draft)
    {
        this.Background = SystemColors.WindowBrush;
        this.Foreground = SystemColors.WindowTextBrush;
        this.Icon = Ui.AppIcon;
        this.Title = "Edit tool";
        this.Width = 760;
        this.Height = 840;
        this.MinWidth = 560;
        this.MinHeight = 420;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) };
        this.Content = root;
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        var name = Ui.Input(draft.Name);
        Ui.Field(body, "Name", name);
        var mode = new ComboBox
        {
            ItemsSource = new[] { "Installed", "Bundled" },
            SelectedItem = draft.Mode,
            Margin = new Thickness(4),
        };
        Ui.Field(body, "Application mode", mode);
        body.Children.Add(Ui.Text("Installed: PATH names, absolute paths, or relative paths. Bundled: use relative tool paths. Scripts are executed as written; paths inside them are not rewritten.", 12));
        var behavior = new ComboBox
        {
            ItemsSource = new[] { "Managed", "Custom" },
            SelectedItem = draft.Behavior,
            Margin = new Thickness(4),
        };
        Ui.Field(body, "Behavior", behavior);
        body.Children.Add(Ui.Text("Managed: both one-shot and long-running programs. Custom: scripts controlling an external target; its state is unknown until checked.", 12));
        var shell = new ComboBox
        {
            ItemsSource = config.Shells,
            SelectedItem = config.Shells.FirstOrDefault(s => s.Id == draft.ShellId),
            Margin = new Thickness(4),
        };
        Ui.Field(body, "Execution shell", shell);
        var command = Ui.Input(draft.Command, true);
        Ui.Field(body, "Start command or full script", command);
        body.Children.Add(Ui.Text("PowerShell example: & '.\\tools\\My Tool\\tool.exe' -Port 8080\nManager root is available as $env:TOOLMANAGER_ROOT. PowerShell profile loading follows the shell setting.", 12));
        var interactive = new CheckBox
        {
            Content = "Keep shell open after the start command (enable line input)",
            IsChecked = draft.Interactive,
            Margin = new Thickness(5),
        };
        body.Children.Add(interactive);
        body.Children.Add(Ui.Text("Enable for a persistent shell or commands that read text input. Output > Send or Enter sends one line. Stop ends the entire session.", 12));
        var working = Ui.Input(draft.WorkingDirectory);
        Ui.Field(body, "Working directory (relative paths start at the manager folder)", working);
        var stopTimeout = Ui.Input(draft.StopTimeoutSeconds?.ToString() ?? string.Empty);
        Ui.Field(body, "Ctrl+C grace period in seconds (blank uses global default)", stopTimeout);
        var custom = new StackPanel();
        body.Children.Add(custom);
        var stop = Ui.Input(draft.StopCommand, true);
        Ui.Field(custom, "Stop script for the external target", stop);
        var status = Ui.Input(draft.StatusCommand, true);
        Ui.Field(custom, "Optional status script: exit 0 = running, 1 = stopped, other = unknown", status);
        behavior.SelectionChanged += (_, _) => custom.Visibility = (string?)behavior.SelectedItem == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        custom.Visibility = draft.Behavior == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        var restricted = new CheckBox
        {
            Content = "Only show on these PCs",
            IsChecked = draft.RestrictToPcs,
            Margin = new Thickness(5),
        };
        body.Children.Add(restricted);
        var names = Ui.Input(string.Join(Environment.NewLine, draft.PcNames), true);
        Ui.Field(body, "PC names (one per line, case-insensitive)", names);
        void AddPc()
        {
            var pcs = names.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).Append(Environment.MachineName).Distinct(StringComparer.OrdinalIgnoreCase);
            names.Text = string.Join(Environment.NewLine, pcs);
        }

        restricted.Checked += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(names.Text))
            {
                AddPc();
            }
        };
        body.Children.Add(Ui.Button("Add this PC (" + Environment.MachineName + ")", AddPc));
        footer.Children.Add(Ui.Button("Save", () =>
        {
            try
            {
                draft.Name = name.Text.Trim();
                draft.Mode = (string)mode.SelectedItem;
                draft.Behavior = (string)behavior.SelectedItem;
                draft.ShellId = (shell.SelectedItem as ShellProfile)?.Id ?? string.Empty;
                draft.Interactive = interactive.IsChecked == true;
                draft.Command = command.Text;
                draft.StopCommand = stop.Text;
                draft.StatusCommand = status.Text;
                draft.WorkingDirectory = working.Text.Trim();
                draft.StopTimeoutSeconds = string.IsNullOrWhiteSpace(stopTimeout.Text) ? null : int.Parse(stopTimeout.Text);
                draft.RestrictToPcs = restricted.IsChecked == true;
                draft.PcNames = names.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
                var check = Store.Clone(config);
                check.Tools.RemoveAll(t => t.Id == draft.Id);
                check.Tools.Add(draft);
                Store.Validate(check);
                this.DialogResult = true;
            }
            catch (Exception e)
            {
                Ui.Error(e);
            }
        }));
        footer.Children.Add(Ui.Button("Cancel", () => this.DialogResult = false));
    }
}
