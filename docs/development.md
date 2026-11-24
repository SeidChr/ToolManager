# Building and maintaining Tool Manager

[Back to the README](../README.md)

## Build and run

Requires Windows and the **.NET 10 SDK**. Run from PowerShell:

```powershell
.\Build.ps1 -Test -Publish
.\outputs\ToolManager\ToolManager.exe
```

| Package | Build command | Output folder | Target PC needs |
| --- | --- | --- | --- |
| Self-contained | `.\Build.ps1 -Publish` | `outputs/ToolManager` | Windows x64; .NET included |
| Framework-dependent | `.\Build.ps1 -Publish -FrameworkDependent` | `outputs/ToolManager-framework-dependent` | Windows x64 and .NET 10 Windows Desktop Runtime (x64) |

Both variants bundle the app into a single executable. The self-contained bundle is compressed. Native libraries extract to the .NET bundle cache at runtime; debug symbols are excluded and framework languages are retained.

Copy the whole portable folder, including your tools and configuration, when moving to another PC. Close a running distribution before updating it. Local builds preserve `tools.json`, its backup and user data; missing configuration is recovered from `tools.json.bak` when available.

For a consistent launch from the repository, use `Start-ToolManager.ps1`. To work on the app in isolation:

```powershell
.\Build.ps1
.\Start-ToolManager.ps1 -Development
```

Development builds, including ordinary Release builds, show **DEV** in titles, tray text, icons and scheduled task names. Source-tree executables under `src/ToolManager/bin` or `work/build` use `outputs/ToolManager-dev` for separate settings when the source project is present. Published and distributed builds use their own portable folder; published builds have no DEV markers. Each configuration root has its own instance lock.

Production settings and tools are not copied into development automatically. Both copies can run together, but their tools can still contend for ports and other resources.

The build script backs up settings, builds and tests under `work/build`, publishes to a staging directory and copies the executable into the output folder. Configuration, backups and user data are never copied from a build or deleted. No installer, service, registry changes or web server is used. Optional startup uses a local scheduled task.

The repository launcher also supports -Hidden and -Autostart.

## Process checks and their scope

Build.ps1 -Test runs actual pwsh.exe, powershell.exe, CMD and a dedicated console fixture. It checks scripts and parameters, output and exit codes, real Ctrl+C, timeout enforcement, owned children, duplicate prevention, retention, PC filtering, config backup, missing dependencies and cleanup. These are local process tests, not validation of your own tools or of a clean PC.

## Code style and editor setup

ToolManager.slnx stays at the repository root and contains the WPF application in src/ToolManager and its console test fixture in tests. Application files are grouped into Models, Execution, Interop, Persistence, Startup, UI (including Placement), and Diagnostics. Namespaces follow the project name and relative folder path, for example ToolManager.UI.Placement. Program.cs and GlobalUsings.cs remain at the application project root; embedded assets and the application manifest stay with the project. The SDK discovers C# source files recursively using its default compile items. VS Code is configured to load this solution and to avoid treating UI files or backup sources as standalone console applications. The application uses the Windows Desktop reference pack through UseWPF; do not add runtime WindowsBase.dll references manually.

StyleCop.Analyzers 1.3.180 uses its default rules. Documentation rules and the corresponding disabled-documentation notice are excluded. Analyzer warnings fail the build. Each top-level type has its own file, object initializers and control flow use readable multiline layouts, and native handle ownership remains local to ConsoleSession.

```powershell
dotnet restore ToolManager.slnx
dotnet format ToolManager.slnx --no-restore
dotnet format ToolManager.slnx --no-restore --verify-no-changes
.\Build.ps1 -Test -Publish
```

The clean-code review removed unused notification events, separated request preparation and cleanup from run monitoring, made native handle disposal idempotent, guaranteed active-run finalization even if cleanup fails, validated null configuration entries, and handled invalid helper startup arguments without a desktop crash dialog. Configuration property names and the portable settings location remain unchanged.

For this cleanup, chose focused lifecycle methods over new application layers because the identified problems were contained in ownership and error paths. This commits the UI to its existing WPF code-behind structure and keeps process behavior covered by real-process checks.

For the main-view button change, Decision4 class: Repair. Boundary map gap: no project boundary map was supplied. Chose separate fixed Start and Stop buttons over a shared toggle because a second click must not reverse the first action. Commits us to enabling Stop only for an active run or a Custom target reported as running.

## Release workflow and approval setup

After `.github/workflows/release.yml` is on the repository's default branch, open **Actions > Build and release > Run workflow**. Select the branch to build and enter a new version tag such as `v1.0.0`. The workflow runs only when manually started.

One-time setup: open **Settings > Environments**, create an environment named **release**, and enable **Required reviewers** with your GitHub account as a reviewer. Leave **Prevent self-review** off if you want to approve runs you started. Disable administrator bypass if approval must always be required. These settings are stored in GitHub, not in the workflow YAML. Without a required reviewer, the workflow refuses publication after uploading the candidate.

