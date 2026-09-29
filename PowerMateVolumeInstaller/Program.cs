using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace PowerMateVolumeInstaller;

internal static class Program
{
    private const string AppName = "PowerMateVolume";
    private const string DisplayName = "PowerMate Volume";
    private const string Version = "1.0.0";
    private const string Publisher = "Local";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerMateVolume";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "PowerMateVolume");

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var uninstall = args.Any(arg => arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        var quiet = args.Any(arg => arg.Equals("/quiet", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (uninstall)
            {
                Uninstall();
                WriteLine("PowerMate Volume 已卸载。", quiet);
            }
            else
            {
                Install();
                WriteLine("PowerMate Volume 已安装并启动。", quiet);
            }

            if (!quiet)
            {
                Console.WriteLine();
                Console.WriteLine("按 Enter 关闭窗口。");
                Console.ReadLine();
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("安装失败：");
            Console.Error.WriteLine(exception.Message);
            if (!quiet)
            {
                Console.WriteLine();
                Console.WriteLine("按 Enter 关闭窗口。");
                Console.ReadLine();
            }

            return 1;
        }
    }

    private static void Install()
    {
        Console.WriteLine("正在安装 PowerMate Volume...");

        StopProcess(AppName);
        StopProcess("PowerMate");
        RemoveRunValue("PowerMate");

        Directory.CreateDirectory(InstallDir);
        ExtractResource("PowerMateVolume.exe", Path.Combine(InstallDir, "PowerMateVolume.exe"));
        WriteDefaultConfig();
        WriteStartupScript();
        CopyInstallerForUninstall();
        RegisterStartup();
        RegisterUninstall();
        StartInstalledApp();

        Console.WriteLine($"安装目录：{InstallDir}");
        Console.WriteLine(@"日志目录：%LOCALAPPDATA%\PowerMateVolume");
    }

    private static void Uninstall()
    {
        Console.WriteLine("正在卸载 PowerMate Volume...");

        StopProcess(AppName);
        RemoveRunValue(AppName);

        using (var uninstallKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
        {
            uninstallKey?.DeleteSubKeyTree("PowerMateVolume", throwOnMissingSubKey: false);
        }

        if (Directory.Exists(InstallDir))
        {
            foreach (var file in Directory.EnumerateFiles(InstallDir))
            {
                try
                {
                    if (!Path.GetFileName(file).Equals("PowerMateVolumeSetup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Leave locked files behind; a later uninstall run can remove them.
                }
            }
        }
    }

    private static void ExtractResource(string resourceName, string destination)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"安装包缺少资源：{resourceName}");
        using var output = File.Create(destination);
        resource.CopyTo(output);
    }

    private static void WriteDefaultConfig()
    {
        var configPath = Path.Combine(InstallDir, "PowerMateVolume.json");
        if (File.Exists(configPath))
        {
            return;
        }

        File.WriteAllText(configPath,
            "{\r\n" +
            "  \"volumeStep\": 0.01,\r\n" +
            "  \"startMutedToggleOnPress\": true,\r\n" +
            "  \"backend\": \"auto\",\r\n" +
            "  \"logLevel\": \"Info\"\r\n" +
            "}\r\n");
    }

    private static void WriteStartupScript()
    {
        var scriptPath = Path.Combine(InstallDir, "start-powermate-volume.ps1");
        File.WriteAllText(scriptPath,
            "$ErrorActionPreference = 'Stop'\r\n" +
            "$app = Join-Path $PSScriptRoot 'PowerMateVolume.exe'\r\n" +
            "if (Test-Path -LiteralPath $app) {\r\n" +
            "    Start-Process -FilePath $app -WindowStyle Hidden -WorkingDirectory $PSScriptRoot\r\n" +
            "}\r\n");
    }

    private static void CopyInstallerForUninstall()
    {
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return;
        }

        var destination = Path.Combine(InstallDir, "PowerMateVolumeSetup.exe");
        if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void RegisterStartup()
    {
        var scriptPath = Path.Combine(InstallDir, "start-powermate-volume.ps1");
        var command = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        runKey.SetValue(AppName, command, RegistryValueKind.String);
    }

    private static void RegisterUninstall()
    {
        using var uninstallKey = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        uninstallKey.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
        uninstallKey.SetValue("DisplayVersion", Version, RegistryValueKind.String);
        uninstallKey.SetValue("Publisher", Publisher, RegistryValueKind.String);
        uninstallKey.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
        uninstallKey.SetValue("DisplayIcon", Path.Combine(InstallDir, "PowerMateVolume.exe"), RegistryValueKind.String);
        uninstallKey.SetValue("UninstallString", $"\"{Path.Combine(InstallDir, "PowerMateVolumeSetup.exe")}\" /uninstall", RegistryValueKind.String);
        uninstallKey.SetValue("QuietUninstallString", $"\"{Path.Combine(InstallDir, "PowerMateVolumeSetup.exe")}\" /uninstall /quiet", RegistryValueKind.String);
        uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void StartInstalledApp()
    {
        var scriptPath = Path.Combine(InstallDir, "start-powermate-volume.ps1");
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = true
        });
    }

    private static void StopProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // The next launch usually succeeds even if an elevated process refuses to stop.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void RemoveRunValue(string valueName)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static void WriteLine(string message, bool quiet)
    {
        if (!quiet)
        {
            Console.WriteLine(message);
        }
    }
}
