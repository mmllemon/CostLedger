# 设计系统 · 版本 C（Claude / Anthropic 暖色极简风）

> 状态：**已定稿**（用户 2026-10-09 确认）。本文件是成本台账系统的唯一视觉源，供 Avalonia 实现与 MagicPath/设计稿对齐使用。  
> 配套可执行资产：`Styles.axaml`（Avalonia 资源字典，直接合并进 App.axaml）。  
> 设计参照：SpendWise 式极简布局 + Claude 暖色语言（奶油底 / 陶土橙点缀 / 大圆角 / 留白克制 / 零 emoji / 细线 SVG 图标）。

---

## 1. 设计原则

1. **暖，不冷**：底色与卡片走奶油暖调，点缀色用陶土橙（clay），不出现冷蓝/冷灰主色。
2. **克制留白**：大圆角（18px）、极浅暖阴影、几乎无边框，靠留白与卡片抬升区分层级。
3. **数字即主角**：金额/关键指标用超大粗体、右对齐、千分位；弱化标签（小字、低对比灰）。
4. **单色点缀**：全站只有一个彩色强调（陶土橙）+ 一个语义色（红=异常）。其余皆灰阶。
5. **零 emoji**：所有图标用 1.7px 细线 SVG（线性、单色、currentColor）。

---

## 2. 色板（Color Tokens）

### 背景与表面

| Token  | 值         | 用途          |
| ------ | --------- | ----------- |
| `Bg`   | `#F4EEE5` | 应用画布底色（暖米）  |
| `Card` | `#FFFDFA` | 卡片/表面（纯白微暖） |
| `Line` | `#ECE3D6` | 描边/分隔线（暖浅）  |

### 文字

| Token  | 值         | 用途             |
| ------ | --------- | -------------- |
| `Ink`  | `#211E1A` | 主文字 / 标题（暖近黑）  |
| `Txt2` | `#6B635A` | 次要文字（暖灰）       |
| `Txt3` | `#A89E90` | 弱化文字 / 标签（浅暖灰） |

### 主色（陶土橙 clay）

| Token        | 值         | 用途                 |
| ------------ | --------- | ------------------ |
| `Accent`     | `#CC785C` | 品牌块、头像、图标强调        |
| `AccentDeep` | `#A24E30` | 实心按钮、进度弧、本周柱、已结算标签 |
| `AccentSoft` | `#F0DFD2` | 浅底标签 / 选中态底        |
| `AccentMid`  | `#D9A98C` | 环图中段 / 次级强调        |

### 中性/轨道

| Token   | 值         | 用途    |
| ------- | --------- | ----- |
| `Track` | `#ECE2D4` | 进度条轨道 |
| `Bar`   | `#E7DAC9` | 柱状图浅柱 |

### 语义色

| Token     | 值         | 用途                                                               |
| --------- | --------- | ---------------------------------------------------------------- |
| `Red`（异常） | `#BE4A3C` | **超支 / 被驳回 / 异常**（国内造价习惯：支出/超支=红）。绿色在本风格不用于"正常"，正常即默认灰/橙，红专用于异常。 |

> 注：原 SpendWise 截图用黑色作点缀，定稿版按用户要求改为 Claude 暖橙，故 `Accent`/`AccentDeep` 取代原黑 `#111`。

---

## 3. 形状 / 阴影 / 间距 / 字体

| 类别            | Token         | 值                                               |
| ------------- | ------------- | ----------------------------------------------- |
| 卡片圆角          | `CardRadius`  | 18px                                            |
| 按钮/胶囊圆角       | `PillRadius`  | 12px                                            |
| 小方块圆角（图标/状态块） | `BlockRadius` | 10px                                            |
| 柱状圆角          | `BarRadius`   | 7px                                             |
| 卡片阴影          | `CardShadow`  | `0 1px 2px rgba(80,55,35,.05)`（暖调极浅）            |
| 基础间距          | `Gap`         | 14px（卡片间距）/ 26px（主栏间距）                          |
| 内容内边距         | `CardPad`     | 18~22px                                         |
| 正文字号          | `Body`        | 13.5px                                          |
| 标题字号          | `H1`          | 24px（页面标题）/ 44px（英雄金额）                          |
| 标签字号          | `Label`       | 11~12px，字距 .14em，大写（英文标签）                       |
| 字体            | `FontFamily`  | 中文「微软雅黑」`Microsoft YaHei`；数字建议 Segoe UI 粗体等宽右对齐 |

---

## 4. 组件规范

