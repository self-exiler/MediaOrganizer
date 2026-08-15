# MediaOrganizer

MediaOrganizer 是一个用于整理照片和视频文件的桌面工具。它会扫描指定源目录，提取文件的拍摄日期，并按“年/月/日”结构将文件复制或移动到目标目录，帮助你快速建立清晰的媒体归档结构。

该项目是原有 Python 版本的 C# 重构与精简版，重点保持功能简洁、扩展性和跨平台兼容性，并提供更稳定的桌面体验。

## 功能概览

- 递归扫描源目录中的媒体文件
- 支持图片/视频常见扩展名过滤与“扫描全部文件”模式
- 使用多级日期提取链：Exif、文件名规则、文件系统时间等
- 支持本地目录与网络目标（SMB / WebDAV）输出
- 按年/月/日分级归档，支持复制或移动
- 自定义同名文件处理：跳过、覆盖、重命名
- 分析阶段与执行阶段分离，允许预览计划并执行确认
- 提供失败文件列表与“待处理文件夹”管理能力
- 预留“魔术工具”用来生成与测试文件名正则规则
- 提供配置管理与提取器权重调节能力

## 适用场景

- 手机/相机导出的图片、视频按日期归档
- 批量整理海量照片到统一目录结构
- 在缺少原始 EXIF 的情况下，通过文件名规则补足日期识别
- 将归档结果输出到 NAS、WebDAV 或本地存储

## 技术栈

- .NET 10
- Avalonia 12
- MVVM（CommunityToolkit.Mvvm）
- C# Core Library（无 UI 依赖）
- Magick.NET（图片/EXIF 读取）
- TagLib#（视频元数据读取）
- WebDAVClient（网络输出目标支持）

## 项目结构

```text
MediaOrganizer/
├─ docs/                       # 需求文档、ADR、功能说明
├─ src/
│  ├─ MediaOrganizer.Core/     # 核心扫描、分析、提取、归档逻辑
│  └─ MediaOrganizer.Desktop/  # Avalonia 桌面应用界面
├─ test/
│  └─ MediaOrganizer.Core.Tests/ # 单元测试
├─ MediaOrganizer.slnx         # 解决方案文件
├─ .gitignore
├─ README.md
└─ ...
```

## 主要组件说明

### 1. Core

Core 项目负责核心业务逻辑，主要包含：

- 文件扫描：递归扫描、过滤扩展名、并发处理
- 日期提取：Exif、FileName、FileSystem 提取器及加权链
- 模式引擎：规则匹配、结构指纹、年份预扫描
- 分析：产出分析结果与报告
- 执行：生成归档计划，执行复制/移动并处理冲突
- 配置：保存和读取 app 配置、网络位置、提取器设置

### 2. Desktop

Desktop 项目为用户界面，采用 Avalonia + MVVM 架构，主要提供：

- 工作台：选择源目录、输出目录，执行分析与归档
- 报告查看：查看分析摘要与失败清单
- 设置页：配置提取器、模式、网络目标
- 魔术工具：生成和测试文件名规则

### 3. Tests

测试项目覆盖核心逻辑，包括：

- 规则匹配与日期解析
- 归档计划生成
- 配置管理
- 安全存储与网络配置

## 运行要求

- Windows 10 1809+（主目标平台）
- .NET 10 SDK
- 4GB 以上内存（大批量照片场景下更佳）

## 快速开始

### 1. 安装 .NET 10 SDK

请先确保本机已安装 .NET 10 SDK。

### 2. 克隆代码

```bash
git clone <repository-url>
cd MediaOrganizer
```

### 3. 恢复依赖

```bash
dotnet restore MediaOrganizer.slnx
```

### 4. 构建项目

```bash
dotnet build MediaOrganizer.slnx
```

### 5. 运行桌面应用

```bash
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

- 让媒体整理逻辑与 UI 解耦，便于后续扩展
- 优先保证日期识别的稳定性与可调试性
- 让“分析”和“执行”保持明确边界，方便用户在执行前确认计划
- 支持复杂文件命名规则场景，减少归档失败率

## 文档

更多设计与需求资料位于 `docs/` 目录：

- `docs/SRS.md`：软件需求规格说明
- `docs/glossary.md`：术语表
- `docs/adr/`：架构决策记录
- `docs/功能需求要点.md`：功能要点说明

## 说明

当前项目处于持续开发状态，功能覆盖了核心整理链路，并已具备扩展式配置与网络输出能力。后续会继续完善魔术工具、失败文件处理体验和更丰富的规则配置能力。
