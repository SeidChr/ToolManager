namespace ToolManager.Diagnostics;

using ToolManager.Execution;
using ToolManager.Models;
using ToolManager.Persistence;

internal static class CronTests
{
    public static async Task RunAsync()
    {
        var config = new Configuration { StopTimeoutSeconds = 1 };
        config.Shells.ForEach(shell => shell.LoadProfile = false);
        var tool = new ToolEntry
        {
            Name = "Cron test",
            Behavior = "Cron",
            ShellId = "powershell",
            Command = "Write-Output 'cron-ready'",
            CronExpression = "invalid",
        };
        config.Tools.Add(tool);
        try
        {
            Store.Validate(config);
            throw new InvalidOperationException("Invalid cron expressions must fail validation.");
        }
        catch (InvalidDataException error) when (error.Message.Contains(tool.Name, StringComparison.Ordinal))
        {
        }

        tool.CronExpression = "0 0 30 2 *";
        try
        {
            Store.Validate(config);
            throw new InvalidOperationException("Cron expressions without a future occurrence must fail validation.");
        }
        catch (InvalidDataException error) when (error.Message.Contains("no future occurrence", StringComparison.Ordinal))
        {
        }

        tool.CronExpression = "0 9 * * MON-FRI";
        Store.Validate(config);
        Require(Store.Clone(config).Tools.Single().CronExpression == tool.CronExpression, "Cron schedules survive configuration serialization.");

        tool.CronExpression = "* * * * * *";
        await VerifyRecurrenceAsync(tool);

        config.Shells.Single(shell => shell.Id == tool.ShellId).Executable = Path.Combine(Store.Root, "missing-" + Guid.NewGuid().ToString("N") + ".exe");
        var manager = new RunManager(config);
        await manager.StartToolAsync(tool);
        Require(manager.HasActiveTools && manager.IsActive(tool.Id) && manager.ScheduleState(tool.Id) == "Scheduled" && manager.History.Count == 0, "Starting cron arms the schedule without creating an immediate run.");

        await WaitAsync(() => manager.History.Any(run => !run.Active));
        Require(manager.History[0].State == "Failed" && manager.IsActive(tool.Id), "A failed occurrence reports its error and leaves the schedule armed.");
        await manager.StopAllAsync();
        Require(!manager.HasActiveTools && !manager.IsActive(tool.Id), "Stopping all disarms cron.");
        var count = manager.History.Count;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Require(manager.History.Count == count, "Stopped cron schedules do not start later occurrences.");

        tool.CronExpression = "0 0 1 1 *";
        await manager.StartToolAsync(tool);
        Require(manager.IsActive(tool.Id), "Cron can be restarted after stopping.");
        await manager.StopAllAsync();
        Require(!manager.HasActiveTools && manager.History.Count == count, "Stopping a waiting schedule does not create a run.");
    }

    private static async Task VerifyRecurrenceAsync(ToolEntry tool)
    {
        using var stop = new CancellationTokenSource();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int launches = 0;
        var schedule = CronSchedule.RunAsync(CronSchedule.Parse(tool), () => ++launches == 1 ? first.Task : second.Task, stop.Token);
        await WaitAsync(() => launches == 1);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Require(launches == 1, "Occurrences arriving during an active run are skipped.");

        first.SetResult();
        await WaitAsync(() => launches == 2);
        stop.Cancel();
        second.SetResult();
        await schedule;
        Require(launches == 2, "Canceling a schedule during a job prevents subsequent runs.");
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
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(20))
            {
                throw new TimeoutException("Timed out waiting for a scheduled cron run.");
            }

            await Task.Delay(25);
        }
    }
}
