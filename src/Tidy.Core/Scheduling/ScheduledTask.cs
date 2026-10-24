using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Tidy.Core.Scheduling;

/// <summary>
/// Tidy's one scheduled task: <c>Tidy.exe --background</c> once an hour, as the signed-in
/// user, without administrator rights and only while they are signed in. It is an ordinary
/// Task Scheduler entry, so the user can see it there, and Tidy removes it when both
/// automatic cleaning and low space alerts are off.
/// </summary>
public static class ScheduledTask
{
    public const string Argument = "--background";

    /// <summary>Tasks live in Task Scheduler's shared root folder, so each user gets their own.</summary>
    public static string Name => $"Tidy - {Environment.UserName}";

    /// <summary>Creates the task, or points the existing one at <paramref name="exePath"/>. Returns why it failed, or null.</summary>
    public static string? Register(string exePath)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return "Windows didn't say who is signed in.";
        var file = Path.Combine(Path.GetTempPath(), $"tidy-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks reads task XML only as UTF-16; Encoding.Unicode writes the byte order mark it wants.
            File.WriteAllText(file, Xml(exePath, sid, DateTime.Today), Encoding.Unicode);
            return Run("/Create", "/TN", Name, "/XML", file, "/F");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Removes the task if it exists. Returns why it couldn't, or null.</summary>
    public static string? Unregister() => IsRegistered() ? Run("/Delete", "/TN", Name, "/F") : null;

    public static bool IsRegistered() => Run("/Query", "/TN", Name) is null;

    internal static string Xml(string exePath, string userSid, DateTime startLocal) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>Tidy</Author>
            <Description>Cleans the caches Tidy marks Safe to Clean on the schedule you chose, and tells you when disk space runs low. Turn both off in Tidy's Settings to remove this task.</Description>
          </RegistrationInfo>
          <Triggers>
            <TimeTrigger>
              <StartBoundary>{startLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary>
              <Enabled>true</Enabled>
              <Repetition>
                <Interval>PT1H</Interval>
                <StopAtDurationEnd>false</StopAtDurationEnd>
              </Repetition>
            </TimeTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userSid)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"{SecurityElement.Escape(exePath)}"</Command>
              <Arguments>{Argument}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    private static string? Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? null : error.Result.Trim() is { Length: > 0 } message ? message : $"Task Scheduler failed with code {process.ExitCode}.";
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return e.Message;
        }
    }
}
