namespace ToolManager;

internal static class SelfTest
{
    private static readonly List<string> Results = [];

    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "self-test workspace " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Store.Root = root;
        try
        {
            var startup = new StartupTask("S-1-5-21-1-2-3-1001", @"C:\Portable & tools\ToolManager.exe");
            var definition = System.Xml.Linq.XDocument.Parse(startup.Definition());
            System.Xml.Linq.XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            string Value(string name) => definition.Descendants(taskNamespace + name).First().Value;
            Assert(Value("Command") == @"C:\Portable & tools\ToolManager.exe" && Value("WorkingDirectory") == @"C:\Portable & tools", "Startup executable paths preserve spaces and XML characters");
            Assert(definition.Descendants(taskNamespace + "UserId").All(value => value.Value == "S-1-5-21-1-2-3-1001"), "Startup trigger and principal retain the original user through elevation");
            Assert(Value("LogonType") == "InteractiveToken" && Value("RunLevel") == "LeastPrivilege", "Startup uses the interactive desktop with normal privileges and no password");
            Assert(Value("ExecutionTimeLimit") == "PT0S" && Value("DisallowStartIfOnBatteries") == "false" && Value("StopIfGoingOnBatteries") == "false", "Startup works on battery without a runtime limit");
            Assert(startup.Name == new StartupTask("S-1-5-21-1-2-3-1001", @"c:\portable & tools\toolmanager.exe").Name, "Startup identity is stable across executable path casing");
            Assert(startup.Name != new StartupTask("S-1-5-21-1-2-3-1002", @"C:\Portable & tools\ToolManager.exe").Name && startup.Name != new StartupTask("S-1-5-21-1-2-3-1001", @"C:\Other\ToolManager.exe").Name, "Startup tasks are isolated by user and portable copy");
            var c = new Configuration
            {
                StopTimeoutSeconds = 1,
                HistoryCount = 2,
                OutputLimitKiB = 16,
            };
            foreach (var profile in c.Shells)
            {
                profile.LoadProfile = false;
            }

            var m = new RunManager(c);
            ToolEntry Entry(string command, string shell = "pwsh")
            {
                var e = new ToolEntry
                {
                    Name = "Test",
                    ShellId = shell,
                    Command = command,
                };
                c.Tools.Add(e);
                return e;
            }

            async Task<RunRecord> Run(ToolEntry e)
            {
                var r = await m.StartAsync(e);
                await Wait(() => !r.Active);
                return r;
            }

            var invalidConfig = new Configuration();
            invalidConfig.Tools.Add(null!);
            bool invalidEntryRejected = false;
            try
            {
                Store.Validate(invalidConfig);
            }
            catch (InvalidDataException)
            {
                invalidEntryRejected = true;
            }

            Assert(invalidEntryRejected, "Null configuration entries produce a validation error");
            var invalidRunnerStart = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            invalidRunnerStart.ArgumentList.Add("--runner");
            using (var invalidRunner = Process.Start(invalidRunnerStart)!)
            {
                await invalidRunner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert(invalidRunner.ExitCode == 1, "Invalid helper arguments exit without opening a desktop error dialog");
            }

            var early = Entry("Start-Sleep -Seconds 60");
            var earlyRun = await m.StartAsync(early);
            await m.StopAsync(early.Id);
            try
            {
                await Wait(() => !earlyRun.Active);
            }
            catch
            {
                throw new Exception("Startup stop: " + earlyRun.State + " deadline=" + earlyRun.StopDeadline + " requested=" + earlyRun.StopRequested + " output=" + earlyRun.Output);
            }

            Assert(earlyRun.State is "Stopped" or "Force-stopped", "Stop during startup is delivered: " + earlyRun.State + " " + earlyRun.Output);
            var script = Entry("Write-Output 'héllo 世界'; [Console]::Error.WriteLine('error stream'); Write-Output $env:TOOLMANAGER_ROOT; exit 7");
            var result = await Run(script);
            Assert(result.ExitCode == 7 && result.State == "Failed", "PowerShell exit code is preserved");
            Assert(result.Output.Contains("héllo 世界") && result.Output.Contains("error stream"), "UTF-8 stdout and stderr captured");
            Assert(result.Output.Contains(root), "Portable manager root is available to scripts");
            foreach (var shell in new[] { "powershell", "cmd" })
            {
                result = await Run(Entry(shell == "cmd" ? "@echo shell-ok\r\n@exit /b 3" : "Write-Output 'shell-ok'; exit 3", shell));
                Assert(result.ExitCode == 3 && result.Output.Contains("shell-ok"), shell + " execution and exit code");
            }

            await File.WriteAllTextAsync(Path.Combine(root, "parameter test.ps1"), "param([string]$Value)\nWrite-Output $Value");
            result = await Run(Entry("& '.\\parameter test.ps1' -Value 'argument with spaces'"));
            Assert(result.Output.Contains("argument with spaces") && result.ExitCode == 0, "Relative script path and parameters with spaces");
            result = await Run(Entry("1..3 | ForEach-Object { Write-Output ('line-' + $_) }\nWrite-Output 'done'"));
            Assert(result.Output.Contains("line-3") && result.Output.Contains("done"), "Multiline scripts and cmdlets");
            foreach (var failing in new[] { "DefinitelyMissingToolManagerCommand", "throw 'Expected terminating failure'", "[Console]::Error.Write('stderr only'); exit 5", "exit 0" })
            {
                result = await Run(Entry(failing));
                Assert(result.ExitCode is not null && !result.Output.Contains("Pipe is broken"), "Short/silent/error run reports shell result without broken-pipe error");
            }

            var plain = new PlainOutput();
            string ansi = "\x1b[31;1mred\x1b[0m \x1b]8;;https://example.invalid\x1b\\link\x1b]8;;\x1b\\\r\nnext\rline";
            var filtered = string.Concat(ansi.Select(ch => plain.Filter(ch.ToString())));
            Assert(filtered == "red link\nnext\nline", "ANSI colors and OSC hyperlinks stripped across single-character chunks");
            result = await Run(Entry("[Console]::Write(([char]27)+'[31mcolored'+([char]27)+'[0m')"));
            Assert(result.Output.Contains("colored") && !result.Output.Contains('\x1b'), "Raw ANSI sequences removed from captured output");
            var oldShell = JsonSerializer.Deserialize<ShellProfile>("{\"Id\":\"old\",\"Name\":\"Old\"}", Store.Json)!;
            Assert(oldShell.LoadProfile, "Existing shell configurations default to loading profiles");
            c.Shells[0].LoadProfile = true;
            result = await Run(Entry("if (Get-Command Type-Clipboard -ErrorAction Ignore) { 'PROFILE_FUNCTION_AVAILABLE' }; 'PROFILE_RUN_COMPLETED'"));
            Assert(result.ExitCode == 0 && result.Output.Contains("PROFILE_RUN_COMPLETED"), "PowerShell runs with profile loading enabled");
            if (result.Output.Contains("PROFILE_FUNCTION_AVAILABLE"))
            {
                Results.Add("PASS User's Type-Clipboard profile function resolved (not invoked)");
            }

            c.Shells[0].LoadProfile = false;
            foreach (var shell in new[] { "pwsh", "powershell", "cmd" })
            {
                var entry = Entry(shell == "cmd" ? "@echo startup-ready" : "$sessionValue='persisted'; Write-Output 'startup-ready'", shell);
                entry.Interactive = true;
                var run = await m.StartAsync(entry);
                await Wait(() => run.Output.Contains("startup-ready") || !run.Active);
                Assert(run.Active && run.State == "Running", shell + " interactive shell remains open after startup script");
                await m.SendInputAsync(run, shell == "cmd" ? "echo sent-command-ok" : "Write-Output ('sent-command-ok-'+$sessionValue)");
                await Wait(() => run.Output.Contains(shell == "cmd" ? "sent-command-ok" : "sent-command-ok-persisted") || !run.Active);
                Assert(run.Active && run.Output.Contains(shell == "cmd" ? "sent-command-ok" : "sent-command-ok-persisted"), shell + " accepts commands in the same session");
                if (shell != "cmd")
                {
                    await m.SendInputAsync(run, "$answer=Read-Host 'Answer'; Write-Output ('received-'+$answer)");
                    await Task.Delay(200);
                    await m.SendInputAsync(run, "text response");
                    await Wait(() => run.Output.Contains("received-text response") || !run.Active);
                    Assert(run.Output.Contains("received-text response"), shell + " accepts Read-Host responses");
                }

                await m.SendInputAsync(run, "exit");
                await Wait(() => !run.Active);
                Assert(!run.Output.Contains("Pipe is broken"), shell + " interactive exit closes cleanly");
                bool rejected = false;
                try
                {
                    await m.SendInputAsync(run, "ignored");
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }

                Assert(rejected, "Input to completed history is rejected");
            }

            var interactiveStop = Entry("'interactive-stop-ready'");
            interactiveStop.Interactive = true;
            var stoppedShell = await m.StartAsync(interactiveStop);
            await Wait(() => stoppedShell.Output.Contains("interactive-stop-ready") || !stoppedShell.Active);
            await m.StopAsync(interactiveStop.Id);
            await Wait(() => !stoppedShell.Active);
            Assert(stoppedShell.State is "Stopped" or "Force-stopped", "Interactive shell still honors Stop and timeout");
            var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Fixture.exe");
            Assert(File.Exists(fixture), "Native console fixture exists");
            foreach (var shell in new[] { "pwsh", "powershell", "cmd" })
            {
                foreach (var mode in new[] { "cooperate", "ignore", "child" })
                {
                    var marker = Path.Combine(root, shell + "-" + mode + ".txt");
                    var call = shell == "cmd" ? $"\"{fixture}\" {mode} \"{marker}\"" : $"& '{fixture.Replace("'", "''")}' {mode} '{marker.Replace("'", "''")}'";
                    var entry = Entry(call, shell);
                    var run = await m.StartAsync(entry);
                    await Wait(() => run.Output.Contains("Fixture ready") || !run.Active);
                    Assert(run.Active, shell + " " + mode + " started");
                    Assert(run.Output.Contains("Console hidden: True"), shell + " " + mode + " console starts hidden without a later ShowWindow call");
                    bool duplicate = false;
                    try
                    {
                        await m.StartAsync(entry);
                    }
                    catch (InvalidOperationException)
                    {
                        duplicate = true;
                    }

                    Assert(duplicate, "Duplicate run rejected");
                    if (mode == "child")
                    {
                        await Wait(() => File.Exists(marker + ".pid"));
                    }

                    var watch = Stopwatch.StartNew();
                    await m.StopAsync(entry.Id);
                    await Wait(() => !run.Active);
                    Assert(File.Exists(marker) && File.ReadAllText(marker).Contains("Ctrl+C:0"), shell + " " + mode + " received actual Ctrl+C");
                    if (mode == "cooperate")
                    {
                        Assert(run.State == "Stopped" && watch.Elapsed.TotalSeconds < 3, "Cooperative shutdown");
                    }
                    else if (shell == "cmd")
                    {
                        Assert(run.State == "Stopped" || (run.State == "Force-stopped" && watch.Elapsed.TotalSeconds >= 0.8), "CMD session exits after Ctrl+C or is forced after timeout: " + run.State);
                    }
                    else
                    {
                        Assert(run.State == "Force-stopped" && watch.Elapsed.TotalSeconds >= 0.8, "Forced shutdown after timeout");
                    }

                    if (mode == "child")
                    {
                        var pid = int.Parse(File.ReadAllText(marker + ".pid"));
                        bool alive;
                        try
                        {
                            using var child = Process.GetProcessById(pid);
                            alive = !child.HasExited;
                        }
                        catch (ArgumentException)
                        {
                            alive = false;
                        }

                        Assert(!alive, "Child process terminated with owning job");
                    }
                }
            }

            var flood = Entry("[Console]::Write(('x' * 100000)); Write-Output 'tail'");
            result = await Run(flood);
            Assert(result.Length <= c.OutputLimitKiB * 1024 / 2 && result.Output.Contains("discarded") && result.Output.Contains("tail"), "Output is bounded and truncation marked");
            for (int i = 0; i < 3; i++)
            {
                await Run(flood);
            }

            Assert(m.History.Count(r => r.ToolId == flood.Id) == 2, "Completed history retention");
            var custom = Entry("Write-Output started");
            custom.Behavior = "Custom";
            custom.StopCommand = "Write-Output stopped";
            custom.StatusCommand = "exit 1";
            var customStop = await m.StartAsync(custom, "Stop");
            await Wait(() => !customStop.Active);
            Assert(customStop.Output.Contains("stopped") && customStop.ExitCode == 0, "Custom stop script executes");
            var customCheck = await m.StartAsync(custom, "Check");
            await Wait(() => !customCheck.Active);
            Assert(customCheck.ExitCode == 1, "Custom status script preserves state exit code");
            var global = new RunManager(new Configuration
            {
                HistoryCount = 100,
                GlobalOutputLimitMiB = 4,
            });
            for (int i = 0; i < 100; i++)
            {
                var r = new RunRecord
                {
                    ToolId = "global",
                    Ended = DateTime.Now,
                };
                r.Append(new string('z', 40000), 40000);
                global.History.Add(r);
            }

            global.Prune();
            Assert(global.History.Sum(r => (long)r.Length) <= 4L * 1024 * 1024 / 2, "Global output budget enforced");
            var bounded = new RunRecord();
            bounded.Append(new string('y', 10000), 200);
            Assert(bounded.Length == 200, "Single large output chunk bounded");
            var pc = new ToolEntry
            {
                RestrictToPcs = true,
                PcNames = [" " + Environment.MachineName.ToLowerInvariant() + " "],
            };
            Assert(pc.Visible, "PC filtering is case-insensitive and trimmed");
            pc.PcNames = [];
            Assert(!pc.Visible, "Empty restricted PC list hides entry");
            var project = Directory.GetParent(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName)!.FullName;
            if (File.Exists(Path.Combine(project, "ToolManager.csproj")))
            {
                Assert(Store.ApplicationRoot(Path.Combine(project, "bin", "Release", "net10.0-windows")) == Path.Combine(project, "outputs", "ToolManager"), "Development builds use the existing portable configuration root");
            }

            Assert(Store.ApplicationRoot(Path.Combine(root, "copied-portable-app")) == Path.Combine(root, "copied-portable-app"), "Distributed copies keep their own portable root");
            c.Tools.Clear();
            var portable = Entry("Write-Output ok");
            portable.Mode = "Bundled";
            portable.WorkingDirectory = @"C:\\";
            bool invalid = false;
            try
            {
                Store.Validate(c);
            }
            catch (InvalidDataException)
            {
                invalid = true;
            }

            Assert(invalid, "Bundled absolute working directory rejected");
            portable.WorkingDirectory = ".";
            portable.PcNames = ["test", "TEST", " ", Environment.MachineName];
            Store.Save(c);
            Store.Save(c);
            Assert(File.Exists(Store.ConfigPath + ".bak") && Store.Load().Tools[0].PcNames.Count == 2, "Atomic config save, backup, and distinct PC names");
            File.Move(Store.ConfigPath, Store.ConfigPath + ".saved-for-test");
            Assert(Store.Load().Tools.Count == 1 && File.Exists(Store.ConfigPath), "Missing configuration recovers from valid backup instead of creating empty settings");
            result = await Run(new ToolEntry
            {
                ShellId = "pwsh",
                Command = "'unused'",
                WorkingDirectory = "missing-directory",
            });
            Assert(result.State == "Failed" && result.Output.Contains("does not exist"), "Missing working directory produces actionable error");
            c.Shells[0].Executable = "definitely-missing-shell.exe";
            result = await Run(portable);
            Assert(result.State == "Failed" && result.Output.Contains("not found in PATH"), "Missing shell produces actionable error");
            Assert(!Directory.EnumerateFiles(root, "request.json", SearchOption.AllDirectories).Any(), "Temporary run requests cleaned up");
            await File.WriteAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), Results);
            return 0;
        }
        catch (Exception e)
        {
            Results.Add("FAIL " + e);
            await File.WriteAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), Results);
            return 1;
        }
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok)
        {
            throw new Exception(message);
        }

        Results.Add("PASS " + message);
    }

    private static async Task Wait(Func<bool> condition, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("Test timed out.");
            }

            await Task.Delay(30);
        }
    }
}
