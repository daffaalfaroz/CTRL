using System.Text.Json;

namespace CTRL.Desktop.Settings;

public static class DesktopSettingsStore
{
    private const string FolderName = "CTRL";
    private const string FileName = "DesktopSettings.json";

    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        FolderName, FileName);

    public static DesktopSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath))
                return new DesktopSettings();

            var json = System.IO.File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new DesktopSettings();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<DesktopSettings>(json, options) ?? new DesktopSettings();
        }
        catch
        {
            return new DesktopSettings();
        }
    }

    public static void Save(DesktopSettings settings)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = FilePath + ".tmp";
            System.IO.File.WriteAllText(tmpPath, json);
            System.IO.File.Copy(tmpPath, FilePath, true);
            System.IO.File.Delete(tmpPath);
        }
        catch
        {
            // Silent fallback — do not crash the application.
        }
    }
}

public sealed class DesktopSettings
{
    public int ListenPort { get; init; } = 0;
    public string ListenAddress { get; init; } = "127.0.0.1";
    public bool StartMinimized { get; init; } = false;
    public bool AutoStartServer { get; init; } = true;
}