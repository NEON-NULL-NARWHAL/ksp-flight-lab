// Flight Lab · src/LunaEvo.FlightLab.Core/Install/KspFolder.cs
// [Core] A KSP install: whether a folder is one (Windows or Linux), and whether that KSP is running (mods are only
//        installed with it closed).
// Used by: WingmanTools, SetupPage
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Diagnostics;

namespace LunaEvo.FlightLab.Core.Install;

public static class KspFolder
{
    /// <summary>KSP's program in its folder, per system: KSP_x64.exe on Windows, KSP.x86_64 on Linux.</summary>
    public static readonly string[] Programs = ["KSP_x64.exe", "KSP.x86_64"];

    public static bool IsKspFolder(string? folder) =>
        folder != null && Directory.Exists(folder) && Directory.Exists(Path.Combine(folder, "GameData")) &&
        Programs.Any(p => File.Exists(Path.Combine(folder, p)));

    /// <summary>Whether a KSP from that folder is running (its program's path is in that folder).</summary>
    public static bool IsRunning(string folder)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var name in new[] { "KSP_x64", "KSP.x86_64" })
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Another user's process, or it just exited: can't tell, so it isn't ours.
                }
                finally
                {
                    process.Dispose();
                }
            }
        return false;
    }
}
