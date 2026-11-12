namespace ToolManager;

using System.Text.Json.Serialization;

public static class Store
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Root { get; set; } = ApplicationRoot(AppContext.BaseDirectory);

    public static string ConfigPath => Path.Combine(Root, "tools.json");

    public static string ApplicationRoot(string directory)
    {
        var path = Path.GetFullPath(directory);
        for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
        {
            if (!File.Exists(Path.Combine(parent.FullName, "ToolManager.csproj")))
            {
                continue;
            }

            var relative = Path.GetRelativePath(parent.FullName, path).TrimEnd(Path.DirectorySeparatorChar);
            if (relative.Equals("bin", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin\\", StringComparison.OrdinalIgnoreCase) ||
               relative.Equals("work\\build", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(parent.FullName, "outputs", "ToolManager");
            }

            break;
        }

        return path;
    }

    public static string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    public static Configuration Load()
    {
        if (!File.Exists(ConfigPath))
        {
            if (File.Exists(ConfigPath + ".bak"))
            {
                var backup = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(ConfigPath + ".bak"), Json) ?? throw new InvalidDataException("Configuration backup is empty.");
                Validate(backup);
                File.Copy(ConfigPath + ".bak", ConfigPath, false);
                return backup;
            }

            Directory.CreateDirectory(Root);
            var c = new Configuration();
            Save(c);
            return c;
        }

        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(ConfigPath), Json) ?? throw new InvalidDataException("Configuration is empty.");
        Validate(config);
        return config;
    }

    public static void Validate(Configuration c)
    {
        if (c.Version != 1)
        {
            throw new InvalidDataException("Unsupported configuration version.");
        }

        if (c.StopTimeoutSeconds is < 1 or > 3600 || c.HistoryCount is < 1 or > 100 || c.OutputLimitKiB is < 16 or > 16384 || c.GlobalOutputLimitMiB is < 4 or > 512)
        {
            throw new InvalidDataException("Timeout must be 1-3600s, history 1-100, output 16-16384 KiB, and total output 4-512 MiB.");
        }

        if (c.Shells is null || c.Tools is null || c.Shells.Count == 0)
        {
            throw new InvalidDataException("At least one shell is required.");
        }

        if (c.Shells.Any(shell => shell is null) || c.Tools.Any(tool => tool is null))
        {
            throw new InvalidDataException("Shell and tool lists must not contain null entries.");
        }

        if (c.Shells.Select(s => s.Id).Distinct().Count() != c.Shells.Count || c.Tools.Select(t => t.Id).Distinct().Count() != c.Tools.Count)
        {
            throw new InvalidDataException("Entry IDs must be unique.");
        }

        foreach (var s in c.Shells)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Executable) || s.Kind is not ("PowerShell" or "CMD"))
            {
                throw new InvalidDataException("Each shell needs an ID, name, executable, and supported kind.");
            }
        }

        if (!c.Shells.Any(s => s.Id == c.DefaultShellId))
        {
            throw new InvalidDataException("Choose an existing default shell.");
        }

        foreach (var t in c.Tools)
        {
            if (string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Command) || string.IsNullOrWhiteSpace(t.WorkingDirectory))
            {
                throw new InvalidDataException("Each tool needs an ID, name, command, and working directory.");
            }

            if (!c.Shells.Any(s => s.Id == t.ShellId))
            {
                throw new InvalidDataException($"Select a valid shell for {t.Name}.");
            }

            if (t.Mode is not ("Installed" or "Bundled") || t.Behavior is not ("Managed" or "Custom"))
            {
                throw new InvalidDataException("Unsupported tool mode or behavior.");
            }

            if (t.Mode == "Bundled" && Path.IsPathRooted(t.WorkingDirectory))
            {
                throw new InvalidDataException("Bundled working directories must be relative.");
            }

            if (t.StopTimeoutSeconds is < 1 or > 3600)
            {
                throw new InvalidDataException("Stop timeout must be 1-3600 seconds.");
            }

            if (t.Interactive && t.Behavior != "Managed")
            {
                throw new InvalidDataException("Keep-open shells require Managed behavior.");
            }

            if (t.Behavior == "Custom" && string.IsNullOrWhiteSpace(t.StopCommand))
            {
                throw new InvalidDataException("Custom tools need a stop command.");
            }

            t.PcNames = (t.PcNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static void Save(Configuration c)
    {
        Validate(c);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(c, Json), new UTF8Encoding(false));
        if (File.Exists(ConfigPath))
        {
            File.Replace(temp, ConfigPath, ConfigPath + ".bak");
        }
        else
        {
            File.Move(temp, ConfigPath);
        }
    }
}
