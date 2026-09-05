using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using MediaOrganizer.Core.Platforms;
using MediaOrganizer.Core.Security;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android Keystore 凭据加密（FR-A10，ADR-0005 §7）：AES-256-GCM，密钥由 Keystore 生成且不可导出（硬件 backed，设备绑定）。
/// 存储格式 KS:base64(IV ‖ 密文)，与 CredentialCrypto 的 DPAPI:/B64: 前缀约定并存。
/// </summary>
public sealed class AndroidCredentialCrypto : ICredentialCrypto
{
    private const string KeyAlias = "mediaorganizer_master";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int GcmIvLength = 12;

    public string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            var cipherText = cipher.DoFinal(Encoding.UTF8.GetBytes(plain));
            var iv = cipher.GetIV();
            if (cipherText is null || iv is null) return ""; // 加密失败宁可不存凭据，不明文落盘
            var buf = new byte[iv.Length + cipherText.Length];
            Buffer.BlockCopy(iv, 0, buf, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, buf, iv.Length, cipherText.Length);
            return CredentialCrypto.KeystorePrefix + Convert.ToBase64String(buf);
        }
        catch (Exception ex)
        {
            // 加密失败抛异常由调用方提示（评审 2.7：不得静默存空密码导致“看似保存成功、连接时原因不明”）；
            // 同时满足 FR-A10.4：宁可不保存凭据，也不明文落盘。
            throw new System.Security.Cryptography.CryptographicException("Keystore 加密失败，无法安全存储凭据", ex);
        }
    }

    public string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(CredentialCrypto.KeystorePrefix, StringComparison.Ordinal))
            return stored; // 兼容旧明文/其他前缀：交给上层按原值处理

        try
        {
            var buf = Convert.FromBase64String(stored[CredentialCrypto.KeystorePrefix.Length..]);
            var spec = new GCMParameterSpec(128, buf, 0, GcmIvLength);
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), spec);
            var plain = cipher.DoFinal(buf, GcmIvLength, buf.Length - GcmIvLength) ?? [];
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }

    private static IKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);
        if (keyStore.GetKey(KeyAlias, null) is IKey existing)
            return existing;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        var spec = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}
