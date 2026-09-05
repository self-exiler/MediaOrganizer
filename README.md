# MediaOrganizer

MediaOrganizer 是一个用于整理照片和视频文件的桌面工具。它会扫描指定源目录，提取文件的拍摄日期，并按「年/月/日」结构将文件复制或移动到目标目录，帮助你快速建立清晰的媒体归档结构。

该项目是他人 Python 版《媒体文件整理器》的 C# 重构，重点保持功能简洁、扩展性与跨平台兼容性，并在此基础上提供网络共享（SMB / WebDAV）能力。桌面端与安卓端**同为 v1.0 正式发布目标**（见 ADR-0008），共享同一套核心逻辑。

## 平台定位

| 平台              | 状态                    | 说明                                                            |
| ----------------- | ----------------------- | --------------------------------------------------------------- |
| 桌面端（Windows） | **v1.0 发布目标** | 功能完整、可日常使用，提供自包含安装包                          |
| 安卓端            | **v1.0 发布目标** | 功能已完成并通过全面代码审查，覆盖 SMB / WebDAV、后台传输与保活 |

> 两端共用同一套 Core（整理逻辑）与 Shared（ViewModel）分层，交互逻辑一致；平台差异通过条件编译与源端抽象（ADR-0006）隔离。

## 功能概览

- 递归扫描源目录中的媒体文件
- 支持图片/视频常见扩展名过滤与「扫描全部文件」模式
- 使用多级日期提取链：Exif、文件名规则、文件系统时间等
- 支持本地目录与网络目标（SMB / WebDAV）输出
- 按年/月/日分级归档，支持复制或移动
- 自定义同名文件处理：跳过、覆盖、重命名
- 分析阶段与执行阶段分离，允许预览计划并执行确认
- 提供失败文件列表与「待处理文件夹」管理能力
- 预留「魔术工具」用来生成与测试文件名正则规则
- 提供配置管理与提取器权重调节能力

## 适用场景

- 手机/相机导出的图片、视频按日期归档
- 批量整理海量照片到统一目录结构
- 在缺少原始 EXIF 的情况下，通过文件名规则补足日期识别
- 将归档结果输出到 NAS、WebDAV 或本地存储

## 技术栈

- .NET 10
- Avalonia 12（UI 框架，桌面端与安卓端复用同一套 UI 抽象）
- MVVM（CommunityToolkit.Mvvm）
- C# Core Library（无 UI 依赖，桌面 / 安卓两端共享）
- Magick.NET（图片/EXIF 读取）
- TagLib#（视频元数据读取）
- WebDAVClient（网络输出目标支持）

## 项目结构

```text
MediaOrganizer/
├─ docs/                            # 需求文档、ADR、功能说明
├─ packaging/                       # 桌面版打包：发布脚本 + Inno Setup 安装包脚本
├─ src/
│  ├─ MediaOrganizer.Core/         # 核心扫描、分析、提取、归档逻辑（net10.0 + net10.0-android 双目标）
│  ├─ MediaOrganizer.Shared/       # 桌面 / 安卓共享的 ViewModel 层（net10.0 单目标）
│  ├─ MediaOrganizer.Desktop/      # Avalonia 桌面应用界面
│  └─ MediaOrganizer.Android/      # Avalonia 安卓应用
├─ test/
│  └─ MediaOrganizer.Core.Tests/   # 单元测试
├─ MediaOrganizer.slnx             # 解决方案文件
├─ .gitignore
├─ README.md
└─ ...
```

## 主要组件说明

### 1. Core

Core 项目负责核心业务逻辑，是桌面端和安卓端共享的基础，**不依赖任何 UI**：

- 文件扫描：递归扫描、过滤扩展名、并发处理
- 日期提取：Exif、FileName、FileSystem 提取器及加权链
- 模式引擎：规则匹配、结构指纹、年份预扫描
- 分析：产出分析结果与报告
- 执行：生成归档计划，执行复制/移动并处理冲突
- 配置：保存和读取 app 配置、网络位置、提取器设置

Core 以双目标（`net10.0` 与 `net10.0-android`）编译，使同一套逻辑可同时服务桌面与安卓，平台相关代码通过条件编译（`#if ANDROID`）隔离。

### 2. Shared

Shared 项目承载**两端共享的 ViewModel 层**（MVVM 中的 VM），与具体 UI 框架解耦，桌面端与安卓端各自通过绑定消费同一套视图模型，保证交互逻辑一致、避免重复实现。

### 3. Desktop

