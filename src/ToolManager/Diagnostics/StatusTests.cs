namespace ToolManager.Diagnostics;

using System.Globalization;
using ToolManager.Execution;
using ToolManager.Models;
using ToolManager.Persistence;

internal static class StatusTests
{
    public static async Task RunAsync()
    {
        var record = new RunRecord { State = "Running" };
        var filter = new PlainOutput();
        var parser = new StatusOutput(CustomStatusSource.ExplicitStatusLine);
        int changes = 0;
        record.Changed += (_, _) => changes++;
        void Feed(string text) => parser.Append(filter.Filter(text), record);
        Feed("log\n\x1b[3");
        Feed("2m##TM[STA");
        Feed("TUS]:  Ping\t12 ms\u2028  Success\u202e 99%\x1b[0m\r");
        Feed("\nordinary output\n");
        Require(record.DisplayStatus == "Ping 12 ms Success 99%" && changes == 1, "Split prefixes, escapes, CRLF and unsafe characters are handled without accepting ordinary output.");
        Feed("##TM[STATUS]:Ping 12 ms Success 99%\n");
        Require(changes == 1, "Unchanged status does not notify observers.");
        Feed("##TM[STATUS]:\n");
        Require(record.DisplayStatus == "Running", "An empty explicit status clears the custom text.");
        Feed("##TM[STATUS]:" + string.Concat(Enumerable.Repeat("😀e\u0301", 100)) + "\n");
        Require(new StringInfo(record.CustomStatus).LengthInTextElements == 120 && record.CustomStatus.EndsWith("e\u0301", StringComparison.Ordinal), "Status is limited to 120 text elements without splitting emoji or combining characters.");
        Feed("##TM[STATUS]:" + new string('x', 100000) + "\n");
        Require(record.CustomStatus == new string('x', 120), "Oversized lines are bounded and truncated.");
        Feed("##TM[STATUS]:Recovered\n");
        Require(record.CustomStatus == "Recovered", "Parsing recovers after oversized output.");
        Feed("##TM[STATUS]:final partial line");
        parser.Complete(record);
        Require(record.CustomStatus == "final partial line", "The final unterminated line is processed.");
        record.Append("retained output", 100);
        record.Trim(0);
        Require(record.CustomStatus == "final partial line", "Output retention does not erase status.");
        foreach (var state in new[] { "Starting", "Stopping", "Stopped", "Force-stopped", "Failed", "Finished" })
        {
            record.State = state;
            Require(record.DisplayStatus == state, "Authoritative state replaces custom text: " + state);
        }

        record.State = "Running";
        record.Ended = DateTime.Now;
        Require(record.DisplayStatus == "Running", "An ended Custom Check cannot display stale custom text.");
        var latest = new RunRecord { State = "Running" };
        var lastLine = new StatusOutput(CustomStatusSource.LastOutputLine);
        lastLine.Append("first\n \t\nsecond\n", latest);
        Require(latest.DisplayStatus == "second", "Last output line skips blank lines and selects the latest completed line.");
        new StatusOutput(CustomStatusSource.None).Append("##TM[STATUS]:ignored\n", latest);
        Require(latest.CustomStatus == "second", "None does not consume output as custom status.");
        Require(JsonSerializer.Deserialize<ToolEntry>("{}", Store.Json)!.CustomStatus == CustomStatusSource.None, "Existing configuration defaults to None.");

        foreach (var (source, label) in new[]
        {
            (CustomStatusSource.None, "None"),
            (CustomStatusSource.LastOutputLine, "Last output line"),
            (CustomStatusSource.ExplicitStatusLine, "Explicit status line"),
        })
        {
            var json = JsonSerializer.Serialize(new ToolEntry { CustomStatus = source }, Store.Json);
            using var document = JsonDocument.Parse(json);
            Require(document.RootElement.GetProperty("CustomStatus").GetString() == label, "Configuration preserves the existing status source name.");
            var existingJson = "{\"CustomStatus\":\"" + label + "\"}";
            Require(JsonSerializer.Deserialize<ToolEntry>(existingJson, Store.Json)!.CustomStatus == source, "Existing status source names deserialize to enums.");
        }

        var config = new Configuration { StopTimeoutSeconds = 1 };
        config.Shells.ForEach(shell => shell.LoadProfile = false);
        var tool = new ToolEntry { Command = "unused", CustomStatus = (CustomStatusSource)(-1) };
        config.Tools.Add(tool);
        try
        {
            Store.Validate(config);
            throw new InvalidOperationException("Invalid status sources must fail validation.");
        }
        catch (InvalidDataException error) when (error.Message.Contains("Custom Status", StringComparison.Ordinal))
        {
        }

        tool.CustomStatus = CustomStatusSource.ExplicitStatusLine;
        Require(Store.Clone(tool).CustomStatus == tool.CustomStatus, "Custom Status survives configuration serialization.");
        var manager = new RunManager(config);
        foreach (var shell in config.Shells)
        {
            tool.ShellId = shell.Id;
            tool.Command = shell.Kind == "CMD"
                ? "@echo off\r\necho ##TM[STATUS]:Ping 12 ms\r\necho ordinary output\r\necho diagnostic 1>&2\r\nping -n 30 127.0.0.1 >nul"
                : "[Console]::WriteLine('##TM[STATUS]:Ping 12 ms'); [Console]::WriteLine('ordinary output'); [Console]::Error.WriteLine('diagnostic'); Start-Sleep -Seconds 30";
            int notifications = 0;
            EventHandler observer = (_, _) => notifications++;
            manager.Changed += observer;
            try
            {
                var run = await manager.StartAsync(tool);
                Require(run.CustomStatus.Length == 0, "A new run starts without stale custom status.");
                await WaitAsync(() => run.CustomStatus == "Ping 12 ms" && run.Output.Contains("diagnostic", StringComparison.Ordinal));
                Require(run.DisplayStatus == "Ping 12 ms" && notifications > 1, "Live shell status notifies observers: " + shell.Name);
                Require(run.Output.Contains(StatusOutput.Prefix, StringComparison.Ordinal) && run.Output.Contains("ordinary output", StringComparison.Ordinal), "Status extraction preserves output history.");
                await manager.StopAsync(tool.Id);
                Require(run.DisplayStatus == "Stopping", "Stopping immediately replaces custom text.");
                await WaitAsync(() => !run.Active);
                Require(run.DisplayStatus is "Stopped" or "Force-stopped", "Stopped shells display only their authoritative state.");
            }
            finally
            {
                await manager.StopAllAsync();
                manager.Changed -= observer;
            }
        }
    }

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
                throw new TimeoutException("Timed out waiting for custom status.");
            }

            await Task.Delay(25);
        }
    }
}