The **Build downloadable candidate** job installs the .NET 10 SDK and runs `Build.ps1 -Test -Publish`. After the build and checks pass, it publishes both single-file variants and uploads both Windows x64 ZIPs as one Actions artifact retained for 30 days, subject to repository retention limits. No tag or release is created by this job. Use the download link in the job summary or the run's **Artifacts** section. Extract the downloaded artifact archive, then extract `ToolManager-v1.0.0-win-x64.zip` and run `ToolManager/ToolManager.exe`. Alternatively, extract `ToolManager-v1.0.0-win-x64-framework-dependent.zip` and run `ToolManager-framework-dependent/ToolManager.exe` on a PC with the required Desktop Runtime. Each package contains the executable and empty tools and scripts folders. The README, guides and screenshots are available in the repository. The summary records the source commit and both ZIP checksums.

The **Publish approved candidate** job waits for approval of the **release** environment. Download and test the candidate, then use **Review deployments** on the workflow run to approve publication, or reject it to stop. On approval, the job downloads the exact artifact by ID, verifies both SHA256 checksums, and creates a published GitHub release with generated notes and both original ZIPs, without rebuilding. The new tag points to the exact source commit of the run, even if the branch has advanced. Existing tags are rejected both before building and immediately before publication. The executable's version is taken from the tag without the `v` prefix. If the artifact has expired or was deleted, publication fails. Re-running the build creates a new candidate that needs to be tested again.

Only the approval-gated publication job has `contents: write` on the built-in `GITHUB_TOKEN`; the build job has read-only repository access. No additional secret is required. Repository or organization policy must allow GitHub Actions and these permissions. The release job archives a staging folder containing only the executable and empty tools and scripts folders, so local settings and user files from the build output stay out of the ZIP.

Decision4 class: Repair. Boundary map gap: no project boundary map was supplied. Chose two jobs in one workflow with an environment approval gate over two manual workflows because this keeps candidate review and publication in the same run. Commits us to maintaining required reviewers on the release environment, a Windows runner, and creating a new version tag for each release. The existing build script keeps release and local checks consistent. Chose two single-file variants over AOT or framework migration because WPF requires the managed runtime. Commits us to testing both bundles and documenting the Desktop Runtime prerequisite for the framework-dependent variant.

## Implementation decisions

Decision4 class: Repair. Boundary map gap: no project boundary map was supplied. Chose WPF and a small per-run helper over WinUI 3 and a persistent service because this is a portable Windows utility. Commits us to Windows, native console/job interop, and validating shutdown with real processes. The .NET 10 Fluent theme selection API is marked experimental by WPF; the suppression is limited to that selection.

## Issue fixes and choices

Issues #1-#7 are implemented locally. Decision4 class: Repair. Boundary map gap: no project boundary map was supplied. Chose separate development identity and configuration over sharing production settings because parallel testing must not edit production configuration. Commits us to configuring development tools separately and testing both development and published builds. Chose background request preparation and process launch over moving manager state to worker threads because WPF and lifecycle state keep their existing single-thread ownership. Commits us to preserving that ownership for later execution changes. Chose Windows temporary run directories over synchronized portable data because scripts and requests are short-lived execution inputs. Commits us to best-effort cleanup; interrupted runs can leave temporary files. Existing portable data is left untouched. Chose primary taskbar placement for initial opening and tray-click placement thereafter over cursor-only startup because launch cursor position does not identify the notification area.

## Startup and console design choices

Decision4 class: Repair. Map gap: no boundary map was supplied for startup integration. Chose an interactive per-user logon task over a boot task because the tray icon needs the user's desktop. Commits us to a local path-bound task that must be removed and recreated when the executable moves.

For console startup visibility, Decision4 class: Repair. Boundary map gap: no project boundary map was supplied. Chose the runner's hidden startup setting over hiding the console after allocation because hiding afterward permits a visible flash. Commits us to retaining a real console for Ctrl+C and checking hidden console visibility with the real-process fixture.

## Script-supplied status

Issue #20 adds per-tool Custom Status (None, Last output line, Explicit status line). Explicit lines use the fixed prefix ##TM[STATUS]: on stdout. A bounded streaming parser follows PlainOutput filtering and stores sanitized text separately from retained output. Custom text replaces Running only during an active execution; lifecycle states and Custom Check exit codes retain authority over both display and controls.

RunRecord and RunManager expose change events. The main window observes manager changes, coalesces refreshes on the WPF dispatcher, and updates existing status labels without rebuilding rows for text changes. A timer runs only while a stopping countdown is visible. Output windows retain their existing output refresh mechanism. Manager and run mutation still belong to the existing caller/dispatcher context.

Decision4 class: Repair. Boundary map gap: no applicable boundary map was supplied. Chose the existing stdout channel and observer events over variable polling or another IPC channel because every supported shell already sends stdout through the runner pipe. Commits us to maintaining the exact prefix contract, bounded line parsing, observer lifetime cleanup and authoritative lifecycle precedence. Existing configuration defaults to None without a version migration. CustomStatusSource is an enum throughout configuration, parsing and editor selection; JSON enum member names preserve the existing string values and the editor retains its readable labels. Chose a typed enum over string comparisons because the supported sources are a closed set. Commits us to preserving serialized member names when renaming enum members.
