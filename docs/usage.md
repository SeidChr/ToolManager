# Using Tool Manager

[Back to the README](../README.md)

## Configure a tool

Settings includes Show configuration in File Explorer to select tools.json for copying or manual edits. Save in Settings can overwrite manual edits made while the dialog is open. Settings supports adding, editing, deleting and ordering tools, as well as adding/editing shell profiles and choosing defaults. If runs are active, Settings offers Stop and continue, waits for all managed sessions to finish, then opens configuration. Cancel leaves the runs active. External targets controlled by Custom scripts are not stopped automatically.

- Managed: use for both one-shot commands and long-running tools. Start creates a run; Stop sends Ctrl+C and waits for the configured timeout, then forcibly terminates remaining processes owned by that run. Only one active run per entry is allowed.
- Custom: start and stop scripts control a target outside the manager's process session, such as a service. Optional Check script uses exit code 0 for running, 1 for stopped, and other values for unknown. No checks run automatically. Canceling an active script stops that script's session, not its external target. External targets are not automatically stopped on manager exit.
- Cron: run a command on a schedule while the tool is started. See [Cron tasks](cron.md).
- Installed: commands can use PATH names, absolute paths or relative paths.
- Bundled: use relative tool paths. The working-directory setting must be relative. Arbitrary script contents are executed as entered, not parsed or rewritten to enforce portability.

### Shells and scripts

Shell profiles accept an executable name found in PATH, an absolute path, or a path relative to the manager folder. Default is pwsh.exe. Windows PowerShell and CMD profiles are supplied. There is no silent shell fallback. The portable manager includes .NET, not PowerShell 7 or your tools' other dependencies. A bundled shell can be selected by a relative executable path.

PowerShell profiles are loaded by default, including for existing shell configurations. Disable Load PowerShell profiles in Settings > shell profiles > Edit shell when you want an isolated environment. Script execution policy is respected. Scripts run with your user privileges. Enter commands, cmdlets, .ps1 calls with parameters, or full multiline scripts. CMD profiles interpret CMD syntax.

```powershell
& '.\tools\My Tool\tool.exe' --port 8080
& '.\scripts\Export.ps1' -Destination '.\exports'
Get-ChildItem '.\input' | ForEach-Object { Write-Output $_.Name }
& "$env:TOOLMANAGER_ROOT\tools\tool.exe"
```

### Paths and working directories

All relative working-directory settings resolve from the manager folder, not the folder used to launch it. Relative paths inside a script resolve from that run's working directory. TOOLMANAGER_ROOT always contains the manager folder. PowerShell does not automatically execute files from the working directory: use .\tool.exe or the call operator for quoted paths. Scripts should use explicit exit codes where appropriate; the manager reports the shell's exit code.

### Start and Stop states

The main view always shows separate Start and Stop buttons. Start is enabled while idle; Stop is enabled while running, including an external Custom target reported as running by Check. Both are disabled while stopping. An unknown Custom status leaves Start enabled and Stop disabled.

## Start with Windows

Settings > Startup provides Add startup task, Remove startup task and Reset. The task passes --autostart --hidden: opted-in tools start while the manager stays in the tray. Use Reset to update an existing task. Running ToolManager.exe --hidden also starts without showing a window, independently of --autostart. Add is enabled only when this copy's task is absent; Remove is enabled only when it exists. Status refreshes when Settings opens or regains focus. If Task Scheduler cannot be read, both buttons remain disabled and an error is shown.

The task starts the currently running executable when the current user signs in, including after Windows startup. A tray application requires an interactive desktop, so it uses a user-specific logon trigger rather than a pre-login boot trigger. No password is stored, the manager runs with normal user privileges, battery power does not prevent startup, and no execution time limit is imposed. Windows requests administrator permission only when adding or removing fails with access denied. Canceling that prompt leaves the task unchanged. Changes apply immediately, independently of Save or Cancel.

