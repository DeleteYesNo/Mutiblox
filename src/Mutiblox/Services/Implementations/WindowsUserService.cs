using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

/// <summary>
/// Manages Windows user accounts for per-instance Roblox isolation.
/// Each slot (1, 2, 3...) maps to a Windows user (Mutiblox_1, Mutiblox_2...).
/// Slot 0 = the current user (no extra account needed).
/// </summary>
public sealed class WindowsUserService : IWindowsUserService
{
    private const string UserPrefix = "Mutiblox_";
    private readonly Dictionary<int, WindowsUserInfo> _users = new();
    private readonly string _credentialsPath;

    public WindowsUserService()
    {
        _credentialsPath = Path.Combine(Constants.AppDataPath, "winusers.json");
        LoadCredentials();
    }

    public int GetNextSlot(IReadOnlyList<RobloxInstance> runningInstances)
    {
        var usedSlots = runningInstances
            .Where(i => i.Status == InstanceStatus.Running)
            .Select(i => i.SlotIndex)
            .ToHashSet();

        // Slot 0 first, then 1, 2, 3...
        for (int i = 0; i <= 20; i++)
        {
            if (!usedSlots.Contains(i))
                return i;
        }
        return runningInstances.Count;
    }

    public async Task<Result<WindowsUserInfo>> EnsureUserAsync(int slotIndex)
    {
        if (slotIndex == 0)
            return Result<WindowsUserInfo>.Success(new WindowsUserInfo(
                Environment.UserName, "",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        if (_users.TryGetValue(slotIndex, out var existing))
        {
            // Verify the user still exists
            if (UserExists(existing.Username))
                return Result<WindowsUserInfo>.Success(existing);
        }

        // Create the user
        var username = $"{UserPrefix}{slotIndex}";
        var password = GeneratePassword();

        try
        {
            // Create user account (or reset password if exists)
            if (UserExists(username))
            {
                await RunNetCommand($"user {username} {password}");
            }
            else
            {
                await RunNetCommand($"user {username} {password} /add /Y");
                // Hide from login screen
                await RunRegCommand(
                    $"add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\SpecialAccounts\\UserList\" /v {username} /t REG_DWORD /d 0 /f");
            }

            // Initialize profile by logging in once
            var profilePath = await InitializeProfileAsync(username, password);
            if (profilePath == null)
                return Result<WindowsUserInfo>.Failure($"無法初始化 {username} 的使用者設定檔");

            // Grant Administrators full access to the new user's profile.
            // Required so we can create Roblox directory structure inside it.
            RunCmd("icacls", $"\"{profilePath}\" /grant \"Administrators:(OI)(CI)F\" /T /Q");

            var info = new WindowsUserInfo(username, password, profilePath);
            _users[slotIndex] = info;
            SaveCredentials();

            return Result<WindowsUserInfo>.Success(info);
        }
        catch (Exception ex)
        {
            return Result<WindowsUserInfo>.Failure($"建立使用者 {username} 失敗: {ex.Message}");
        }
    }

    public Result<int> LaunchProcess(int slotIndex, string exePath, string arguments, string workingDirectory)
    {
        if (slotIndex == 0)
        {
            // Current user — normal launch
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            });
            return proc != null
                ? Result<int>.Success(proc.Id)
                : Result<int>.Failure("無法啟動進程");
        }

        if (!_users.TryGetValue(slotIndex, out var userInfo))
            return Result<int>.Failure($"Slot {slotIndex} 的使用者不存在");

        try
        {
            // Grant the target user read+execute access to the Roblox directory.
            GrantDirectoryAccess(userInfo.Username, workingDirectory);

            // Prepare Roblox directory structure under the new user's profile.
            // Creates symlinks to the real Versions dir + per-instance data files.
            PrepareRobloxDirectoryForUser(userInfo.ProfilePath, exePath);

            // Ensure the user has full access to their own Roblox data directory
            var userRobloxDir = Path.Combine(userInfo.ProfilePath, "AppData", "Local", "Roblox");
            GrantDirectoryAccess(userInfo.Username, userRobloxDir);

            // Use .NET's built-in user process launch (handles desktop access internally)
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                UserName = userInfo.Username,
                PasswordInClearText = userInfo.Password,
                Domain = ".",
                LoadUserProfile = true,
            };

            using var proc = Process.Start(psi);
            return proc != null
                ? Result<int>.Success(proc.Id)
                : Result<int>.Failure("無法啟動進程");
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"啟動失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Grants read+execute access to a directory for a specific user.
    /// </summary>
    private static void GrantDirectoryAccess(string username, string directory)
    {
        try
        {
            var parent = Path.GetDirectoryName(directory);
            if (parent != null)
            {
                RunCmd("icacls", $"\"{parent}\" /grant \"{username}:(OI)(CI)RX\" /T /Q");
                RunCmd("icacls", $"\"{directory}\" /grant \"{username}:(OI)(CI)RX\" /T /Q");
            }
        }
        catch { }
    }

    /// <summary>
    /// Prepares the Roblox directory structure under the target user's profile.
    /// Creates symlinks so Roblox finds its installation files (Versions, etc.)
    /// while having per-instance data files (cookies, database).
    /// </summary>
    private static void PrepareRobloxDirectoryForUser(string profilePath, string robloxExePath)
    {
        try
        {
            var userLocalAppData = Path.Combine(profilePath, "AppData", "Local");
            var userRobloxDir = Path.Combine(userLocalAppData, "Roblox");
            var userVersionsDir = Path.Combine(userRobloxDir, "Versions");
            var userLocalStorage = Path.Combine(userRobloxDir, "LocalStorage");

            Directory.CreateDirectory(userRobloxDir);
            Directory.CreateDirectory(userLocalStorage);

            // Find the real Roblox root (parent of Versions directory)
            var realVersionDir = Path.GetDirectoryName(robloxExePath)!; // e.g. .../Versions/version-xxx
            var realVersionsDir = Path.GetDirectoryName(realVersionDir)!; // e.g. .../Versions
            var realRobloxRoot = Path.GetDirectoryName(realVersionsDir)!; // e.g. .../Roblox

            // Create junction: user's Versions → real Versions directory
            if (!Directory.Exists(userVersionsDir))
            {
                RunCmd("cmd", $"/c mklink /J \"{userVersionsDir}\" \"{realVersionsDir}\"");
            }

            // Copy essential config files from the real Roblox directory
            foreach (var fileName in new[] { "GlobalBasicSettings_13.xml", "AnalystSettings_13.xml" })
            {
                var src = Path.Combine(realRobloxRoot, fileName);
                var dst = Path.Combine(userRobloxDir, fileName);
                if (File.Exists(src) && !File.Exists(dst))
                    try { File.Copy(src, dst); } catch { }
            }

            // Copy appStorage.json for initial settings
            var srcStorage = Path.Combine(realRobloxRoot, "LocalStorage", "appStorage.json");
            var dstStorage = Path.Combine(userLocalStorage, "appStorage.json");
            if (File.Exists(srcStorage) && !File.Exists(dstStorage))
                try { File.Copy(srcStorage, dstStorage); } catch { }
        }
        catch { }
    }

    private static void RunCmd(string exe, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(15000);
        }
        catch { }
    }

