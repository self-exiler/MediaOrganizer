
### 当前 Fluent 实现可改进的地方

问题不在 Fluent 本身，而在项目 **没有真正用 Fluent 的 token 体系** ，是自己拼了一套：

1. **去掉自定义 Brush，改用 Fluent 的主题资源。** 现在 `AccentBrush`、`CardBrush`、`BgBrush` 等 8 个键是手写的，换成 `SystemAccentColor`、`SolidBackgroundFillColorBase`、`CardBackgroundFillColorDefault`、`TextFillColorPrimary`、`DividerStrokeColorDefault` 等 Fluent 内置资源，深色模式基本不用额外做，`RequestedThemeVariant` 改成 `Default` 即可。
2. **硬编码色值归并。** 提示条（警告、成功、信息）抽成三个 Style 类，颜色引用 `SystemFillColorCaution/Success/Attention` 系列。这是第一节 #7 的同一件事，和主题选择无关，无论选哪个都该做。
3. **去掉手机上无意义的悬停样式。** `Button.primary:pointerover` 这类规则在触屏上基本不触发，可以删掉。
4. **导航改底部栏。** 这条不属于 MD3 专属：4 个顶层页面用底部导航比侧滑抽屉更顺手，Fluent 的视觉也能做（用 `TabControl` 或自绘 4 个按钮），去掉抽屉的 scrim 和手写显隐。
5. **文字字符图标换矢量图标。** `≡ ▶ ↻ × → ⚠` 依赖 Noto Sans SC 字体里有这些字形，换成 `PathIcon`，字体回退失败时也不会出现方块。可用 Fluent 或 Material Symbols 的路径数据，两者在 Fluent 外观下都不突兀。
6. **字号收敛。** 现在 10、11、12、13、14、18、24 都在用，收到 12 / 14 / 16 / 22 四档，10px 的 chip 提到 11 以上。
7. **触控尺寸。** 小按钮 `MinHeight=36` 提到 40–48，列表行保持 ≥48。
8. **提示反馈。** 常驻状态栏去掉，临时消息用顶部或底部 Toast 样式的浮层，进度留在卡片里。
