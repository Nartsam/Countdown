# 倒计时点击器（Countdown）— 项目交接文档

> 本文档是完整的项目上下文快照。新的 AI Agent 只需阅读本文档即可恢复到当前开发状态并继续开发。
> 最后更新：2026-07-09。当前状态：**全部已知需求已实现并编译通过，冒烟测试通过，无待办缺陷。**

---

## 1. 项目目标

一个 Windows 下的定时键鼠模拟小工具：用户添加若干个"操作"（模拟单击某个鼠标键，或模拟按下某个键盘按键/组合键），每个操作有独立倒计时，用户点击"开始"后所有操作进入倒计时，倒计时归零时自动触发对应的键鼠事件。

### 1.1 硬性约束（用户明确要求）

| 约束 | 含义 |
|---|---|
| 轻量、绿色、便携 | 单个 exe，零安装、零依赖，删掉即卸载 |
| 无痕 | 运行时**除当前目录外**不在任何地方写文件、不写注册表、不写配置 |
| 无后台能力 | 不需要托盘图标；运行期间主窗口保持最小化在任务栏，关闭窗口即完全退出 |
| 输入模拟 | 默认不申请管理员权限；普通窗口、普通游戏/应用可直接接收 `SendInput`，若目标程序本身以管理员权限运行，则需用户手动以管理员身份启动本工具 |

### 1.2 功能需求（按用户提出的顺序汇总，全部已实现）

**第一轮（初始需求）：**
1. 鼠标操作：屏幕上显示一个可拖动的小圆点表示最终点击位置，颜色区分左右键，旁边悬浮显示倒计时。
2. 键盘操作：屏幕右上角有一个半透明悬浮列表，每行显示按键名和倒计时，整个列表可拖动。
3. 在圆点或列表项上**右键 → 删除**可移除操作。
4. 操作触发后立即从屏幕上消失。
5. 运行期间主窗口一直最小化（不退出）；无需托盘/后台。

**第二轮（修改意见）：**
1. 倒计时改为**时、分、秒三个输入框**独立设置。
2. 键盘按键支持**组合键**（Ctrl/Shift/Alt + 主键，也支持单独的修饰键）。
3. 添加操作后**不自动开始倒计时**；底部有「▶ 开始倒计时」按钮，点击后所有未开始的操作同时进入倒计时。
4. 勾选项文字为「倒计时开始后最小化本窗口」，最小化时机是**点击开始时**（不是添加时）。
5. 输入模拟：默认普通权限运行 + 使用 `SendInput` 扫描码方式；若目标程序是管理员权限窗口，用户可按需手动以管理员身份运行本工具。

**第三轮（构建与打包）：**
1. 程序默认不申请管理员权限；仅在操作管理员权限目标程序时由用户手动以管理员身份运行。
2. 构建时若当前目录存在 `icon.png`，自动将其嵌入为最终 `Countdown.exe` 的自定义图标。

---

## 2. 技术方案与关键决策

### 2.1 技术选型：C# WinForms + 系统自带编译器（决策）

- **单文件 C# 源码**（`Countdown.cs`，约 600 行），目标框架 .NET Framework 4.x（Win10/11 系统内置，无需安装运行时）。
- 用 Windows 自带的编译器编译：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，产物是单个 exe（若嵌入图标，体积会随图标资源增加）。
- **为什么不用其他方案**：AutoHotkey/Python 需要安装解释器，违反"绿色便携"；.NET Core/自包含发布体积大且需要 SDK。系统自带 csc 是唯一"用户机器上开箱即编译"的方案。
- ⚠️ **该编译器只支持 C# 5 语法**：不能用字符串插值 `$""`、`?.`、`nameof`、表达式体成员、自动属性初始化器等。事件处理统一用 `delegate(object s, EventArgs e) { }` 匿名方法风格。
- 编译参数里必须带 `/codepage:65001`（源码是 UTF-8 无 BOM，含中文字符串，不带此参数在中文系统上可能按 GBK 误读）。
- `build.bat` 会在当前目录检测 `icon.png`。若存在，则用系统自带 PowerShell + .NET/GDI+ 转成随机命名的临时 `.ico`，通过 `csc /win32icon:` 嵌入 exe 图标，编译结束后删除临时 `.ico`；若不存在，则不带图标参数编译。该流程不安装依赖、不写当前目录外文件。

### 2.2 DPI 适配（决策 + 踩坑记录）

