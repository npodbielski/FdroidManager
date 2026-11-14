using System.Security.Cryptography;
using System.Text;
using FdroidMgr.Api.Abstractions;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FdroidMgr.Api.Services;

public class PasswordProtector(FdroidDb db, IConfiguration config, IEnvironmentService env) : IPasswordProtector
{
    private const string V2Prefix = "v2:";

    private const int Pbkdf2Iterations = 600_000;

    private byte[]? _cachedKey;

    public string Protect(string plaintext)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        using (var gcm = new AesGcm(key, 16))
        {
            gcm.Encrypt(nonce, plain, cipher, tag);
        }

        return $"{V2Prefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipher)}";
    }

    public string Unprotect(string blob)
    {
        if (!IsLegacy(blob))
        {
            return UnprotectV2(blob);
        }

        return UnprotectLegacy(blob);
    }

    public bool IsLegacy(string blob)
        => !blob.StartsWith(V2Prefix, StringComparison.Ordinal);

    private string UnprotectV2(string blob)
    {
        var parts = blob[3..].Split(':', 3);
        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plain = new byte[cipher.Length];
        using (var gcm = new AesGcm(GetKey(), 16))
        {
            gcm.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private string UnprotectLegacy(string blob)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(MasterPass()));
        var parts = blob.Split(':', 2);
        var iv = Convert.FromBase64String(parts[0]);
        var cipher = Convert.FromBase64String(parts[1]);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetKey()
    {
        if (_cachedKey is not null)
        {
            return _cachedKey;
        }

        var salt = GetOrCreateSalt();
        var key = Rfc2898DeriveBytes.Pbkdf2(MasterPass(), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        _cachedKey = key;
        return key;
    }

    private byte[] GetOrCreateSalt()
    {
        var cfg = db.RepoConfig.AsNoTracking().FirstOrDefault();
        if (cfg?.MasterKeySalt is { Length: > 0 } existing)
        {
            return Convert.FromBase64String(existing);
        }

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var tracked = db.RepoConfig.FirstOrDefault();
        if (tracked is null)
        {
            tracked = new RepoConfig();
            db.RepoConfig.Add(tracked);
        }

        tracked.MasterKeySalt = salt;
        db.SaveChanges();
        return Convert.FromBase64String(salt);
    }

    private string MasterPass()
        => config["FDROID_MASTER_KEY"]
           ?? env.GetEnvironmentVariable("FDROID_MASTER_KEY")
           ?? throw new InvalidOperationException("FDROID_MASTER_KEY not set — cannot protect secrets");
}
