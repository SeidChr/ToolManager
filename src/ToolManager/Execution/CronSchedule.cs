namespace ToolManager.Execution;

using Cronos;
using ToolManager.Models;

internal static class CronSchedule
{
    public static CronExpression Parse(ToolEntry tool)
    {
        var text = tool.CronExpression?.Trim() ?? string.Empty;
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var message = $"Enter a valid cron expression for {tool.Name} (five fields, or six with leading seconds).";
        if (fields.Length is not (5 or 6))
        {
            throw new InvalidDataException(message);
        }

        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        if (!CronExpression.TryParse(text, format, out var expression))
        {
            throw new InvalidDataException(message);
        }

        if (expression.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local) is null)
        {
            throw new InvalidDataException($"The cron expression for {tool.Name} has no future occurrence.");
        }

        return expression;
    }

    public static async Task RunAsync(CronExpression expression, Func<Task> run, CancellationToken token)
    {
        var lastOccurrence = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var after = now > lastOccurrence ? now : lastOccurrence;
            if (expression.GetNextOccurrence(after, TimeZoneInfo.Local) is not DateTime next)
            {
                return;
            }

            while ((now = DateTime.UtcNow) < next)
            {
                var wait = next - now;
                await Task.Delay(wait < TimeSpan.FromMinutes(1) ? wait : TimeSpan.FromMinutes(1), token);
            }

            token.ThrowIfCancellationRequested();
            lastOccurrence = next;
            if (expression.GetNextOccurrence(next, TimeZoneInfo.Local) <= now)
            {
                continue;
            }

            await run();
        }
    }
}
