using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class BrowserCookieService : IBrowserCookieService
{
    public Result<List<BrowserCookieEntry>> GetRobloxCookies()
    {
        var entries = new List<BrowserCookieEntry>();

        TryAddChromiumCookie(entries, "Chrome",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data"));

        TryAddChromiumCookie(entries, "Edge",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data"));

        if (entries.Count == 0)
            return Result<List<BrowserCookieEntry>>.Failure(
                "未在瀏覽器中找到 Roblox Cookie。請確認已在 Chrome 或 Edge 中登入 Roblox。");

        return Result<List<BrowserCookieEntry>>.Success(entries);
    }

    private static void TryAddChromiumCookie(List<BrowserCookieEntry> entries, string browserName, string userDataPath)
    {
        try
        {
            if (!Directory.Exists(userDataPath))
                return;

            var masterKey = GetChromiumMasterKey(userDataPath);
            if (masterKey == null)
                return;

            string[] profiles = ["Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5"];

            foreach (var profile in profiles)
            {
                var cookieDb = Path.Combine(userDataPath, profile, "Network", "Cookies");
                if (!File.Exists(cookieDb))
                    cookieDb = Path.Combine(userDataPath, profile, "Cookies");
                if (!File.Exists(cookieDb))
                    continue;

                var cookie = ExtractCookieFromDb(cookieDb, masterKey);
                if (!string.IsNullOrEmpty(cookie))
                {
                    entries.Add(new BrowserCookieEntry($"{browserName} ({profile})", cookie));
                    return;
                }
            }
        }
        catch
        {
            // Silently skip browsers that fail
        }
    }

    private static byte[]? GetChromiumMasterKey(string userDataPath)
    {
        try
        {
            var localStatePath = Path.Combine(userDataPath, "Local State");
            if (!File.Exists(localStatePath))
                return null;

            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(localStatePath));

            if (!json.TryGetProperty("os_crypt", out var osCrypt) ||
                !osCrypt.TryGetProperty("encrypted_key", out var encKeyProp))
                return null;

            var encKeyBase64 = encKeyProp.GetString();
            if (string.IsNullOrEmpty(encKeyBase64))
                return null;

            var encKeyBytes = Convert.FromBase64String(encKeyBase64);

            // Verify and strip "DPAPI" prefix
            if (encKeyBytes.Length <= 5 ||
                Encoding.ASCII.GetString(encKeyBytes, 0, 5) != "DPAPI")
                return null;

            return ProtectedData.Unprotect(
                encKeyBytes[5..], null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractCookieFromDb(string dbPath, byte[] masterKey)
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"mutiblox_{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(dbPath, tempDb, true);
            // Copy WAL/SHM for recent writes
            CopyIfExists(dbPath + "-wal", tempDb + "-wal");
            CopyIfExists(dbPath + "-shm", tempDb + "-shm");

            var bytes = File.ReadAllBytes(tempDb);
            return SearchAndDecryptCookie(bytes, masterKey);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(tempDb);
            TryDelete(tempDb + "-wal");
            TryDelete(tempDb + "-shm");
        }
    }

    private static string? SearchAndDecryptCookie(byte[] data, byte[] masterKey)
    {
        var marker = ".ROBLOSECURITY"u8;

        for (int i = 0; i <= data.Length - marker.Length; i++)
        {
            if (!data.AsSpan(i, marker.Length).SequenceEqual(marker))
                continue;

            // Found cookie name; search nearby for v10-encrypted blob
            int lo = Math.Max(0, i - 2048);
            int hi = Math.Min(data.Length - 31, i + 2048);

            for (int j = lo; j < hi; j++)
            {
                if (data[j] != (byte)'v' || data[j + 1] != (byte)'1' || data[j + 2] != (byte)'0')
                    continue;

                var result = TryDecrypt(data, j, masterKey);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static string? TryDecrypt(byte[] data, int offset, byte[] key)
    {
        // v10 format: "v10"(3) + nonce(12) + ciphertext(N) + tag(16)
        const int headerLen = 3;
        const int nonceLen = 12;
        const int tagLen = 16;
        const int minPayload = headerLen + nonceLen + tagLen + 1;

        if (offset + minPayload > data.Length)
            return null;

        var nonce = data[(offset + headerLen)..(offset + headerLen + nonceLen)];
        int dataStart = offset + headerLen + nonceLen;

        // ROBLOSECURITY cookies are typically 500-1100 bytes
        for (int plainLen = 500; plainLen <= 1200; plainLen++)
        {
            int needed = dataStart + plainLen + tagLen;
            if (needed > data.Length)
                break;

            try
            {
                var ct = data[dataStart..(dataStart + plainLen)];
                var tag = data[(dataStart + plainLen)..(dataStart + plainLen + tagLen)];
                var pt = new byte[plainLen];

                using var aes = new AesGcm(key, tagLen);
                aes.Decrypt(nonce, ct, tag, pt);

                var text = Encoding.UTF8.GetString(pt).TrimEnd('\0');
                if (text.StartsWith("_|WARNING") || (text.Length > 200 && text.All(char.IsAscii)))
                    return text;
            }
            catch (CryptographicException)
            {
                // Wrong length, keep trying
            }
        }

        return null;
    }

    private static void CopyIfExists(string src, string dst)
    {
        try { if (File.Exists(src)) File.Copy(src, dst, true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
