using System;
using System.IO;

namespace Swag.Avalonia.Services;

public static class AppPaths
{
    /// <summary>Where settings and the like are kept, which is created when it is asked for.</summary>
    public static string DataDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Swag");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string RecentLevelsFile => Path.Combine(DataDirectory, "recent.json");
}
