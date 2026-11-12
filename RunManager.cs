namespace ToolManager;

using System.IO.Pipes;

public sealed class RunManager
{
    private readonly Dictionary<string, (StreamWriter Writer, SemaphoreSlim Gate)> controls = [];

    private readonly Dictionary<string, RunRecord> active = [];

    public RunManager(Configuration config) => this.Config = config;

    public Configuration Config
    { get; set; }

    public List<RunRecord> History { get; } = [];

    public bool IsActive(string toolId) => this.active.ContainsKey(toolId);

    public RunRecord? Current(string toolId) => this.active.GetValueOrDefault(toolId);

    public Task<RunRecord> StartAsync(ToolEntry entry, string action = "Run")
    {
        if (this.IsActive(entry.Id))
        {
            throw new InvalidOperationException("This entry already has an active run.");
        }

        var record = new RunRecord
        {
            ToolId = entry.Id,
            Action = action,
            Interactive = entry.Interactive,
        };
        this.active.Add(entry.Id, record);
        this.History.Add(record);
        this.Prune();
        _ = this.ExecuteAsync(entry, record);
        return Task.FromResult(record);
    }

    public async Task StopAsync(string toolId)
    {
        if (!this.active.TryGetValue(toolId, out var record) || record.StopDeadline != null)
        {
            return;
        }

        record.StopRequested = true;
        if (this.controls.ContainsKey(record.Id))
        {
            record.State = "Stopping";
            var entry = this.Config.Tools.FirstOrDefault(t => t.Id == toolId);
            record.StopDeadline = DateTime.Now.AddSeconds(entry?.StopTimeoutSeconds ?? this.Config.StopTimeoutSeconds);
            try
            {
                await this.SendControlAsync(record, new("stop"));
            }
            catch (IOException) { }
        }

        // A stop during startup is delivered immediately after the runner connects.
        else
        {
            record.State = "Stopping";
        }
    }

    public async Task SendInputAsync(RunRecord record, string text)
    {
        if (!record.Active || !record.Interactive || record.State != "Running")
        {
            throw new InvalidOperationException("Select an active interactive run before sending input.");
        }

        if (text.Length > RunRequest.MaximumInputCharacters || text.Contains('\r') || text.Contains('\n'))
        {
            throw new InvalidOperationException("Send one line at a time (up to 4096 characters).");
        }

        try
        {
            await this.SendControlAsync(record, new("input", text));
        }
        catch (IOException)
        {
            throw new InvalidOperationException("The run has ended or its input stream is closed.");
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var id in this.active.Keys.ToArray())
        {
            await this.StopAsync(id);
        }

        while (this.active.Count > 0)
        {
            await Task.Delay(100);
        }
    }

    public void Prune()
    {
        foreach (var group in this.History.Where(r => !r.Active).GroupBy(r => r.ToolId).ToArray())
        {
            foreach (var record in group.OrderByDescending(r => r.Started).Skip(this.Config.HistoryCount).ToArray())
            {
                this.History.Remove(record);
            }
        }

        var over = this.History.Sum(r => (long)r.Length) - (this.Config.GlobalOutputLimitMiB * 1024L * 1024 / 2);
        foreach (var r in this.History.OrderBy(r => r.Active).ThenBy(r => r.Started))
        {
            if (over <= 0)
            {
                break;
            }

            var remove = (int)Math.Min(over, r.Length);
            r.Trim(r.Length - remove);
            over -= remove;
        }
    }

