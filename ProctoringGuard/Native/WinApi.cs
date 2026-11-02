using System.Runtime.InteropServices;

namespace ProctoringGuard.Native;

[StructLayout(LayoutKind.Sequential)]
public struct BLUETOOTH_ADDRESS
{
    // Windows defines this as an 8-byte union (ULONGLONG / BYTE[6])
    public ulong ullLong;
    public override string ToString()
    {
        var b = BitConverter.GetBytes(ullLong);
        return string.Join(":", Enumerable.Range(0, 6).Select(i => b[5 - i].ToString("X2")));
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct BLUETOOTH_DEVICE_INFO
{
    public uint              dwSize;
    public BLUETOOTH_ADDRESS Address;
    public uint              ulClassofDevice;
    [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
    [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
    [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
    public SYSTEMTIME        stLastSeen;
    public SYSTEMTIME        stLastUsed;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
    public string            szName;
}

[StructLayout(LayoutKind.Sequential)]
public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
{
    public uint  dwSize;
    [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
    [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
    [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
    [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
    [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
    public byte  cTimeoutMultiplier;
    public IntPtr hRadio;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEMTIME
{
    public ushort wYear, wMonth, wDayOfWeek, wDay;
    public ushort wHour, wMinute, wSecond, wMilliseconds;
}

public static class BluetoothApi
{
    const string DLL = "bthprops.cpl";

    [DllImport(DLL, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextDevice(
        IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindDeviceClose(IntPtr hFind);
}
