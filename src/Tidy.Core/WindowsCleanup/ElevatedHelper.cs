using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Tidy.Core.Catalog;

namespace Tidy.Core.WindowsCleanup;

/// <summary>
/// The part of Tidy that runs with administrator rights: <c>Tidy.exe --elevated</c>, started
/// with a UAC prompt for one job. It only measures or runs the Disk Cleanup handlers named in
/// catalog.json, never anything else, and reports back over a pipe the app that started it
/// owns. It takes no paths from anyone.
/// </summary>
public static class ElevatedHelper
{
    public const string Argument = "--elevated";
    public const string Measure = "measure";
    public const string Clean = "clean";

    /// <summary><c>&lt;pipe&gt; &lt;parent pid&gt; measure|clean &lt;handler key&gt;...</c>. Returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, ICleanupHandlers handlers)
    {
        if (args.Count < 4 || !int.TryParse(args[1], out var parent) || args[2] is not (Measure or Clean)) return 2;
        var allowed = CleanupCatalog.Default.WindowsCleanup.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var keys = args.Skip(3).ToList();
        if (!keys.All(allowed.Contains)) return 3;

        // Identification only: whoever owns the pipe learns who connected, but can't act as this process.
        using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.Identification);
        pipe.Connect(TimeSpan.FromSeconds(30));
        // The name was on the command line; make sure it's the app that started this that's listening.
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != parent) return 4;

        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        void Send(ElevatedMessage message) => writer.WriteLine(JsonSerializer.Serialize(message, ElevatedJsonContext.Default.ElevatedMessage));

        var volume = Path.GetPathRoot(Environment.SystemDirectory)!;
        foreach (var key in keys)
        {
            Send(new ElevatedMessage(ElevatedMessageType.Working, key));
            if (args[2] == Measure)
            {
                Send(new ElevatedMessage(ElevatedMessageType.Size, key, handlers.Measure(key, volume) ?? 0));
            }
            else
            {
                var error = handlers.Purge(key, volume, bytes => Send(new ElevatedMessage(ElevatedMessageType.Progress, key, bytes)), out var freed);
                Send(new ElevatedMessage(ElevatedMessageType.Done, key, freed, error));
            }
        }
        return 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out int processId);
}

/// <summary>
/// Starts the elevated helper for one job and relays what it sends. The pipe is created first,
/// as its only instance, and only this user and administrators may open it.
/// </summary>
public static class ElevatedSession
{
    public sealed record Outcome(bool Started, string? Problem);

    /// <summary>A started helper: why it didn't start, or a task that ends with its exit code.</summary>
    public sealed record Launched(string? Problem, Task<int> Exited);

    /// <summary>Starts the helper; by default Tidy itself, elevated through UAC. Tests run it in-process.</summary>
    public static Func<string, Task<Launched>> Launch { get; set; } = LaunchElevated;

    public static async Task<Outcome> RunAsync(string job, IReadOnlyList<string> keys, Action<ElevatedMessage> onMessage, CancellationToken cancellation = default)
    {
        var name = "tidy-" + Guid.NewGuid().ToString("N");
        using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, Security());

        var arguments = $"{ElevatedHelper.Argument} {name} {Environment.ProcessId} {job} " + string.Join(' ', keys.Select(k => $"\"{k}\""));
        var connected = pipe.WaitForConnectionAsync(cancellation);
        var launched = await Launch(arguments);
        if (launched.Problem is not null) return new Outcome(false, launched.Problem);
        // A helper that refuses its job exits without connecting; don't wait for it.
        var first = await Task.WhenAny(connected, launched.Exited, Task.Delay(TimeSpan.FromMinutes(2), cancellation));
        if (first != connected) return new Outcome(false, "Tidy's administrator helper stopped before it could start.");
        await connected;

        using var reader = new StreamReader(pipe);
        while (await reader.ReadLineAsync(cancellation) is { } line)
        {
            if (JsonSerializer.Deserialize(line, ElevatedJsonContext.Default.ElevatedMessage) is { } message) onMessage(message);
        }
        return new Outcome(true, null);
    }

    private static PipeSecurity Security()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));
        return security;
    }

    private static Task<Launched> LaunchElevated(string arguments) => Task.Run(() =>
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null) return new Launched("Tidy's administrator helper couldn't start.", Task.FromResult(-1));
            return new Launched(null, WaitForExit(process));
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return new Launched("Cancelled: Windows asked for permission and it was declined.", Task.FromResult(-1));
        }
        catch (Win32Exception e)
        {
            return new Launched($"Tidy's administrator helper couldn't start ({e.Message.Trim()}).", Task.FromResult(-1));
        }
    });

    private static async Task<int> WaitForExit(System.Diagnostics.Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }
}
