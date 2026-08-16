namespace MediaOrganizer.Core.Platforms;

/// <summary>
/// 凭据加密抽象（ADR-0005 §7）：桌面 DPAPI，Android Keystore AES（前缀 KS:）。
/// 实现经 CredentialCrypto 门面注入（启动时设置 Current）。
/// </summary>
public interface ICredentialCrypto
{
    string Encrypt(string plain);
    string Decrypt(string stored);
}
