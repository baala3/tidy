using System.Security;
using Microsoft.Win32;
using Tidy.Core.Platform;
using Tidy.Core.Scheduling;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Tidy.App.Services;

/// <summary>
/// Tidy's Windows notifications. Tidy is a plain .exe with no package identity, so it tells
/// Windows its name, icon and click handler in the registry, the way unpackaged apps do. Each
/// notification carries the page to open; a click reaches <see cref="ToastActivator"/>.
/// </summary>
public static class Notifications
{
    /// <summary>Tidy's identity in Windows' notification settings.</summary>
    public const string AppId = "baala3.Tidy";

    /// <summary>The COM class Windows starts or calls when a notification is clicked.</summary>
    public const string ActivatorClsid = "{E8802D55-FAFB-4D7C-96AF-16F11F38046A}";

    /// <summary>What Windows passes to Tidy.exe when it starts it to deliver a click.</summary>
    public const string ActivatedArgument = "-ToastActivated";

    private const string PagePrefix = "page=";
    private static readonly string AppIdKey = $@"Software\Classes\AppUserModelId\{AppId}";
    private static readonly string ActivatorKey = $@"Software\Classes\CLSID\{ActivatorClsid}";

    /// <summary>Points Windows at this copy of Tidy. Run again on each start, in case the app moved.</summary>
    public static void Register(string exePath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(AppIdKey))
        {
            key.SetValue("DisplayName", "Tidy");
            key.SetValue("IconUri", Path.Combine(Path.GetDirectoryName(exePath)!, "Assets", "Tidy-64.png"));
            key.SetValue("CustomActivator", ActivatorClsid);
        }
        using var server = Registry.CurrentUser.CreateSubKey($@"{ActivatorKey}\LocalServer32");
        server.SetValue("", $"\"{exePath}\" {ActivatedArgument}");
    }

    /// <summary>For the uninstaller: forgets everything <see cref="Register"/> told Windows.</summary>
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(AppIdKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ActivatorKey, throwOnMissingSubKey: false);
    }

    /// <summary>The page a click asked for, or null.</summary>
    public static string? PageOf(string arguments) =>
        arguments.StartsWith(PagePrefix, StringComparison.Ordinal) ? arguments[PagePrefix.Length..] : null;

    public static void ShowCleaned(ScheduledCleanResult cleaned)
    {
        var names = cleaned.Cleaned;
        var what = names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]}, {names[1]} and {Format.Count(names.Count - 2, "more", "more")}",
        };
        Show("history", $"Tidy cleared {Format.Bytes(cleaned.Result.FreedBytes)}", $"{what} went to the Recycle Bin, if you need anything back.");
    }

    public static void ShowLowSpace(DiskUsage disk) =>
        Show("overview", $"Drive {disk.Drive} is almost full", $"{Format.Bytes(disk.FreeBytes)} free of {Format.Bytes(disk.TotalBytes)}. Open Tidy to see what you can clear.");

    private static void Show(string page, string title, string body)
    {
        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast launch="{SecurityElement.Escape(PagePrefix + page)}" activationType="foreground">
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(title)}</text>
                  <text>{SecurityElement.Escape(body)}</text>
                </binding>
              </visual>
            </toast>
            """);
        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
    }
}