    private static void DeleteRunDirectory(string? directory)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // A tool or sync client can still hold a temporary run file open.
        }
        catch (UnauthorizedAccessException)
        {
            // Temporary files stay inside the portable root if cleanup is denied.
        }
    }

    private static string FindShell(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains('\\') || executable.Contains('/'))
        {
            var path = Store.Resolve(executable);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Shell not found: " + path);
            }

            return path;
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var path = Path.Combine(folder.Trim('"'), executable);
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        throw new FileNotFoundException($"Shell '{executable}' was not found in PATH. Configure its executable in Settings.");
    }

    private static async Task StopHelperAsync(Process? helper)
    {
        if (helper is null)
        {
            return;
        }

        using (helper)
        {
            try
            {
                if (!helper.HasExited)
                {
                    helper.Kill();
                }
            }
            catch (Exception exception) when ((exception is InvalidOperationException or Win32Exception) && helper.HasExited)
            {
                // The runner can exit between the state check and Kill.
            }

            await helper.WaitForExitAsync();
        }
    }

    private async Task SendControlAsync(RunRecord record, RunnerMessage message)
    {
        if (!this.controls.TryGetValue(record.Id, out var channel))
        {
            throw new IOException("Run input is not available yet.");
        }

        await channel.Gate.WaitAsync();
        try
        {
            await channel.Writer.WriteLineAsync(JsonSerializer.Serialize(message));
        }
        finally
        {
            channel.Gate.Release();
        }
    }

    private async Task<string> CreateRequestAsync(ToolEntry entry, RunRecord record, string directory)
    {
        var profile = this.Config.Shells.First(s => s.Id == entry.ShellId);
        var shell = FindShell(profile.Executable);
        var workingDirectory = Store.Resolve(entry.WorkingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException("Working directory does not exist: " + workingDirectory);
        }

        Directory.CreateDirectory(directory);
        var script = record.Action switch
        {
            "Stop" => entry.StopCommand,
            "Check" => entry.StatusCommand,
            _ => entry.Command,
        };
        var path = Path.Combine(directory, profile.Kind == "CMD" ? "command.cmd" : "command.ps1");

        // BOM preserves non-ASCII scripts in Windows PowerShell 5.1.
        script = profile.Kind == "CMD" ? "@chcp 65001 >nul\r\n" + script : "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n$OutputEncoding = [Console]::OutputEncoding\nif (Get-Variable PSStyle -ErrorAction Ignore) { $PSStyle.OutputRendering = 'PlainText' }\n" + script;
        await File.WriteAllTextAsync(path, script, profile.Kind == "CMD" ? new UTF8Encoding(false) : new UTF8Encoding(true));
        var command = profile.Kind == "CMD"
            ? $"\"{shell}\" /d /s {(entry.Interactive ? "/k" : "/c")} \"\"{path}\"\""
            : $"\"{shell}\" -NoLogo {(profile.LoadProfile ? string.Empty : "-NoProfile")} {(entry.Interactive ? "-NoExit" : "-NonInteractive")} -File \"{path}\"";
        var requestPath = Path.Combine(directory, "request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(
            new RunRequest
            {
                Root = Store.Root,
                Shell = shell,
                CommandLine = command,
                Directory = workingDirectory,
                Timeout = entry.StopTimeoutSeconds ?? this.Config.StopTimeoutSeconds,
                Interactive = entry.Interactive,
            },
            Store.Json));

        return requestPath;
    }

    private async Task ExecuteAsync(ToolEntry entry, RunRecord record)
    {
        string? directory = null;
        Process? helper = null;
        try
        {
            directory = Path.Combine(Store.Root, "data", Environment.MachineName, "runs", record.Id);
            var requestPath = await this.CreateRequestAsync(entry, record, directory);
            var pipeName = "toolmanager-" + record.Id;
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,

                // AllocConsole uses this startup setting to stay hidden from creation.
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            start.ArgumentList.Add("--runner");
            start.ArgumentList.Add(requestPath);
            start.ArgumentList.Add(pipeName);
            if (record.StopRequested)
            {
                record.State = "Stopped";
                return;
            }

            helper = Process.Start(start) ?? throw new IOException("Could not start process runner.");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                await pipe.WaitForConnectionAsync(timeout.Token);
            }

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

            bool exited = false, started = false;
            var stdout = new PlainOutput();
            var stderr = new PlainOutput();
            var startupDeadline = DateTime.UtcNow.AddSeconds(20);
            var pendingRead = reader.ReadLineAsync();
            while (true)
            {
                if (!pendingRead.IsCompleted)
                {
                    if (!started && DateTime.UtcNow > startupDeadline)
                    {
                        throw new TimeoutException("Runner did not complete startup within 20 seconds.");
                    }

                    if (record.StopDeadline is DateTime deadline && DateTime.Now > deadline.AddSeconds(3))
                    {
                        if (!helper.HasExited)
                        {
                            helper.Kill();
                        }

                        record.State = "Force-stopped";
                        record.Append("[manager] Runner shutdown watchdog expired; owned processes terminated.\n", this.Config.OutputLimitKiB * 1024 / 2);
                        exited = true;
                        break;
                    }

                    await Task.WhenAny(pendingRead, Task.Delay(200));
                    continue;
                }

                var line = await pendingRead;
                if (line is null)
                {
                    break;
                }

                var m = JsonSerializer.Deserialize<RunnerMessage>(line)!;
                switch (m.Kind)
                {
                    case "stdout":
                        record.Append(stdout.Filter(m.Text), this.Config.OutputLimitKiB * 1024 / 2);
                        this.Prune();
                        break;
                    case "stderr":
                        var clean = stderr.Filter(m.Text);
                        if (clean.Length > 0)
                        {
                            record.Append("[stderr] " + clean, this.Config.OutputLimitKiB * 1024 / 2);
                        }

                        this.Prune();
                        break;
                    case "started":
                        started = true;
                        this.controls[record.Id] = (writer, new SemaphoreSlim(1));
                        if (record.StopRequested)
                        {
                            await this.StopAsync(entry.Id);
                        }
                        else
                        {
                            record.State = "Running";
                        }

                        record.Append("[manager] Process session started.\n", this.Config.OutputLimitKiB * 1024 / 2);
                        break;
                    case "stopping":
                        record.State = "Stopping";
                        record.Append("[manager] Ctrl+C sent; waiting for shutdown.\n", this.Config.OutputLimitKiB * 1024 / 2);
                        break;
                    case "notice":
                        record.Append("[manager] " + m.Text + "\n", this.Config.OutputLimitKiB * 1024 / 2);
                        break;
                    case "error":
                        throw new IOException(m.Text);
                    case "exit":
                        record.ExitCode = m.Code;
                        record.State = m.Text == "Finished" && m.Code != 0 ? "Failed" : m.Text;
                        exited = true;
                        break;
                }

                if (exited)
                {
                    break; // Do not read or flush again after the peer reports completion.
                }

                pendingRead = reader.ReadLineAsync();
            }

            if (!exited)
            {
                throw new IOException("Runner disconnected before reporting completion.");
            }

            await helper.WaitForExitAsync();
        }
        catch (Exception e)
        {
            record.State = "Failed";
            record.Append("[manager] " + e.Message + "\n", this.Config.OutputLimitKiB * 1024 / 2);
        }
        finally
        {
            this.controls.Remove(record.Id);
            try
            {
                await StopHelperAsync(helper);
                DeleteRunDirectory(directory);
            }
            catch (Exception exception)
            {
                record.State = "Failed";
                record.Append("[manager] Cleanup failed: " + exception.Message + "\n", this.Config.OutputLimitKiB * 1024 / 2);
            }
            finally
            {
                this.active.Remove(entry.Id);
                record.Ended = DateTime.Now;
                this.Prune();
            }
        }
    }
}
