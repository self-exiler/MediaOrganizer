/// 凭据混淆存储（对应 Core/Security/CredentialCrypto.cs）。
///
/// C# 桌面版在 Windows 用 DPAPI、非 Windows 回退 `B64:` Base64；
/// Flutter 版无 Keystore/DPAPI 等硬件级方案，统一使用 `B64:` 前缀的
/// Base64 编码。注意：这是**混淆而非加密**，与桌面版非 Windows 行为一致；
/// 接入 flutter_secure_storage（Android Keystore）是后续迭代项（SRS FR-A10.2）。
library;

import 'dart:convert';

class CredentialCrypto {
  static const prefix = 'B64:';

  static String encrypt(String plain) => '$prefix${base64Encode(utf8.encode(plain))}';

  static String decrypt(String stored) {
    if (!stored.startsWith(prefix)) return stored; // 明文兼容
    try {
      return utf8.decode(base64Decode(stored.substring(prefix.length)));
    } on FormatException {
      return '';
    }
  }
}
