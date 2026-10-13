using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CodexTray;

internal static class StartupManager
{
    private const string ValueName = "CodexTray";
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int FileNotFound = unchecked((int)0x80070002);

    public static bool IsEnabled
    {
        get
        {
            try
            {
                return WithTaskFolder(folder =>
                {
                    object? task = FindTask(folder);
                    if (task is null)
                    {
                        return File.Exists(ShortcutPath);
                    }

                    try
                    {
                        return (bool)((dynamic)task).Enabled;
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(task);
                    }
                });
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                // An unavailable scheduler must not prevent an interactive launch.
                AppLog.Write($"Could not read startup task: {exception.Message}");
                return File.Exists(ShortcutPath);
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        WithTaskFolder(folder =>
        {
            if (enabled)
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("The application path is unavailable.");
                object task = folder.RegisterTask(
                    TaskName, CreateTaskXml(executable, UserSid), TaskCreateOrUpdate,
                    UserSid, null, TaskLogonInteractiveToken, null);
                Marshal.FinalReleaseComObject(task);
                AppLog.Write($"Startup task registered: {TaskName}, executable={executable}.");
            }
            else
            {
                object? task = FindTask(folder);
                if (task is not null)
                {
                    Marshal.FinalReleaseComObject(task);
                    // Deleting the registration leaves the current widget running.
                    folder.DeleteTask(TaskName, 0);
                }

                AppLog.Write("Automatic startup disabled.");
            }

            return true;
        });

        // Register the replacement successfully before removing the old launcher.
        File.Delete(ShortcutPath);
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "CodexTray.lnk");

    private static string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value
                ?? throw new InvalidOperationException("The current Windows user is unavailable.");
        }
    }

    private static string TaskName => $"CodexTray-{UserSid}";

    internal static string CreateTaskXml(string executable, string userSid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name, object value) => new(ns + name, value);

        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Description", "Codex usage taskbar widget")),
            Element("Triggers", Element("LogonTrigger", new object[]
            {
                Element("Enabled", true), Element("UserId", userSid), Element("Delay", "PT10S")
            })),
            Element("Principals", new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"),
                Element("RunLevel", "LeastPrivilege"))),
            Element("Settings", new object[]
            {
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", false), Element("StopIfGoingOnBatteries", false),
                Element("AllowStartOnDemand", true), Element("StartWhenAvailable", true),
                Element("Enabled", true), Element("ExecutionTimeLimit", "PT0S"),
                Element("RestartOnFailure", new object[] { Element("Interval", "PT1M"), Element("Count", 3) })
            }),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"),
                Element("Exec", new object[]
                {
                    Element("Command", executable), Element("Arguments", "--startup-supervisor"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable)!)
                }))).ToString();
    }

    internal static object? FindTask(dynamic folder)
    {
        try
        {
            return folder.GetTask(TaskName);
        }
        catch (Exception exception) when (
            exception is COMException or FileNotFoundException && exception.HResult == FileNotFound)
        {
            // The dynamic COM binder can translate a missing task into FileNotFoundException.
            return null;
        }
    }

    private static T WithTaskFolder<T>(Func<dynamic, T> action)
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        object service = Activator.CreateInstance(schedulerType)!;
        object? folder = null;
        try
        {
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            if (folder is not null)
            {
                Marshal.FinalReleaseComObject(folder);
            }

            Marshal.FinalReleaseComObject(service);
        }
    }
}
