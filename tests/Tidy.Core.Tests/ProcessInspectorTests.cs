using System.Diagnostics;
using Tidy.Core.Platform;

namespace Tidy.Core.Tests;

public class ProcessInspectorTests
{
    [Fact]
    public void ReadsAnotherProcesssWorkingDirectory()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tidy-tests", "cwd-" + Guid.NewGuid().ToString("N"))).FullName;
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            WorkingDirectory = dir,
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            Thread.Sleep(500);
            var cwd = PathUtil.Normalize(ProcessInspector.ReadWorkingDirectory(child.Id));
            Assert.Equal(PathUtil.Normalize(PathUtil.LongPath(dir)), cwd, ignoreCase: true);
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            Directory.Delete(dir);
        }
    }
}