Tasks are local to the PC and identified by user SID and executable path. Separate portable copies have separate tasks. Remove the task before moving or deleting its executable, then add it again from the new location.

## Output and lifecycle

Output opens a separate window with the current and retained previous runs, stdout, marked stderr, duration, exit code, Copy, and Follow output. The output and input panes have a fixed black background, including while hovered or focused. PowerShell color formatting is disabled where supported, and ANSI/OSC terminal escapes are filtered incrementally from both output streams, including sequences split across pipe reads. Terminal cursor controls and interactive TUI applications are not emulated. Separate stdout/stderr streams cannot guarantee exact combined ordering. UTF-8 is configured for supplied shells; legacy executables with different encodings may display incorrectly.

### Run history

History is in memory only, cleared on exit. Defaults: last 10 completed runs per entry, 2 MiB of UTF-16 text per run, and 64 MiB total retained text. Limits bound stored text, not total application memory or temporary UI copies. Old output is discarded with a visible marker. Closing an output window does not stop its run.

### Send input to a running shell

Enable Keep shell open after the start command in a Managed tool to send line input from its Output window. PowerShell uses -NoExit and CMD uses /k; Send or Enter sends a single UTF-8 line (up to 4096 characters) to the same live session. Variables persist across commands and Read-Host accepts text responses. Select the active run, not a previous run. Use exit to finish or Stop to end the session with Ctrl+C and the configured timeout. Input is not evaluated by the manager: it goes to whichever program currently reads the session input. No terminal emulation, tab completion or secure-input UI is provided. Keep-open mode is off for existing tools.

### Shutdown and process ownership

Each run has an isolated hidden Windows console and a Windows Job Object. Ctrl+C is a real console event, not an input character. A cooperating program can perform cleanup before exiting. An ignoring program and its ordinary child processes are terminated after the grace period. Running means processes exist, not that the tool is ready. Stop during startup is queued. Exit manager stops all active sessions using the same sequence.

The runner starts with a hidden window setting so its console is hidden when allocated, avoiding the brief popup caused by creating a visible console and hiding it afterward. This applies to the manager's console; tools that explicitly create their own windows control those windows themselves.

Detached consoles, services, elevated processes and other processes created outside the job may require Custom commands. Processes ignoring Ctrl+C are subject to forced termination. A manager crash closes its runner connection; the runner attempts graceful shutdown. If a runner crashes, the job closes and its processes are terminated immediately.

## PC visibility and storage

Enable Only show on these PCs to autofill the current name. Add this PC appends it distinctly; manual names use one line each. Matching ignores case and surrounding whitespace. An empty restricted list hides the entry everywhere. Settings still shows hidden entries on every PC. This is filtering, not access control.

- tools.json: shared configuration next to the executable.
- tools.json.bak: previous configuration after an atomic replacement.
- Windows temporary folder, ToolManager/runs/RUN-ID/: temporary scripts and execution requests, removed after completion where possible. Interrupted runs may leave files here.

Configuration and scripts can contain sensitive values. Configuration stays in the portable root. Per-run scripts and requests use the current user's Windows temporary folder so sync clients do not upload run traffic. Use a writable folder. Sharing/synchronizing the same configuration is supported for reading on multiple PCs; simultaneous editing is not coordinated. Keep separate copies or avoid simultaneous saves.

## Tray placement

The main window fits the tools shown on this PC. It widens for long names and scrolls vertically when the list is taller
than the monitor allows.

The popup uses the tray click location to select its monitor and reads actual primary/secondary taskbar bounds, including moved taskbars. It opens beside the corresponding edge within the monitor work area. Per-monitor DPI scaling and negative coordinates are supported. If a custom shell provides no usable taskbar window, placement falls back to the clicked edge and reported work area.

Left-click the notification-area icon to open the window or hide it when it is already visible. The manager and its
tools keep running while the window is hidden.