- 程序启动时调用 `SetProcessDPIAware()`，使 WinForms 坐标 = 物理像素 = `SetCursorPos` 坐标，保证高分屏缩放下圆点圆心与实际点击位置严格一致。
- **踩过的坑**：第一版声明 DPI 感知后，字体随系统缩放（如 150%）变大，但控件坐标是写死的 96 DPI 像素值，导致"控件全挤在一起、无法点击"（用户报告的第一个 bug）。
- **修复方案**：启动时用 `Graphics.DpiX / 96f` 算出缩放系数存入 `Ui.Scale`，**所有**窗口尺寸、控件坐标、圆点直径、列表行高都必须经过 `Ui.S(int)` 缩放。**后续新增任何 UI 元素时，坐标/尺寸必须包一层 `Ui.S()`，字体用磅（pt）指定则自动缩放、不要动。**

### 2.3 输入模拟：SendInput + 扫描码 + 普通权限清单（决策）

- 第一版用 `mouse_event`/`keybd_event`（老 API），第二轮按用户要求升级为 `SendInput`：
  - **键盘**：通过 `MapVirtualKey` 把虚拟键码转成硬件扫描码，以 `KEYEVENTF_SCANCODE` 模式发送（同时也填了 `wVk`）。这样用 DirectInput 读扫描码的游戏也能收到。扩展键（方向键、Insert/Delete/Home/End/PageUp/PageDown、NumLock、小键盘除号、PrintScreen、Win 键等）要加 `KEYEVENTF_EXTENDEDKEY`，已在 `Native.IsExtended()` 中枚举。
  - **鼠标**：先 `SetCursorPos`，再发一条 `MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK` 的绝对坐标移动（坐标归一化到 0~65535，基于**虚拟桌面**度量 `SM_XVIRTUALSCREEN` 等，支持多显示器含负坐标），然后按下、抬起。三条 INPUT 一次性 `SendInput`。
- **权限策略**：`Countdown.manifest` 内嵌 `asInvoker`，编译时用 `/win32manifest:` 嵌入。`SendInput` 本身不要求管理员权限；普通桌面窗口、普通游戏/应用在同等完整性级别下可以接收输入。Windows UIPI 会静默拦截低权限进程向管理员权限窗口注入输入，因此只有操作管理员权限目标时，才需要用户手动以管理员身份启动本工具。
- **组合键触发顺序**（`MainForm.FireKey`）：修饰键依次按下 → 主键按下、抬起 → 修饰键**反序**抬起，每步之间 `Thread.Sleep(15)`，防止目标程序轮询不到瞬时状态。若捕获的只是单独修饰键（如只按 Ctrl），则只对该修饰键做一次按下抬起（`codeIsModifier` 分支）。
- **已知无解的边界**：带反作弊驱动（EAC/BattlEye 等）的游戏会在驱动层过滤 `LLMHF_INJECTED` 标记的模拟输入，任何用户态工具都绕不过。已向用户说明。

### 2.4 悬浮 UI 实现方式（决策）

- **鼠标圆点**（`MouseDot`，每个操作一个独立窗体）：无边框 + `TopMost` + `ShowInTaskbar=false` + `TransparencyKey=Magenta` 抠出不规则形状。窗体内容 = 圆点（直径 26 逻辑像素，左键蓝 `RGB(0,122,255)` 圆内写"左"，右键橙红 `RGB(255,69,58)` 圆内写"右"）+ 右侧圆角胶囊显示倒计时。**圆点圆心即最终点击坐标**（`ClickPoint` 属性）。
- **按键列表**（`KeyListForm`，全局单例，与 `MainForm.keyItems` 共享同一个 `List<KeyItem>` 引用）：无边框 + `TopMost` + `Opacity=0.86` 深色窗体，初始位置主屏工作区右上角。完全 owner-draw：标题行"按键倒计时（拖动移动）"+ 每行左侧按键名（超长省略号）、右侧倒计时。行高 26 逻辑像素，窗体高度随条目数 `SyncLayout()` 动态调整，空了就 `Hide()`。
- **拖动**：两种窗体都是手写的 MouseDown 记偏移 / MouseMove 改 `Location`（没用 `WM_NCLBUTTONDOWN` 技巧，行为一致且简单）。
- **右键删除**：两种窗体都挂 `ContextMenuStrip`（"删除此操作"）。列表窗体在 MouseDown（右键）时用 `(e.Y - HeaderH) / RowH` 算出行号存 `menuRow`，菜单 `Opening` 事件里对越界行号 `Cancel`。
- **防抢焦点**：两种悬浮窗都重写 `ShowWithoutActivation => true`。
- **触发鼠标点击时的自遮挡问题**：圆点窗口就在点击坐标上，会挡住点击。解决：`FireMouse` 先 `Hide()+Close()+Dispose()` 圆点，**然后**才发送点击。

