# keystore(不入库)

此目录存放安卓正式签名 keystore,内容被 `.gitignore` 排除,只存在于本地。

- `mediaorganizer-release.jks`:发布签名密钥(RSA 4096,有效期 30 年,别名 `mediaorganizer`)
- `keystore-password.txt`:keystore 与 key 密码(两者相同)

## GitHub Actions 需要的 Secrets

发布流水线(`.github/workflows/release.yml`)依赖以下仓库 Secrets,由本地一次性配置:

| Secret | 值 |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | `mediaorganizer-release.jks` 的 base64 编码 |
| `ANDROID_KEYSTORE_PASS` | `keystore-password.txt` 内容 |
| `ANDROID_KEY_ALIAS` | `mediaorganizer` |

**keystore 一旦用于发布,必须永久妥善保管**——丢失或更换将导致已发布 APK 无法覆盖升级。
