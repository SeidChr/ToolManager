# Cron tasks

Choose **Cron** under **Behavior** in the tool editor. Enter a cron expression and a command that exits when its work is complete. The expression uses this PC's local time and has five fields in this order: minute, hour, day of month, month, day of week. For second-level scheduling, add a leading seconds field.

| Expression | Schedule |
| --- | --- |
| `*/5 * * * *` | Every five minutes |
| `0 9 * * MON-FRI` | 09:00 on weekdays |
| `*/30 * * * * *` | Every 30 seconds |

When you constrain both day of month and day of week, both must match. Tool Manager uses Cronos (MIT) to interpret expressions. See [Cronos expression syntax](https://github.com/HangfireIO/Cronos#cron-format) for supported ranges, steps, and daylight-saving behavior.

**Start** arms the schedule. The row shows **Scheduled** between runs. Each occurrence starts a fresh session. **Output** shows one live log with every retained occurrence, oldest first. Each block has a start timestamp and shows its result when the run ends. **Copy** copies the combined log. The regular history and output limits still apply. A failed command leaves the schedule armed. Runs from the same entry never overlap. An occurrence that falls during a run is skipped. After a PC sleep or scheduling delay, at most one pending occurrence runs, and older occurrences are skipped.

**Stop** disarms the schedule and, if a run is active, sends Ctrl+C and applies its configured grace period. Opening Settings or exiting the manager also stops active schedules after confirmation. Hidden tools cannot be autostarted on this PC. When the manager starts an opted-in cron tool through autostart, it arms its schedule. The schedule is active only while Tool Manager is running.
