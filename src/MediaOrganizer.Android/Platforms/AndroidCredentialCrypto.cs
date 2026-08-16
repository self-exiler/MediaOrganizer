using System.Security.Cryptography;
using System.Text;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using MediaOrganizer.Core.Platforms;
using AesCipher = Javax.Crypto.Cipher;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// Android Keystore 凭据加密（FR-A10，ADR-0005 §7）：AES/GCM/NoPadding 密钥由 Android Keystore 生成，
/// 硬件-backed、不可导出、设备绑定。存储格式：KS:&lt;base64(iv|密文含tag)&gt;。
/// 遇到 DPAPI:/B64:/旧明文时按原样返回（无密钥可解），交由网络层处理。
/// </summary>
public sealed class AndroidCredentialCrypto : ICredentialCrypto
{
    private const string KeyAlias = "mediaorganizer-credential";
    private const string AndroidKeyStore = "AndroidKeyStore";
    private const string Prefix = Core.Security.CredentialCrypto.KeystorePrefix;

    public string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var cipher = AesCipher.GetInstance("AES/GCM/NoPadding");
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateKey());
        var iv = cipher.GetIV();
        var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(plain));
        var payload = new byte[iv.Length + encrypted.Length];
        Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
        Buffer.BlockCopy(encrypted, 0, payload, iv.Length, encrypted.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored; // 非 Keystore 密文：旧明文/B64/DPAPI 原样透传
        var payload = Convert.FromBase64String(stored[Prefix.Length..]);
        var iv = new byte[12];
        Buffer.BlockCopy(payload, 0, iv, 0, iv.Length);
        var encrypted = new byte[payload.Length - iv.Length];
        Buffer.BlockCopy(payload, iv.Length, encrypted, 0, encrypted.Length);
        var cipher = AesCipher.GetInstance("AES/GCM/NoPadding");
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(128, iv));
        return Encoding.UTF8.GetString(cipher.DoFinal(encrypted));
    }

    private static ISecretKey GetOrCreateKey()
    {
        var ks = KeyStore.GetInstance(AndroidKeyStore);
        ks.Load(null);
        if (ks.GetKey(KeyAlias, null) is ISecretKey key)
            return key;

        var spec = new KeyGenParameterSpec.Builder(KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build();
        var kg = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, AndroidKeyStore);
        kg.Init(spec);
        return kg.GenerateKey();
    }
}