    public Result InjectCookieForUser(int slotIndex, string cookie)
    {
        if (slotIndex == 0)
        {
            // Current user — write directly
            WriteCookieFile(cookie, Constants.RobloxCookiesPath);
            return Result.Success();
        }

        if (!_users.TryGetValue(slotIndex, out var userInfo))
            return Result.Failure($"Slot {slotIndex} 的使用者不存在");

        // Need to encrypt with the target user's DPAPI context.
        // DPAPI requires the user's profile (registry hive) to be loaded.
        if (!NativeMethods.LogonUser(userInfo.Username, ".", userInfo.Password,
            NativeMethods.LOGON32_LOGON_INTERACTIVE, NativeMethods.LOGON32_PROVIDER_DEFAULT,
            out var token))
            return Result.Failure($"LogonUser 失敗: {Marshal.GetLastWin32Error()}");

        // Load the user profile so DPAPI can access the master key
        var profileInfo = new NativeMethods.PROFILEINFO
        {
            dwSize = Marshal.SizeOf<NativeMethods.PROFILEINFO>(),
            lpUserName = userInfo.Username,
        };

        bool profileLoaded = NativeMethods.LoadUserProfile(token, ref profileInfo);

        try
        {
            if (!profileLoaded)
                return Result.Failure($"LoadUserProfile 失敗: {Marshal.GetLastWin32Error()}");

            var cookiesPath = Path.Combine(userInfo.ProfilePath,
                "AppData", "Local", "Roblox", "LocalStorage", "RobloxCookies.dat");

            // Run DPAPI encryption under the target user's context
            WindowsIdentity.RunImpersonated(new Microsoft.Win32.SafeHandles.SafeAccessTokenHandle(token), () =>
            {
                WriteCookieFile(cookie, cookiesPath);
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Cookie 注入失敗: {ex.Message}");
        }
        finally
        {
            if (profileLoaded)
                NativeMethods.UnloadUserProfile(token, profileInfo.hProfile);
            NativeMethods.CloseHandle(token);
        }
    }

    private static void WriteCookieFile(string cookie, string cookiesPath)
    {
        var sb = new StringBuilder();
        sb.Append("#HttpOnly_.roblox.com\tTRUE\t/\tTRUE\t0\t.ROBLOSECURITY\t");
        sb.Append(cookie);

        var plainBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        var base64 = Convert.ToBase64String(encryptedBytes);

        var json = JsonSerializer.Serialize(new
        {
            CookiesVersion = "1",
            CookiesData = base64
        });

        Directory.CreateDirectory(Path.GetDirectoryName(cookiesPath)!);
        File.WriteAllText(cookiesPath, json);
    }

    private static Task<string?> InitializeProfileAsync(string username, string password)
    {
        // LogonUser with INTERACTIVE to create the profile
        if (!NativeMethods.LogonUser(username, ".", password,
            NativeMethods.LOGON32_LOGON_INTERACTIVE, NativeMethods.LOGON32_PROVIDER_DEFAULT,
            out var token))
            return Task.FromResult<string?>(null);

        try
        {
            // Get profile directory
            int size = 0;
            NativeMethods.GetUserProfileDirectory(token, null, ref size);
            var buffer = new char[size];
            if (!NativeMethods.GetUserProfileDirectory(token, buffer, ref size))
            {
                // Profile might not exist yet — derive the path.
                // LOGON_WITH_PROFILE flag in CreateProcessWithLogonW will create it on first launch.
                var path = Path.Combine(
                    Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!,
                    username);
                return Task.FromResult<string?>(path);
            }

            return Task.FromResult<string?>(new string(buffer, 0, size - 1));
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static bool UserExists(string username)
    {
        try
        {
            var psi = new ProcessStartInfo("net", $"user {username}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task RunNetCommand(string args)
    {
        var psi = new ProcessStartInfo("net", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0 && proc.ExitCode != 2) // 2 = user already exists
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"net command failed ({proc.ExitCode}): {err}");
        }
    }

    private static async Task RunRegCommand(string args)
    {
        var psi = new ProcessStartInfo("reg", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();
    }

    private static string GeneratePassword()
    {
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$";
        var bytes = RandomNumberGenerator.GetBytes(24);
        var sb = new StringBuilder(24);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private void SaveCredentials()
    {
        try
        {
            var data = _users.ToDictionary(
                kv => kv.Key.ToString(),
                kv => new { kv.Value.Username, kv.Value.Password, kv.Value.ProfilePath });

            // Encrypt with current user's DPAPI
            var json = JsonSerializer.Serialize(data);
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
            File.WriteAllBytes(_credentialsPath, encrypted);
        }
        catch { }
    }

    private void LoadCredentials()
    {
        try
        {
            if (!File.Exists(_credentialsPath)) return;

            var encrypted = File.ReadAllBytes(_credentialsPath);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));

            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data == null) return;

            foreach (var (key, value) in data)
            {
                if (!int.TryParse(key, out var slot)) continue;
                var username = value.GetProperty("Username").GetString() ?? "";
                var password = value.GetProperty("Password").GetString() ?? "";
                var profilePath = value.GetProperty("ProfilePath").GetString() ?? "";

                if (!string.IsNullOrEmpty(username) && UserExists(username))
                    _users[slot] = new WindowsUserInfo(username, password, profilePath);
            }
        }
        catch { }
    }
}
