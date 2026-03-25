using System.Security.Cryptography;
using System.Text;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class DpapiEncryptionService : IEncryptionService
{
    // Additional entropy for DPAPI - unique to this application
    private static readonly byte[] Entropy = "Mutiblox.v1.entropy"u8.ToArray();

    public string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    public string Decrypt(string encryptedText)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedText);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
