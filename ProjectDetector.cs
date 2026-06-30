namespace YmmSubtitleSync;

// JetCutPlugin (https://github.com/Rindai0123-Artifact/ymm4-jetcut-plugin) を参考に実装
static class ProjectDetector
{
    static object? _cachedMainModel;
    static Type?   _cachedMainModelType;

    public static string? GetCurrentProjectPath()
    {
        var path = GetPropertyValue<string>("ProjectFilePath");
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

        path = GetProjectPathFromTitle();
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

        return FindLatestYmmpFile();
    }

    static T? GetPropertyValue<T>(string name) where T : class
    {
        var raw = GetPropertyValueRaw(name);
        if (raw is T t) return t;
        try
        {
            var v = raw?.GetType().GetProperty("Value");
            return v?.GetValue(raw) as T;
        }
        catch { return default; }
    }

    static object? GetPropertyValueRaw(string name)
    {
        try
        {
            var (model, modelType) = GetMainModel();
            if (model == null || modelType == null) return null;
            var prop = modelType.GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(model);
        }
        catch { return null; }
    }

    static (object? Model, Type? ModelType) GetMainModel()
    {
        if (_cachedMainModel != null && _cachedMainModelType != null)
            return (_cachedMainModel, _cachedMainModelType);
        try
        {
            var appType = Type.GetType("System.Windows.Application, PresentationFramework");
            var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (app == null) return (null, null);

            var mw = appType!.GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Instance)?.GetValue(app);
            if (mw == null) return (null, null);

            var dc = mw.GetType().GetProperty("DataContext", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mw);
            if (dc == null) return (null, null);

            _cachedMainModel = dc;
            _cachedMainModelType = dc.GetType();
            return (_cachedMainModel, _cachedMainModelType);
        }
        catch { return (null, null); }
    }

    static string? GetProjectPathFromTitle()
    {
        try
        {
            var appType = Type.GetType("System.Windows.Application, PresentationFramework");
            var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var mw  = appType?.GetProperty("MainWindow")?.GetValue(app);
            var title = mw?.GetType().GetProperty("Title")?.GetValue(mw) as string;
            if (string.IsNullOrEmpty(title)) return null;

            var dash = title.LastIndexOf(" - ");
            if (dash <= 0) return null;
            var name = title[..dash].Trim();

            if (name.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase) && File.Exists(name))
                return name;

            var dir = Path.Combine(GetYmm4Dir() ?? "", "user", "project");
            if (!Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir, "*.ymmp", SearchOption.AllDirectories))
                if (Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return f;
        }
        catch { }
        return null;
    }

    static string? FindLatestYmmpFile()
    {
        try
        {
            var dir = Path.Combine(GetYmm4Dir() ?? "", "user", "project");
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.ymmp", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    public static bool TryReloadProject(string ymmpPath)
    {
        try
        {
            var (model, modelType) = GetMainModel();
            if (model == null || modelType == null) return false;

            // Look for any ICommand property that accepts a file path
            string[] commandNames = ["OpenProjectCommand", "LoadProjectCommand", "OpenCommand", "OpenFileCommand", "ReloadCommand"];
            foreach (var name in commandNames)
            {
                var prop = modelType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) continue;
                var cmd = prop.GetValue(model);
                if (cmd == null) continue;
                var cmdType = cmd.GetType();
                var canExec = cmdType.GetMethod("CanExecute");
                var exec    = cmdType.GetMethod("Execute");
                if (exec == null) continue;

                object? arg = name == "ReloadCommand" ? null : (object?)ymmpPath;
                bool canRun = canExec == null || (bool)(canExec.Invoke(cmd, [arg]) ?? false);
                if (canRun) { exec.Invoke(cmd, [arg]); return true; }
            }

            // Fallback: try method directly
            string[] methodNames = ["OpenProject", "LoadProject", "OpenFile"];
            foreach (var name in methodNames)
            {
                var m = modelType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) continue;
                var parms = m.GetParameters();
                if (parms.Length == 0) { m.Invoke(model, []); return true; }
                if (parms.Length == 1 && parms[0].ParameterType == typeof(string)) { m.Invoke(model, [ymmpPath]); return true; }
            }
        }
        catch { }
        return false;
    }

    static string? GetYmm4Dir()
    {
        try { return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName); }
        catch { return null; }
    }
}
