using System.Diagnostics;
using Tidy.Core.Platform;

namespace Tidy.Core.Tests;

public class ProcessInspectorTests
{
    [Fact]
    public void ReadsAnotherProcesssWorkingDirectory()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tidy-tests", "cwd-" + Guid.NewGuid().ToString("N"))).FullName;
        // ping itself, not through cmd: a grandchild could still hold the folder after the wait below.
        using var child = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            WorkingDirectory = dir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var expected = PathUtil.Normalize(PathUtil.LongPath(dir));
            // A busy machine may not have set the new process up yet; give it a few seconds.
            string? cwd = null;
            for (var i = 0; i < 50 && !string.Equals(cwd, expected, StringComparison.OrdinalIgnoreCase); i++)
            {
                cwd = PathUtil.Normalize(ProcessInspector.ReadWorkingDirectory(child.Id));
                if (!string.Equals(cwd, expected, StringComparison.OrdinalIgnoreCase)) Thread.Sleep(100);
            }
            Assert.Equal(expected, cwd, ignoreCase: true);
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            Directory.Delete(dir);
        }
    }
}
