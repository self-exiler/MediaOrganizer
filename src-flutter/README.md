# MediaOrganizer · Flutter 版

对应 C#/Avalonia 版 `src/MediaOrganizer.Android` 的 Dart 移植：界面与
`docs/android-界面原型设计/index.html` 保持一致（四屏 + 抽屉导航 + 底部状态栏），
功能对齐 `docs/SRS-Android.md`（分析 → 计划 → 执行的整理流程、失败文件管理、TXT 报告、设置持久化）。

## 运行

本目录只包含 Dart 代码与 pubspec，**平台目录（android/ 等）由 Flutter 工具本地生成**，不入库：

```bash
cd src-flutter
flutter create --platforms=android,windows .   # 生成平台脚手架，不会覆盖 lib/
flutter pub get
flutter run                                    # 或 flutter build apk --release
```

Android 端建议在生成的 `android/app/src/main/AndroidManifest.xml` 中补充：

```xml
<uses-permission android:name="android.permission.READ_MEDIA_IMAGES"/>
<uses-permission android:name="android.permission.READ_MEDIA_VIDEO"/>
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE"
                 android:maxSdkVersion="32"/>
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE"
                 android:maxSdkVersion="29"/>
<!-- 全文件访问（APK 侧载分发不受 Play 政策约束），用于直读真实路径 -->
<uses-permission android:name="android.permission.MANAGE_EXTERNAL_STORAGE"/>
<application android:requestLegacyExternalStorage="true" ...>
```

桌面端（Windows）可直接运行，目录选取走系统对话框。

## 与 C# 版的功能对照

| 模块 | C# 位置 | Flutter 位置 | 说明 |
|------|---------|--------------|------|
| 配置/模式持久化 | `Core/Configuration` | `lib/core/app_config.dart` `patterns_store.dart` | JSON 键名保持 PascalCase，patterns.json 可与桌面版互换 |
| 文件名模式引擎 | `Core/Patterns/PatternEngine` | `lib/core/pattern_engine.dart` | 正则 + group_mapping + 时间戳(10/13/16)语义一致 |
| 加权提取链 | `Core/Extraction` | `lib/core/extractors.dart` | Exif > FileName > FileSystem，权重降序取首个通过校验者 |
| 视频容器日期 | TagLib# | `lib/core/mp4_date_reader.dart` | 自实现 MP4/MOV `mvhd` creation_time 解析；AVI/MKV 不支持 |
| 分析编排 | `Core/Analysis/Analyzer` | `lib/core/analyzer.dart` | 在独立 Isolate 中扫描+提取，进度节流上报，取消 = 终止 Isolate |
| 归档规划 | `Core/Planning` | `lib/core/archive_planner.dart` | year/month/day 分级；未来日期归入 `FutureDate/` |
| 执行 | `Core/Execution/FileOperator` | `lib/core/file_operator.dart` | `.mo-tmp` 临时名 + 大小校验 + 改名；失败重试 3 次（1/2/4s）；skip/overwrite/rename(_n)；mtime 矫正 |
| 失败文件移动 | `PendingFileMover` | `lib/core/pending_file_mover.dart` | 复制 + 删除源，同名 `_n` |
| TXT 报告 | `AnalysisReportGenerator` | `lib/core/report_generator.dart` | 输出格式逐行一致 |
| WebDAV 目标 | `WebDavFileStorage` | `lib/services/webdav_storage.dart` | dart:io HttpClient 实现 PROPFIND/MKCOL/PUT/MOVE，Basic 认证 |
| SMB 目标 | SMBLibrary | — | **未实现**：可保存配置但测试连接会明确提示不支持 |

相对 C# Android 版的已知裁剪（后续迭代候选）：

- SAF 树 URI 直读：目录选取依赖 `file_picker` 返回的真实路径；纯 SAF 提供方路径由插件降级处理。
- WakeLock 保持（可加 `wakelock_plus`）、凭据 Keystore 加密（现为 `B64:` 混淆存储，非硬件安全）。
- 魔术工具（正则圈选编辑器）：与安卓 SRS 一致，不提供。

## 代码结构

```
lib/
├── main.dart            # 组合根：加载 AppState → 构建 VM → runApp
├── core/                # 业务逻辑（无 Flutter 依赖，可单测）
├── services/            # 平台服务：目录选取 / 存储(WebDAV) / 报告导出
├── viewmodels/          # ChangeNotifier VM，逻辑对应 MediaOrganizer.Shared
└── ui/
    ├── theme.dart       # 原型 CSS 色板（accent #0067C0 等）
    ├── widgets.dart     # 卡片 / InfoBar / 步骤条 / 统计块等原型控件
    └── screens/         # home_shell + 四屏
```