### 2.5 倒计时状态机（第二轮重构的核心）

- 每个操作有 `TimeSpan Duration`（用户设定时长）和 `DateTime? Deadline`（**null = 待开始**，非 null = 倒计时中）。
- **待开始状态的视觉区分**：圆点胶囊显示灰底"待 0:10"；列表行倒计时为银灰色文字。开始后：胶囊变深色、列表数字变绿色 `RGB(120,220,130)`。
- 「▶ 开始倒计时」（`StartAll`）：把所有 `Deadline == null` 的操作设为 `now + Duration`；已在倒计时的不受影响；开始后新添加的操作停留在待开始状态，需再按一次开始。若一个操作都没有则弹提示。若本次至少启动了一个且勾选了最小化 → `WindowState = Minimized`。
- **驱动**：`MainForm` 上一个 100ms 的 `System.Windows.Forms.Timer`，Tick 里倒序遍历两个列表，跳过 `Deadline == null` 的项；到期的先从列表移除再触发（保证"触发后不再显示"）；未到期的刷新显示文本（圆点侧只在文本变化时才 `Invalidate`，避免无谓重绘）。
- 倒计时格式（`Fmt.Remaining`，向上取整）：`≥1h → h:mm:ss`；`≥1m → m:ss`；否则 `Ns`。

### 2.6 其他 UI/交互细节

- 主窗口：固定大小（`FixedSingle`、禁最大化）、340×236 逻辑像素、微软雅黑 UI 9pt。布局从上到下：时/分/秒三个 `NumericUpDown`（0-99 时 / 0-59 分 / 0-59 秒，默认 0:0:10）→「＋鼠标左键」「＋鼠标右键」→ 按键捕获框 +「＋添加」→ 勾选框「倒计时开始后最小化本窗口」（默认勾选）→「▶ 开始倒计时」大按钮 → 灰色提示文字。
- **按键捕获**：只读 `TextBox`，`PreviewKeyDown` 里 `e.IsInputKey = true`（否则方向键/Tab 不进 KeyDown），`KeyDown` 里记录 `e.KeyData`（含修饰键位）并 `SuppressKeyPress`。显示名由 `ComboName()` 生成（如 `Ctrl+Shift+A`），个别键名做了友好化映射（`KeyName()`：Enter、PageUp/PageDown、CapsLock、Ctrl/Shift/Alt）。
- 新圆点初始位置：主屏中心，同屏多个时按 34 逻辑像素对角线错开（`dots.Count % 8`）。
- 时长为 0、未选按键时添加会弹 `MessageBox` 提示。
- 退出：主窗口关闭时在 `OnFormClosed` 里关闭所有圆点和列表窗体（消息循环随主窗体结束，属于兜底清理）。

---

## 3. 文件清单（均在 `D:\Nartsam\Code\Program\Countdown\`）

| 文件 | 说明 |
|---|---|
| `Countdown.cs` | 全部源码（单文件，约 600 行，UTF-8） |
| `Countdown.manifest` | Win32 清单：`asInvoker`，默认普通权限运行，编译时嵌入 |
| `build.bat` | 一键编译脚本；若当前目录存在 `icon.png`，会自动嵌入为 exe 图标 |
| `icon.png` | 可选自定义图标源文件；构建时自动转换为临时 `.ico`，不会作为运行时依赖 |
| `Countdown.exe` | 编译产物（可随时用 build.bat 重新生成） |
| `PROJECT.md` | 本文档 |

