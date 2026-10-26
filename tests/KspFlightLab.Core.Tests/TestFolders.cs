// KSP Flight Lab · tests/KspFlightLab.Core.Tests/TestFolders.cs
// [Test] Throwaway folders for tests (TEMP/FlightLabTests, cleaned up: Mission Control's once filled a C: drive), and
//        a fake KSP.
// Used by: both test projects (the UI tests compile this file too)
// Guide:   Meat and Potatoes/01-Architecture.md

using System.Runtime.CompilerServices;

namespace KspFlightLab.Core.Tests;

internal static class TestFolders
{
    /// <summary>Leftovers older than this go when a test project starts (not newer: the other project may be running).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    public static string Root => Path.Combine(Path.GetTempPath(), "FlightLabTests");

    /// <summary>A new empty folder.</summary>
    public static string Temp(string name) =>
        Directory.CreateDirectory(Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A folder that passes for a KSP: its program and an empty GameData (never the real one).</summary>
    public static string FakeKsp()
    {
        var ksp = Temp("ksp");
        File.WriteAllText(Path.Combine(ksp, OperatingSystem.IsWindows() ? "KSP_x64.exe" : "KSP.x86_64"), "");
        File.WriteAllText(Path.Combine(ksp, "readme.txt"), "Version 1.12.5");
        Directory.CreateDirectory(Path.Combine(ksp, "GameData"));
        return ksp;
    }

    [ModuleInitializer]
    internal static void AtStart()
    {
        try
        {
            if (Directory.Exists(Root))
                foreach (var entry in Directory.GetFileSystemEntries(Root))
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(entry) > MaxAge)
                        Delete(entry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Cleaning up is never worth a failed run.
        }
    }

    /// <summary>Deletes a test's own folder (or file); never throws.</summary>
    public static void Delete(string? path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file still open (a log): left for the next run's cleanup.
        }
    }
}
