using System.Runtime.InteropServices;

namespace Tidy.App.Services;

/// <summary>
/// The COM class Windows calls when one of Tidy's notifications is clicked. The open window
/// registers it, so a click reaches that window. When Tidy isn't running, Windows starts it with
/// <see cref="Notifications.ActivatedArgument"/> and calls in once the new window registers.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ToastActivator : INotificationActivationCallback
{
    private const uint ClsctxLocalServer = 4;
    private const uint RegclsMultipleUse = 1;

    private static uint _cookie;

    /// <summary>Raised on a COM thread with the notification's launch arguments.</summary>
    public static event Action<string>? Clicked;

    /// <summary>If Windows refuses, clicks just open nothing; that's no reason to stop Tidy from starting.</summary>
    public static void Start()
    {
        var clsid = Guid.Parse(Notifications.ActivatorClsid);
        if (CoRegisterClassObject(ref clsid, new Factory(), ClsctxLocalServer, RegclsMultipleUse, out _cookie) != 0) _cookie = 0;
    }

    public static void Stop()
    {
        // The window is closing; if Windows won't let go, there is nothing more to do.
        if (_cookie != 0) _ = CoRevokeClassObject(_cookie);
        _cookie = 0;
    }

    public void Activate(string appUserModelId, string invokedArgs, IntPtr data, uint count) => Clicked?.Invoke(invokedArgs);

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class Factory : IClassFactory
    {
        private const int ClassNoAggregation = unchecked((int)0x80040110);

        public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            if (outer != IntPtr.Zero) return ClassNoAggregation;
            var unknown = Marshal.GetIUnknownForObject(new ToastActivator());
            try
            {
                return Marshal.QueryInterface(unknown, in iid, out instance);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public int LockServer(bool lockServer) => 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
}

[ComImport]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs, IntPtr data, uint count);
}

[ComImport]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockServer);
}
