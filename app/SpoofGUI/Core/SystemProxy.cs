using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SpoofGUI.Core;

internal static class SystemProxy
{
    private const string InternetSettingsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;
    private const int INTERNET_OPTION_PER_CONNECTION_OPTION = 75;

    private const int INTERNET_PER_CONN_FLAGS = 1;
    private const int INTERNET_PER_CONN_PROXY_SERVER = 2;
    private const int INTERNET_PER_CONN_PROXY_BYPASS = 3;

    private const int PROXY_TYPE_DIRECT = 0x00000001;
    private const int PROXY_TYPE_PROXY = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INTERNET_PER_CONN_OPTION_LIST
    {
        public int dwSize;
        public IntPtr pszConnection;
        public int dwOptionCount;
        public int dwOptionError;
        public IntPtr pOptions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INTERNET_PER_CONN_OPTION
    {
        public int dwOption;
        public INTERNET_PER_CONN_OPTION_Value Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INTERNET_PER_CONN_OPTION_Value
    {
        [FieldOffset(0)] public int dwValue;
        [FieldOffset(0)] public IntPtr pszValue;
        [FieldOffset(0)] public long ftValue;
    }

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public static void Enable(string proxyServer)
    {
        WriteRegistry(true, proxyServer);
        ApplyWinInet(true, proxyServer);
        AppLog.Info($"system proxy enabled -> {proxyServer}");
    }

    public static void Disable()
    {
        WriteRegistry(false, null);
        ApplyWinInet(false, null);
        AppLog.Info("system proxy disabled");
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
        if (key is null) return false;
        return key.GetValue("ProxyEnable") is int v && v != 0;
    }

    private static void WriteRegistry(bool enable, string? proxyServer)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("cannot open Internet Settings key");
        key.SetValue("ProxyEnable", enable ? 1 : 0, RegistryValueKind.DWord);
        if (enable && proxyServer is not null)
        {
            key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
            key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
        }
    }

    private static void ApplyWinInet(bool enable, string? proxyServer)
    {
        var options = new INTERNET_PER_CONN_OPTION[enable ? 3 : 1];
        options[0] = new INTERNET_PER_CONN_OPTION
        {
            dwOption = INTERNET_PER_CONN_FLAGS,
            Value = new INTERNET_PER_CONN_OPTION_Value
            {
                dwValue = enable ? PROXY_TYPE_PROXY | PROXY_TYPE_DIRECT : PROXY_TYPE_DIRECT,
            },
        };

        IntPtr serverPtr = IntPtr.Zero;
        IntPtr bypassPtr = IntPtr.Zero;
        if (enable && proxyServer is not null)
        {
            serverPtr = Marshal.StringToHGlobalAuto(proxyServer);
            bypassPtr = Marshal.StringToHGlobalAuto("<local>");
            options[1] = new INTERNET_PER_CONN_OPTION
            {
                dwOption = INTERNET_PER_CONN_PROXY_SERVER,
                Value = new INTERNET_PER_CONN_OPTION_Value { pszValue = serverPtr },
            };
            options[2] = new INTERNET_PER_CONN_OPTION
            {
                dwOption = INTERNET_PER_CONN_PROXY_BYPASS,
                Value = new INTERNET_PER_CONN_OPTION_Value { pszValue = bypassPtr },
            };
        }

        int optSize = Marshal.SizeOf<INTERNET_PER_CONN_OPTION>();
        IntPtr optionsPtr = Marshal.AllocHGlobal(optSize * options.Length);
        try
        {
            for (int i = 0; i < options.Length; i++)
            {
                Marshal.StructureToPtr(options[i], optionsPtr + i * optSize, false);
            }

            var list = new INTERNET_PER_CONN_OPTION_LIST
            {
                pszConnection = IntPtr.Zero,
                dwOptionCount = options.Length,
                dwOptionError = 0,
                pOptions = optionsPtr,
            };
            list.dwSize = Marshal.SizeOf<INTERNET_PER_CONN_OPTION_LIST>();

            IntPtr listPtr = Marshal.AllocHGlobal(list.dwSize);
            try
            {
                Marshal.StructureToPtr(list, listPtr, false);
                if (!InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, listPtr, list.dwSize))
                {
                    int err = Marshal.GetLastWin32Error();
                    AppLog.Warn($"InternetSetOption PER_CONN failed code={err}");
                }
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(listPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(optionsPtr);
            if (serverPtr != IntPtr.Zero) Marshal.FreeHGlobal(serverPtr);
            if (bypassPtr != IntPtr.Zero) Marshal.FreeHGlobal(bypassPtr);
        }
    }
}
