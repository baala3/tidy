using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tidy.Core.Platform;

/// <summary>
/// The working directories of the user's running processes: a terminal, a dev server
/// or an agent sitting inside a project means the project is in use. Windows has no
/// public API for another process's current directory, so this reads it from the
/// process environment block, the same place Process Explorer gets it.
/// Processes that cannot be opened (elevated, other users, protected) are skipped.
/// </summary>
public static class ProcessInspector
{
    public static IReadOnlySet<string> WorkingDirectories()
    {
        var result = new HashSet<string>(PathUtil.Comparer);
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session || process.Id == Environment.ProcessId) continue;
                    var cwd = ReadWorkingDirectory(process.Id);
                    var normalized = PathUtil.Normalize(cwd);
                    if (normalized is not null && normalized.Length > 3) result.Add(normalized);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return result;
    }

    internal static unsafe string? ReadWorkingDirectory(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            // A 32-bit process keeps its current directory in its own 32-bit PEB.
            if (IsWow64Process(handle, out var wow64) && wow64) return ReadWow64(handle);

            PROCESS_BASIC_INFORMATION info;
            if (NtQueryInformationProcess(handle, 0, &info, sizeof(PROCESS_BASIC_INFORMATION), out _) != 0) return null;
            // PEB.ProcessParameters at 0x20; RTL_USER_PROCESS_PARAMETERS.CurrentDirectory.DosPath at 0x38.
            if (!ReadPointer(handle, info.PebBaseAddress + 0x20, out var parameters)) return null;
            return ReadUnicodeString64(handle, parameters + 0x38);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static unsafe string? ReadWow64(IntPtr handle)
    {
        IntPtr peb32;
        if (NtQueryInformationProcess(handle, 26 /* ProcessWow64Information */, &peb32, IntPtr.Size, out _) != 0 || peb32 == IntPtr.Zero) return null;
        uint parameters;
        if (!ReadProcessMemory(handle, peb32 + 0x10, &parameters, 4, out _)) return null;
        // RTL_USER_PROCESS_PARAMETERS32.CurrentDirectory.DosPath at 0x24.
        ushort length;
        uint buffer;
        if (!ReadProcessMemory(handle, (IntPtr)(parameters + 0x24), &length, 2, out _)) return null;
        if (!ReadProcessMemory(handle, (IntPtr)(parameters + 0x28), &buffer, 4, out _)) return null;
        return ReadChars(handle, (IntPtr)buffer, length);
    }

    private static unsafe string? ReadUnicodeString64(IntPtr handle, IntPtr address)
    {
        ushort length;
        IntPtr buffer;
        if (!ReadProcessMemory(handle, address, &length, 2, out _)) return null;
        if (!ReadPointer(handle, address + 8, out buffer)) return null;
        return ReadChars(handle, buffer, length);
    }

    private static unsafe string? ReadChars(IntPtr handle, IntPtr buffer, ushort byteLength)
    {
        if (byteLength == 0 || byteLength > 32_767 * 2) return null;
        var chars = new char[byteLength / 2];
        fixed (char* p = chars)
        {
            if (!ReadProcessMemory(handle, buffer, p, byteLength, out _)) return null;
        }
        return new string(chars);
    }

    private static unsafe bool ReadPointer(IntPtr handle, IntPtr address, out IntPtr value)
    {
        IntPtr result;
        var ok = ReadProcessMemory(handle, address, &result, IntPtr.Size, out _);
        value = result;
        return ok;
    }

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int PROCESS_VM_READ = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool IsWow64Process(IntPtr handle, out bool wow64);

    [DllImport("kernel32.dll")]
    private static extern unsafe bool ReadProcessMemory(IntPtr handle, IntPtr address, void* buffer, nint size, out nint read);

    [DllImport("ntdll.dll")]
    private static extern unsafe int NtQueryInformationProcess(IntPtr handle, int infoClass, void* info, int length, out int returned);
}
