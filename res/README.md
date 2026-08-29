# 照片整理软件 · 图标套件

一套矢量 SVG 图标，适配 **Windows 桌面 / Windows 任务栏 / Android** 三大场景。
设计语言：蓝青渐变 + 照片堆叠意象 + 扁平简约风格。

---

## 文件清单

| 文件 | 用途 | 画布尺寸 | 说明 |
|------|------|----------|------|
| `icon-win-desktop.svg` | Windows 桌面图标 | 256×256 | 带圆角方形底板的完整图标，含3张照片卡片堆叠 |
| `icon-android-background.svg` | Android 自适应图标 · 背景层 | 108×108 | 蓝青渐变全填充，供 launcher 裁剪 |
| `icon-android-foreground.svg` | Android 自适应图标 · 前景层 | 108×108 | 主体落在中心 66dp 安全区内 |
| `icon-win-taskbar.svg` | Windows 任务栏图标（彩色） | 32×32 | 极简版，保留主卡片 + 山太阳符号 |
| `icon-win-taskbar-mono.svg` | Windows 任务栏图标（单色） | 32×32 | 深色描边版，适配浅色任务栏；深色任务栏可反色使用 |

---

## 设计说明

**核心意象**：三张照片卡片扇形错落叠放，顶层主卡片内含「天空渐变 + 太阳 + 山脉」风景照，底部带标题/文字行，直观传达「照片整理 / 归类」。

**配色**（蓝青色系）：
- 底板渐变：`#3B82F6` → `#0EA5E9` → `#06B6D4`
- 天空：`#7DD3FC` → `#0EA5E9`
- 山脉：`#1E40AF` → `#1E3A8A`
- 太阳：`#FCD34D`
- 卡片：`#FFFFFF`

---

## 各平台使用方式

### 1. Windows 桌面图标

SVG → ICO 转换（需要多尺寸）：

```bash
# 用 ImageMagick 将 SVG 转为多尺寸 ICO
magick icon-win-desktop.svg -define icon:auto-resize=256,128,96,64,48,32,16 app.ico
```

或在 Visual Studio 项目属性中将 `icon-win-desktop.svg` 指定为应用图标（会自动生成 `.ico`）。

### 2. Windows 任务栏

- 浅色任务栏：直接用 `icon-win-taskbar-mono.svg`（深色线条）。
- 深色任务栏：将单色版反色处理，或用 CSS `filter: invert(1)`。
- 彩色版 `icon-win-taskbar.svg` 适用于窗口标题栏 / Jumplist 等需要彩色辨识的场景。

任务栏图标建议同时导出 16/20/24/32 px 多尺寸，SVG 可无损缩放，但 16px 下建议人工微调或使用更简化的单色版。

### 3. Android 自适应图标

Android 8.0+ 的 Adaptive Icon 需要前景层 + 背景层：

**res/drawable/ic_launcher_background.xml**（或直接放 `icon-android-background.svg`）：
```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- 引用背景层 SVG -->
```

**res/mipmap-anydpi-v26/ic_launcher.xml**：
```xml
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@drawable/ic_launcher_background"/>
    <foreground android:drawable="@drawable/ic_launcher_foreground"/>
</adaptive-icon>
```

- 前景层主体已落在中心 66dp 安全区内，圆形 / 圆角矩形 / 方形裁剪均不会裁掉主体。
- 同时建议提供 `mipmap-xxxhdpi`（432×432）等位图版本作为低版本兜底，可用 SVG 渲染导出。

### 4. 批量生成 Android 资源

项目已提供脚本，直接基于本目录两个 SVG 生成所有密度 PNG：

```bash
# 先安装 sharp（一次性）
npm install sharp

# 从项目根目录运行
node res/generate-android-icons.js
```

生成目标：

| 目录 | 文件 | 尺寸（按密度） |
|------|------|----------------|
| `drawable-mdpi` … `drawable-xxxhdpi` | `ic_launcher_background.png` | 108×108 / 162×162 / 216×216 / 324×324 / 432×432 |
| `drawable-mdpi` … `drawable-xxxhdpi` | `ic_launcher_foreground.png` | 同上 |
| `mipmap-mdpi` … `mipmap-xxxhdpi` | `ic_launcher.png` | 48×48 / 72×72 / 96×96 / 144×144 / 192×192 |

---

## 自定义

- **改配色**：修改各 SVG `<defs>` 中的 `<linearGradient>` 的 `stop-color`。
- **改意象**：如需换为「网格相册 / 文件夹+照片」等，可替换顶层卡片的照片内容区图形。
- **导出 PNG**：`magick icon-win-desktop.svg -resize 512x512 app-icon.png`


[DuMate AI生成]