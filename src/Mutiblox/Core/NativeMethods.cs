using System.Runtime.InteropServices;

namespace Mutiblox.Core;

internal static partial class NativeMethods
{
    // Process access rights - intentionally minimal, NO VM_READ/WRITE
    public const uint PROCESS_DUP_HANDLE = 0x0040;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // DuplicateHandle flags
    public const uint DUPLICATE_CLOSE_SOURCE = 0x1;

    // NT status
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    public const int STATUS_SUCCESS = 0;

    // SystemInformationClass
    public const int SystemExtendedHandleInformation = 64;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public nint Object;
        public nint UniqueProcessId;
        public nint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    // P/Invoke declarations
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int systemInformationClass,
        nint systemInformation,
        int systemInformationLength,
        out int returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryObject(
        nint handle,
        int objectInformationClass,
        nint objectInformation,
        int objectInformationLength,
        out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(
        uint processAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    // Window messages for anti-AFK
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;

    // Virtual key codes
    public const int VK_W = 0x57;
    public const int VK_SPACE = 0x20;
    public const int VK_F13 = 0x7C;  // F13 - doesn't interfere with gameplay

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    // === CreateProcessWithLogonW / User Isolation ===

    public const int LOGON32_LOGON_INTERACTIVE = 2;
    public const int LOGON32_PROVIDER_DEFAULT = 0;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const uint LOGON_WITH_PROFILE = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    // CreateProcessWithLogonW uses DllImport because STARTUPINFOW contains
    // string fields that source-generated LibraryImport cannot marshal.
    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithLogonW",
        SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessWithLogonW(
        string lpUsername,
        string? lpDomain,
        string lpPassword,
        uint dwLogonFlags,
        string? lpApplicationName,
        string lpCommandLine,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("advapi32.dll", EntryPoint = "LogonUserW",
        SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out nint phToken);

    [LibraryImport("userenv.dll", EntryPoint = "CreateEnvironmentBlock", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateEnvironmentBlock(
        out nint lpEnvironment,
        nint hToken,
        [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [LibraryImport("userenv.dll", EntryPoint = "DestroyEnvironmentBlock", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyEnvironmentBlock(nint lpEnvironment);

    [LibraryImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW",
        SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetUserProfileDirectory(
        nint hToken, char[]? lpProfileDir, ref int lpcchSize);

    // LoadUserProfile / UnloadUserProfile — required for DPAPI under impersonation
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROFILEINFO
    {
        public int dwSize;
        public int dwFlags;
        public string? lpUserName;
        public string? lpProfilePath;
        public string? lpDefaultPath;
        public string? lpServerName;
        public string? lpPolicyPath;
        public nint hProfile;
    }

    [DllImport("userenv.dll", EntryPoint = "LoadUserProfileW",
        SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LoadUserProfile(nint hToken, ref PROFILEINFO lpProfileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnloadUserProfile(nint hToken, nint hProfile);
}