Desktop 项目为 Avalonia + MVVM 架构的桌面界面，主要提供：

- 工作台：选择源目录、输出目录，执行分析与归档
- 报告查看：查看分析摘要与失败清单
- 设置页：配置提取器、模式、网络目标
- 魔术工具：生成和测试文件名规则

### 4. Android

安卓端（`MediaOrganizer.Android`）与桌面端同为 v1.0 发布目标，功能已完成：

- 基于 Avalonia.Android，复用 Core 与 Shared 的完整整理链路
- 支持 SMB / WebDAV 网络输出（SMBLibrary + WebDAVClient，ADR-0007）
- 网络传输采用临时名传输 + 完成改名 + 大小校验，与桌面版一致
- 已做后台传输与进程保活优化，并通过全面代码审查修复
- 以侧载 APK 分发，非商店上架

## 运行要求

### 桌面端（最终用户）

- Windows 10 1809+ x64
- **无需安装 .NET**：安装包自包含运行时，离线可装
- 4GB 以上内存（大批量照片场景下更佳）

### 桌面端（从源码构建）

- .NET 10 SDK

### 安卓端

- Android 侧载安装，无需额外依赖
- 从源码构建需 .NET 10 工作负载 `android` 与对应 Android SDK

## 快速开始

### 桌面端（最终用户）

使用安装包 `MediaOrganizer_Setup_<版本>.exe`（构建方式见 `packaging/README.md`）：

- 中文向导，全程无需管理员权限
- 默认安装到用户目录 `%LocalAppData%\Programs\MediaOrganizer`
- 自包含 .NET 10 Runtime + ReadyToRun 预编译，目标机无需任何运行时，离线可装

### 桌面端（开发者）

```bash
git clone <repository-url>
cd MediaOrganizer
dotnet restore MediaOrganizer.slnx
dotnet build MediaOrganizer.slnx
dotnet run --project src/MediaOrganizer.Desktop/MediaOrganizer.Desktop.csproj
```

## 常用命令

```bash
# 构建全部项目
dotnet build MediaOrganizer.slnx

# 运行测试
dotnet test MediaOrganizer.slnx

# 仅运行桌面应用
dotnet run --project src/MediaOrganizer.Desktop/MediaOrganizer.Desktop.csproj

# 打包桌面版安装包（自包含 + ReadyToRun，详见 packaging/README.md）
cd packaging && ./publish.ps1 && ISCC.exe MediaOrganizer.iss

# 安卓端本地验证构建
dotnet build src/MediaOrganizer.Android/MediaOrganizer.Android.csproj -f net10.0-android
```

## 应用流程

通常使用步骤如下：

1. 选择源目录（待整理的照片/视频目录）
2. 选择输出目录（整理后的归档目录）
3. 扫描并分析文件日期信息
4. 查看失败文件列表和分析摘要
5. 选择复制或移动策略
6. 确认归档计划并执行

## 配置说明

应用会保存一份配置文件，记录：

- 源目录 / 输出目录
- 输出目标（本地或网络位置）
- 提取器启用状态与权重
- 扫描参数
- 执行参数（复制、移动、同名处理、归档粒度）
- 网络账号与加密存储信息

## 设计重点

该项目的设计目标包括：

- 让媒体整理逻辑与 UI 解耦，便于桌面 / 安卓两端复用（Core + Shared 分层）
- 优先保证桌面端的日期识别稳定性与可调试性
- 让「分析」和「执行」保持明确边界，方便用户在执行前确认计划
- 支持复杂文件命名规则场景，减少归档失败率
- 两端共用同一套分层，控制双端维护成本

## 开发路线

- **v1.0（当前）**：双端功能冻结收尾，以打磨性能、修复潜在错误为第一优先级（ADR-0008）
- **后续**：视需求重启国际化、自动更新通道、Magick.NET 体积优化等技术债

## 文档

更多设计与需求资料位于 `docs/` 目录：

- `docs/SRS.md`：软件需求规格说明（桌面端）
- `docs/SRS-Android.md`：安卓端需求规格说明
- `docs/glossary.md`：术语表
- `docs/adr/`：架构决策记录
- `docs/功能需求要点.md`：原python版功能要点说明
- `packaging/README.md`：桌面版打包说明

## 说明

核心整理链路两端均已完成，桌面端提供自包含安装包，安卓端以侧载 APK 分发；当前处于 v1.0 收尾打磨阶段。

## 致谢

原型项目：图片分类器

作者：Justin·HUgh(浅笑子)

原作者联系方式：2911237699@qq.com
