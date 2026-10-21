using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Tidy.Core.Cloud;

namespace Tidy.Core.Tests;

/// <summary>
/// A folder registered with the Windows Cloud Files API, the way OneDrive registers its own,
/// with a provider connected for as long as the test runs. Files written here become
/// placeholders, so the OneDrive code runs against the real platform without an account.
/// </summary>
public sealed class TestSyncRoot : IDisposable
{
    private readonly long _connection;

    public string Path { get; }

    public TestSyncRoot(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
        var registration = new CF_SYNC_REGISTRATION
        {
            StructSize = (uint)Marshal.SizeOf<CF_SYNC_REGISTRATION>(),
            ProviderName = "Tidy tests",
            ProviderVersion = "1.0",
            ProviderId = Guid.NewGuid(),
        };
        var policies = new CF_SYNC_POLICIES
        {
            StructSize = (uint)Marshal.SizeOf<CF_SYNC_POLICIES>(),
            Hydration = 2, // CF_HYDRATION_POLICY_FULL
            Population = 2, // CF_POPULATION_POLICY_ALWAYS_FULL
        };
        Check(CfRegisterSyncRoot(path, ref registration, ref policies, 0));
        Check(CfConnectSyncRoot(path, [new CF_CALLBACK_REGISTRATION { Type = -1 }], IntPtr.Zero, 0, out _connection));
    }

    /// <summary>A placeholder with its contents on this PC: a file written here, then converted.</summary>
    public string Placeholder(string relative, int bytes, bool inSync = true)
    {
        var path = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        new Random(relative.GetHashCode(StringComparison.Ordinal)).NextBytes(data);
        File.WriteAllBytes(path, data);
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var identity = Encoding.UTF8.GetBytes(relative);
        Check(CfConvertToPlaceholder(handle, identity, identity.Length, inSync ? CF_CONVERT_FLAG_MARK_IN_SYNC : 0, out _, IntPtr.Zero));
        return path;
    }

    public static void SetPinState(string path, CloudPinState state)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Check(CfSetPinState(handle, state, 0, IntPtr.Zero));
    }

    public void Dispose()
    {
        _ = CfDisconnectSyncRoot(_connection);
        _ = CfUnregisterSyncRoot(Path);
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new InvalidOperationException($"Cloud Files call failed: 0x{hr:X8}");
    }

    private const int CF_CONVERT_FLAG_MARK_IN_SYNC = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CF_SYNC_REGISTRATION
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string ProviderName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ProviderVersion;
        public IntPtr SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
        public Guid ProviderId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CF_SYNC_POLICIES
    {
        public uint StructSize;
        public ushort Hydration;
        public ushort HydrationModifier;
        public ushort Population;
        public ushort PopulationModifier;
        public uint InSync;
        public uint HardLink;
        public uint PlaceholderManagement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CF_CALLBACK_REGISTRATION
    {
        public int Type;
        public IntPtr Callback;
    }

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfRegisterSyncRoot(string path, ref CF_SYNC_REGISTRATION registration, ref CF_SYNC_POLICIES policies, int flags);

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfUnregisterSyncRoot(string path);

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfConnectSyncRoot(string path, CF_CALLBACK_REGISTRATION[] callbacks, IntPtr context, int flags, out long key);

    [DllImport("cldapi.dll")]
    private static extern int CfDisconnectSyncRoot(long key);

    [DllImport("cldapi.dll")]
    private static extern int CfConvertToPlaceholder(SafeFileHandle handle, byte[] identity, int identityLength, int flags, out long usn, IntPtr overlapped);

    [DllImport("cldapi.dll")]
    private static extern int CfSetPinState(SafeFileHandle handle, CloudPinState state, int flags, IntPtr overlapped);
}
