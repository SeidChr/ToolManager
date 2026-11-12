namespace ToolManager;

using System.Windows;
using System.Windows.Controls;

internal sealed class SettingsWindow : Window
{
    private readonly ListBox tools = new()
    {
        MinHeight = 150,
        Margin = new Thickness(4),
    };

    private readonly ListBox shells = new()
    {
        Height = 110,
        Margin = new Thickness(4),
    };

    private readonly ComboBox defaultShell = new() { Margin = new Thickness(4) };
    private readonly StartupTask startupTask = new();
    private readonly Button addStartup;
    private readonly Button removeStartup;
    private readonly TextBlock startupStatus = Ui.Text("Checking Task Scheduler...", 12);
    private readonly TextBox timeout;
    private readonly TextBox history;
    private readonly TextBox output;
    private readonly TextBox total;
    private bool changingStartup;
    private int startupRevision;

    public SettingsWindow(Configuration source)
    {
        this.Background = SystemColors.WindowBrush;
        this.Foreground = SystemColors.WindowTextBrush;
        this.Result = Store.Clone(source);
        this.Icon = Ui.AppIcon;
        this.Title = "Tool Manager settings";
        this.Width = 700;
        this.Height = 730;
        this.MinWidth = 540;
        this.MinHeight = 400;
        var root = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        footer.Children.Add(Ui.Button("Save", this.Save));
        footer.Children.Add(Ui.Button("Cancel", () => this.DialogResult = false));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        this.Content = root;
        body.Children.Add(Ui.Text("Startup", 20));
        body.Children.Add(Ui.Text("Start this copy of Tool Manager when the current user signs in after Windows starts. Changes apply immediately. Windows asks for administrator permission only if needed.", 12));
        var startupActions = new StackPanel { Orientation = Orientation.Horizontal };
        this.addStartup = Ui.Button("Add startup task", () => _ = this.ChangeStartupAsync(true));
        this.removeStartup = Ui.Button("Remove startup task", () => _ = this.ChangeStartupAsync(false));
        this.addStartup.IsEnabled = false;
        this.removeStartup.IsEnabled = false;
        startupActions.Children.Add(this.addStartup);
        startupActions.Children.Add(this.removeStartup);
        body.Children.Add(startupActions);
        body.Children.Add(this.startupStatus);
        this.Activated += async (_, _) => await this.RefreshStartupAsync();
        this.Loaded += async (_, _) => await this.RefreshStartupAsync();
        body.Children.Add(Ui.Text("Tools", 20));
        body.Children.Add(this.tools);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(actions);
        actions.Children.Add(Ui.Button("Add", () => this.EditTool(true)));
        actions.Children.Add(Ui.Button("Edit", () => this.EditTool(false)));
        actions.Children.Add(Ui.Button("Delete", () =>
        {
            if (this.tools.SelectedItem is ToolEntry t && MessageBox.Show($"Delete '{t.Name}'?", "Delete entry", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                this.Result.Tools.Remove(t);
                this.Refresh();
            }
        }));
        actions.Children.Add(Ui.Button("Up", () => this.Move(-1)));
        actions.Children.Add(Ui.Button("Down", () => this.Move(1)));
        this.tools.MouseDoubleClick += (_, _) => this.EditTool(false);
        body.Children.Add(Ui.Text("Shell profiles", 20));
        body.Children.Add(this.shells);
        var shellActions = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(shellActions);
        shellActions.Children.Add(Ui.Button("Add shell", () => this.EditShell(true)));
        shellActions.Children.Add(Ui.Button("Edit shell", () => this.EditShell(false)));
        shellActions.Children.Add(Ui.Button("Delete shell", () =>
        {
            if (this.shells.SelectedItem is not ShellProfile s)
            {
                return;
            }

            if (this.Result.Tools.Any(t => t.ShellId == s.Id) || this.Result.DefaultShellId == s.Id)
            {
                MessageBox.Show("Change tools and the default shell before deleting this profile.");
                return;
            }

            this.Result.Shells.Remove(s);
            this.Refresh();
        }));
        Ui.Field(body, "Default shell for new entries", this.defaultShell);
        this.defaultShell.SelectionChanged += (_, _) =>
        {
            if (this.defaultShell.SelectedItem is ShellProfile s)
            {
                this.Result.DefaultShellId = s.Id;
            }
        };
        this.timeout = Ui.Input(this.Result.StopTimeoutSeconds.ToString());
        Ui.Field(body, "Default Ctrl+C grace period (seconds, 1-3600)", this.timeout);
        this.history = Ui.Input(this.Result.HistoryCount.ToString());
        Ui.Field(body, "Completed runs retained per tool (1-100)", this.history);
        this.output = Ui.Input(this.Result.OutputLimitKiB.ToString());
        Ui.Field(body, "Maximum output per run (KiB, 16-16384)", this.output);
        this.total = Ui.Input(this.Result.GlobalOutputLimitMiB.ToString());
        Ui.Field(body, "Maximum total retained output (MiB, 4-512)", this.total);
        body.Children.Add(Ui.Text("Output history is kept in memory. All configuration and temporary execution files stay beside the application.\nConfiguration: " + Store.ConfigPath, 12));
        this.Refresh();
    }

    public Configuration Result
    { get; }

    private async Task RefreshStartupAsync()
    {
        if (this.changingStartup)
        {
            return;
        }

        var revision = ++this.startupRevision;
        this.addStartup.IsEnabled = false;
        this.removeStartup.IsEnabled = false;
        try
        {
            var exists = await Task.Run(this.startupTask.Exists);
            if (this.changingStartup || revision != this.startupRevision)
            {
                return;
            }

            this.addStartup.IsEnabled = !exists;
            this.removeStartup.IsEnabled = exists;
            this.startupStatus.Text = (exists ? "Startup task exists: " : "No startup task: ") + this.startupTask.Name;
        }
        catch (Exception e)
        {
            if (revision == this.startupRevision)
            {
                this.startupStatus.Text = "Cannot read Task Scheduler: " + e.Message;
            }
        }
    }

    private async Task ChangeStartupAsync(bool add)
    {
        this.changingStartup = true;
        this.startupRevision++;
        this.addStartup.IsEnabled = false;
        this.removeStartup.IsEnabled = false;
        this.startupStatus.Text = "Updating startup task...";
        try
        {
            await this.startupTask.ChangeAsync(add);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // Canceling the Windows administrator prompt leaves the task unchanged.
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }
        finally
        {
            this.changingStartup = false;
            await this.RefreshStartupAsync();
        }
    }

    private void Refresh()
    {
        var selected = this.tools.SelectedItem;
        this.tools.ItemsSource = null;
        this.tools.ItemsSource = this.Result.Tools;
        this.tools.SelectedItem = selected;
        var shell = this.shells.SelectedItem;
        this.shells.ItemsSource = null;
        this.shells.ItemsSource = this.Result.Shells;
        this.shells.SelectedItem = shell;
        var id = this.Result.DefaultShellId;
        this.defaultShell.ItemsSource = null;
        this.defaultShell.ItemsSource = this.Result.Shells;
        this.defaultShell.SelectedItem = this.Result.Shells.FirstOrDefault(s => s.Id == id);
    }

    private void Move(int delta)
    {
        if (this.tools.SelectedItem is not ToolEntry t)
        {
            return;
        }

        int index = this.Result.Tools.IndexOf(t), next = index + delta;
        if (next < 0 || next >= this.Result.Tools.Count)
        {
            return;
        }

        this.Result.Tools.RemoveAt(index);
        this.Result.Tools.Insert(next, t);
        this.Refresh();
        this.tools.SelectedItem = t;
    }

    private void EditTool(bool add)
    {
        if (!add && this.tools.SelectedItem is not ToolEntry)
        {
            return;
        }

        var original = this.tools.SelectedItem as ToolEntry;
        var draft = add ? new ToolEntry { ShellId = this.Result.DefaultShellId } : Store.Clone(original!);
        var dialog = new ToolEditor(this.Result, draft) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (add)
        {
            this.Result.Tools.Add(draft);
        }
        else
        {
            this.Result.Tools[this.Result.Tools.IndexOf(original!)] = draft;
        }

        this.Refresh();
        this.tools.SelectedItem = draft;
    }

    private void EditShell(bool add)
    {
        if (!add && this.shells.SelectedItem is not ShellProfile)
        {
            return;
        }

        var original = this.shells.SelectedItem as ShellProfile;
        var s = add ? new ShellProfile() : Store.Clone(original!);
        var dialog = new Window
        {
            Title = "Shell profile",
            Icon = Ui.AppIcon,
            Width = 500,
            Height = 410,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var body = new StackPanel { Margin = new Thickness(12) };
        dialog.Content = body;
        var name = Ui.Input(s.Name);
        Ui.Field(body, "Name", name);
        var exe = Ui.Input(s.Executable);
        Ui.Field(body, "Executable: PATH name, absolute path, or manager-relative path", exe);
        var kind = new ComboBox
        {
            ItemsSource = new[] { "PowerShell", "CMD" },
            SelectedItem = s.Kind,
            Margin = new Thickness(4),
        };
        Ui.Field(body, "Command language", kind);
        var loadProfile = new CheckBox
        {
            Content = "Load PowerShell profiles",
            IsChecked = s.LoadProfile,
            Margin = new Thickness(5),
            IsEnabled = s.Kind == "PowerShell",
        };
        body.Children.Add(loadProfile);
        kind.SelectionChanged += (_, _) => loadProfile.IsEnabled = (string?)kind.SelectedItem == "PowerShell";
        body.Children.Add(Ui.Button("Save", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(exe.Text))
            {
                MessageBox.Show("Name and executable are required.");
                return;
            }

            s.Name = name.Text.Trim();
            s.Executable = exe.Text.Trim();
            s.Kind = (string)kind.SelectedItem;
            s.LoadProfile = loadProfile.IsChecked == true;
            dialog.DialogResult = true;
        }));
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (add)
        {
            this.Result.Shells.Add(s);
        }
        else
        {
            this.Result.Shells[this.Result.Shells.IndexOf(original!)] = s;
        }

        this.Refresh();
        this.shells.SelectedItem = s;
    }

    private void Save()
    {
        try
        {
            this.Result.StopTimeoutSeconds = int.Parse(this.timeout.Text);
            this.Result.HistoryCount = int.Parse(this.history.Text);
            this.Result.OutputLimitKiB = int.Parse(this.output.Text);
            this.Result.GlobalOutputLimitMiB = int.Parse(this.total.Text);
            Store.Save(this.Result);
            this.DialogResult = true;
        }
        catch (Exception e)
        {
            Ui.Error(e);
        }
    }
}