### 侧栏 Sidebar（208px）

- 黑色圆角方块 Logo（改为 `Accent` 陶土橙底 + 白字「诺」）+ 品牌名。
- 导航项带文字标签，默认 `Txt2`；选中项白卡浮起（`Card` 底 + `CardShadow` + `Ink` 字 + 图标加粗）；hover 浅底。
- 底部需要讨论

### 顶栏 Topbar

- 左：圆形头像（`AccentDeep` 底 + 白字首字「李」）+ 姓名/职务。
- 右： 铃铛图标按钮（同胶囊样式）。

### 卡片 Card

- 纯白微暖底、`CardRadius=18`、`CardShadow`、padding 18~22px。
- 标题 `Ct`：14.5px / 700；右上可有胶囊按钮或下拉。

### 英雄区 Hero（看板主视觉）

- 左：`Label`「支出」+ 超大金额（44px/800/字距 -1.5px）+ 副文（12.5px `Txt2`）。
- 右：陶土橙圆环进度（`AccentDeep` 弧 + `Track` 轨道），中心显示百分比。

### 迷你卡 Mini（2×1 小卡）

- 浅标签 + 22px 粗体数字。

### 按钮 Button

- **胶囊次要**（pillbtn）：`Line` 描边 + `Card` 底 + `Txt2` 字，hover 转 `Ink`。
- **实心主按钮**（primary）：`AccentDeep` 底 + 白字（如「同意」「发起付款」）。
- **危险按钮**（danger）：白底 + `Red` 字 + `Red` 描边（如「驳回」）。

### 进度条 / 柱状

- 进度条：`Track` 轨道 + `AccentDeep` 填充，圆角端点。
- 柱状图：浅柱 `Bar`，本周/当日柱 `AccentDeep`，圆角 7px。

### 标签 Badge / 状态

- 已结算 / 已支付：浅底 `AccentSoft` + `AccentDeep` 字。
- 待审批 / 待签认：浅底 + `Txt2` 字（或 `Accent` 描边）。
- **超支 / 被驳回 / 异常**：`Red` 字（必要时 `Red` 浅底 `#F6E2DD`）。

### 表格 Table

- 无竖向边框；行 hover 浅底；行内圆角进度条（预算执行）；超支行 `Red` 文字（如「超支 2.45万」）。

### 图标 Icon

- 1.7px 细线 SVG，`stroke=currentColor`，`stroke-linecap/linejoin=round`，单色。统一 18px，小号 14px。

---

## 5. 语义规则（国内造价习惯，强制）

- **红 = 超支 / 被驳回 / 付款异常 / 签证未签认逾期**。绿不在本风格承担"正常"语义（正常即默认灰/橙）。
- 金额一律人民币 ¥、千分位、万元/元按上下文。增值税、质保金、预付款抵扣等业务规则不在 UI 层硬编码，由成本脊柱回写。

---

## 6. 与 Avalonia 的实现映射

- 全部色值已在 `Styles.axaml` 定义为 `SolidColorBrush`（Key 带 `Brush` 后缀，如 `BgBrush`）。
- 圆角/阴影/间距定义为 `CornerRadius` / `BoxShadows` / `Thickness` 资源，供样式引用。
- 控件样式用 `Classes` 选择器挂载（如 `Border.card`、`Button.primary`、`Button.pill`、`ProgressBar.line`、`Border.badge.danger`），在 XAML 中给元素加 `Classes="card"` 即可。
- 图标直接用 `<PathIcon Data="{StaticResource ...}"/>` 或内联 `Path`，描边绑定 `Foreground`（currentColor 等价于 Avalonia 的 `Foreground`）。
- 字体：在 App.axaml 设 `FontFamily="Microsoft YaHei, Segoe UI, sans-serif"`；数字可用 `FontWeight="Bold"` + `TextAlignment="Right"`。

---

## 7. 验收清单（UI 实现自检）

- [ ] 画布 `#F4EEE5`、卡片 `#FFFDFA`、阴影暖调极浅。
- [ ] 全站唯一彩色强调为陶土橙（`#CC785C`/`#A24E30`），无冷蓝主色。
- [ ] 超支/异常红 `#BE4A3C`，其余非红。
- [ ] 卡片圆角 18、按钮 12、小方块 10。
- [ ] 零 emoji；图标均为 1.7px 细线 SVG。
- [ ] 金额超大粗体、千分位、右对齐。
- [ ] 侧栏/顶栏/卡片/按钮/进度条/标签均符合第 4 节规范。
