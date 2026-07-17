using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Secrets;

/// <summary>No encryption — values are stored as-is. The default when no key is supplied; fine for dev /
/// an already-encrypted backing store, but NOT real at-rest protection.</summary>
public sealed class NullSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

/// <summary>AES-256-GCM authenticated encryption with an app-supplied 32-byte key (BYO key — Lyntai never
/// generates or persists it). Output is base64(nonce | tag | ciphertext); tampering fails decryption.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceLen = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagLen = 16;   // AesGcm.TagByteSizes.MaxSize

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
        _key = (byte[])key.Clone();
    }

    public string Protect(string plaintext)
    {
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var tag = new byte[TagLen];
        var ct = new byte[pt.Length];
        using (var aes = new AesGcm(_key, TagLen)) aes.Encrypt(nonce, pt, ct, tag);

        var blob = new byte[NonceLen + TagLen + ct.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceLen);
        ct.CopyTo(blob, NonceLen + TagLen);
        return Convert.ToBase64String(blob);
    }

    public string Unprotect(string ciphertext)
    {
        var blob = Convert.FromBase64String(ciphertext);
        var nonce = blob.AsSpan(0, NonceLen);
        var tag = blob.AsSpan(NonceLen, TagLen);
        var ct = blob.AsSpan(NonceLen + TagLen);
        var pt = new byte[ct.Length];
        using (var aes = new AesGcm(_key, TagLen)) aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}