**基础编译命令**（`build.bat` 在有 `icon.png` 时会额外传入 `/win32icon:<临时ico>`）：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe /optimize+ /win32manifest:Countdown.manifest /out:Countdown.exe Countdown.cs
```

## 4. 源码结构速查（`Countdown.cs` 内的类）

| 类 | 职责 | 关键成员 |
|---|---|---|
| `Native` | 全部 P/Invoke 与输入模拟 | `MouseClickAt(Point, bool left)`、`SendKey(Keys, bool up)`、`IsExtended()`、INPUT/MOUSEINPUT/KEYBDINPUT 结构体（union 用 `LayoutKind.Explicit`） |
| `Ui` | DPI 缩放 | `Scale`（Main 里初始化）、`S(int)` |
| `Fmt` | 格式化工具 | `Remaining(TimeSpan)`、`Rounded(RectangleF, float)` 圆角路径 |
| `Program` | 入口 | `SetProcessDPIAware` → 算 `Ui.Scale` → `Run(new MainForm())` |
| `MouseDot : Form` | 一个鼠标操作的悬浮圆点 | `IsLeft`、`Duration`、`Deadline`（属性，避免 CS1690 警告）、`Started`、`ClickPoint`、`Start()`、`UpdateCountdown()`、`DeleteRequested` 事件 |
| `KeyItem` | 一个键盘操作的数据 | `KeyData`（含修饰键）、`Duration`、`Deadline`、`Remaining`（显示文本）、`DisplayName` |
| `KeyListForm : Form` | 键盘操作悬浮列表（单例） | 构造时接收共享的 `List<KeyItem>`、`SyncLayout()`、`DeleteRow` 事件（传行号） |
| `MainForm : Form` | 主窗口 + 调度中心 | `dots`/`keyItems` 两个列表、100ms `timer` + `OnTick()`、`AddMouse()`/`AddKey()`/`StartAll()`、`FireMouse()`/`FireKey()`（静态） |

## 5. 开发过程中踩过的坑（新 Agent 必读）

1. **DPI 布局挤压**：声明 DPI 感知后所有硬编码像素坐标必须乘 `Ui.Scale`（见 2.2）。新增 UI 时切勿忘记 `Ui.S()`。
2. **C# 5 语法限制**：系统 csc 不认 C# 6+ 语法，写代码前先确认（见 2.1）。
3. **`Timer` 命名冲突**：`using System.Threading;` 会让 `Timer` 在 `System.Threading.Timer` 与 `System.Windows.Forms.Timer` 间歧义（CS0104）。当前解法：不 using 整个命名空间，只 `using Thread = System.Threading.Thread;`。
4. **CS1690 警告**：在 `MainForm` 里访问 `MouseDot`（Form 继承 `MarshalByRefObject`）的**可空值类型字段**的成员会告警，把 `Deadline` 从字段改成属性即消除。
5. **中文源码编码**：编译必须带 `/codepage:65001`。
6. **图标嵌入**：`csc /win32icon:` 只能接收 `.ico`，不能直接吃 PNG。当前做法是在 `build.bat` 内把 `icon.png` 缩放/居中为最大 256×256 的 PNG-compressed ICO，再传给 `csc`。临时文件名形如 `Countdown.icon.<随机>.tmp.ico`，构建后必须清理。
7. **自动化测试与 UAC**：当前清单为 `asInvoker`，正式版不会主动弹 UAC，可直接做启动冒烟测试。若将来临时改回 `requireAdministrator`，无人值守下 `Start-Process` 会弹 UAC 等待，只能验证编译或使用不带清单的临时 exe 做启动测试。
8. **触发点击前必须先隐藏圆点**，否则点击会落在圆点窗口自己身上（见 2.4 末尾）。

## 6. 当前验证状态

- ✅ 编译零错误零警告（2026-07-09）。
- ✅ `icon.png` 存在时，`build.bat` 可生成临时 `.ico` 并通过 `/win32icon:` 编译正式 exe；PE 资源表包含 `RT_GROUP_ICON`/`RT_ICON`，构建后无临时 `.ico` 残留。
- ✅ `asInvoker` 正式版冒烟测试通过（进程启动 2 秒不退出）。
- ✅ 第一轮 DPI 布局 bug 已修复（用户确认前的修复版本，之后用户未再报布局问题）。
- ⚠️ 以下路径**未经真人实测**（代码逻辑推断应正常，如有问题优先排查）：组合键在真实目标程序里的效果、多显示器负坐标点击、待开始→开始→触发的全流程视觉状态、用户手动提升后对管理员目标程序注入。

## 7. 可能的后续方向（用户尚未要求，勿主动实现）

- 操作列表的编辑（改时长/改按键）而不只是删除。
- 重复触发 / 循环模式。
- 全局热键触发"开始"（当前必须回到主窗口点按钮）。
- 暂停/取消所有倒计时的按钮。
- 若用户反馈某游戏收不到按键：可尝试按下与抬起之间加大延时，或改用 `keybd_event` 双通道兜底。

## 8. 与用户协作的注意事项

- 用户使用**简体中文**交流，UI 文案全部为中文。
- 默认普通权限运行，不主动弹 UAC。若用户要操作管理员权限目标程序，可提示其手动以管理员身份运行本工具；勿擅自改回强制 `requireAdministrator`。
- 用户在意"无痕"：不要添加任何写注册表、写 AppData、写日志的功能；临时文件也不要留在项目目录之外。
- 主窗口目前是固定大小，用户曾抱怨"没法改变窗口大小"，但根因是 DPI 布局 bug；修复后未再要求可缩放。若布局再出问题，优先查 `Ui.S()` 遗漏而不是加可缩放边框。
