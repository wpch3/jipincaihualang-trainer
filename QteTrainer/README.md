# QteTrainer — Romantic Escapades / JipinCaiHuaLang 专用修改器插件

一个基于 BepInEx 6（IL2CPP）的独立修改器插件，针对 xmod 训练器没有覆盖的两个新版 QTE
小游戏做了破解，并附带常用优化功能。它可与原 `FlowerPicker.dll`（xmod）共存。

## 已确认并破解的两个 QTE

| 小游戏 | 游戏内部类 | 难点 | 破解点 |
|---|---|---|---|
| 空格打节奏（Ring/Competition） | `Game.CompetitionForm` / `Game.CompetitionPlayer` | 需要在环缩到准星时按触发键，节奏越来越快，按错会 Miss 并掉进度 | 屏蔽 `RingMiss`, `Defeat`；`OnUpdate` 中把男女进度、能量、攻击计数直接拉满 |
| A/D 左右平衡（Dredge） | `Game.DredgeForm` / `Game.DredgePlayer` | 需要 A/D 持续微调中轴，保持击打器在范围里，超时/越界判负 | `OnUpdate` 中把击打器速度改为 0、超时条拉高、死亡计清零；进度拉满直接 Victory |

证据来自 `BepInEx/interop/Assembly-CSharp.dll`（Cpp2IL 假程序集）和
`BepInEx/plugins/xmod/FlowerPicker.dll` 的反向分析，详见仓库根目录
`analysis_game_symbols.txt` 与 `tools/il_dump.py`。

## 功能

- QTE 自动通关（空格节奏 + AD 平衡），默认开启，可关闭。
- 自动跳过对话/剧情（可选）。
- 无限生命 / 无敌（玩家不掉血，HP 恒满）。
- 无限体力/精力（取消战斗与行动中的体力消耗）。
- 战斗破解：一击破防（敌人被打时 HP 直接清零，可独立开关）。
- 移动速度倍率滑块（0.1 ~ 10）。
- 一键全物品（快捷批量添加全部物品，数量可调；原 xmod 手动添加仍可并存）。
- 一键拉满**全部** NPC 好感/星星（默认 5 星，可在 cfg 中改；不再依赖 UI 里选中了谁）。
- **一键解锁全部办事地点**，以及**带数字编号的办事地点传送菜单**（数字键盘输编号 + 前往/解锁，可分页、可只看未解锁的隐藏地点）。
- 金钱设为 99999、训练经验 +10000、时间 +8 小时。
- 无限背包/物品数量不减（物品消耗与删除不会真正扣数量，可开关）。
- 快捷传送：游戏内输入锚点/传送点 Key 后立即传送；码头/黑沼泽/祭坛预设 Key 可在 cfg 中填写。
- 配置导出/导入：将当前所有开关与传送预设导出到 `BepInEx/config/arena.qte.trainer.preset.txt`，可一键还原。
- 一键全开 / 一键全关：快速套用“最强作弊预设”或“全关预设”。
- **总开关（Master）默认关闭**：游戏一打开时所有功能都是关的，插件对游戏完全不介入。
  进入游戏后按 `F8` 打开总开关（面板同时弹出），再按 `F8` 一键全部关闭并回到未修改状态。
- 游戏内 ONGUI 面板，`F9` 显示/隐藏，每个功能可实时开关。
- 传送 Key 用面板上的**字符键盘**输入（本游戏不能用文本框，原因见下面「黑屏问题」）。
- 全部选项写入 `BepInEx/config/arena.qte.trainer.cfg`，重启后保留；但 `Master/Enabled` 与
  `UI/ShowPanel` 每次启动都会被强制重置为 false（除非把 `Master/EnableOnStart` 设为 true）。

## 安装

最终产物为一个 DLL：

```
QteTrainer.dll
```

放到游戏目录下的任意 BepInEx 插件目录即可：

```
BepInEx/plugins/QteTrainer.dll
```

推荐放在 `BepInEx/plugins/`（或 `BepInEx/plugins/xmod/`，若希望与 xmod 一起管理）。

## 方案 A：在本机编译（Windows，已装 .NET 6 SDK）

在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File QteTrainer\build-locally.ps1
```

生成：

```
QteTrainer\bin\Release\net6.0\QteTrainer.dll
```

把该 DLL 复制到游戏插件目录。

## 方案 B：用 GitHub Actions 构建（无需本机 SDK）

仓库里已经有 `.github/workflows/build-qte-trainer.yml`（`QteTrainer/github-actions-build.yml`
是同一份的副本）。只要 `QteTrainer/**` 有改动并推送，Actions 就会自动
`dotnet build QteTrainer/QteTrainer.csproj -c Release`，完成后在 Actions 的
Artifact 里下载 `QteTrainer`（即 `QteTrainer.dll`）。

## 源码结构

- `QteTrainer/QteTrainer.cs`: 插件主逻辑 + Harmony 补丁 + OnGUI 面板。
- `QteTrainer/QteTrainer.csproj`: 引用 `BepInEx/interop` 与 `BepInEx/core` 里的真实 DLL。
- `QteTrainer/build-locally.ps1`: Windows 一键构建。
- `QteTrainer/github-actions-build.yml`: 备用 GitHub Actions workflow。
- `analysis_game_symbols.txt`: 由 Cpp2IL 假程序集抽取的关键符号清单。
- `tools/il_dump.py`: Python 读 IL 的小工具（`dnfile + dncil`）。

## 黑屏问题（v1.2.0 修复）

v1.1.0 进游戏黑屏但进程不退出，`BepInEx/LogOutput.log` 里其实写明了原因，一共三处：

1. **`GUILayout.TextField` 在本游戏里根本无法使用。** 日志里每帧刷：

   ```
   System.NotSupportedException: Method unstripping failed
      at UnityEngine.TextEditor.UpdateScrollOffset()
      at UnityEngine.TextEditor.set_position(Rect value)
      at UnityEngine.GUI.DoTextField(...)
      at UnityEngine.GUILayout.TextField(...)
      at QteTrainer.QteTrainerUi.OnGUI()
   ```

   `TextEditor.set_position` / `UpdateScrollOffset` 在这个 IL2CPP 构建里被裁剪掉了，
   Il2CppInterop 无法 unstrip。异常从 `OnGUI` 中途抛出，`BeginArea` / `BeginHorizontal`
   没有配对的 `EndHorizontal` / `EndArea`，IMGUI 的 layout / GUIClip 栈每帧残留一层，
   而 IL2CPP trampoline 又不会把异常传回原生侧做清理 —— 这就是「画面黑了、进程还在跑」
   的来源。日志里这条错误出现了 256 次，全部发生在第一个场景（`Scene loaded: Start`）之后。

   → v1.2.0 彻底移除 `TextField`，传送 Key 改用纯按钮组成的字符键盘；`OnGUI` 整体包
   `try/catch`，连续异常两次就永久停止绘制，绝不再每帧弄脏 layout 栈。

2. **旧版 `UnityEngine.Input` 在本游戏会直接抛异常**，所以 `F4`/`F5` 热键一直是死的，
   并且每帧再刷一条错误：

   ```
   System.InvalidOperationException: You are trying to read Input using the
   UnityEngine.Input class, but you have switched active Input handling to
   Input System package in Player Settings.
      at UnityEngine.Input.GetKeyDown(KeyCode key)
      at QteTrainer.QteTrainerUi.Update()
   ```

   → v1.2.0 改用 `UnityEngine.InputSystem.Keyboard`（日志里 xmod 也打印了
   `Initialized new InputSystem support.`，确认游戏用的是新输入系统），并且失败会
   永久停用该后端，不再每帧刷日志。

3. **所有功能默认就是开的，而且从第一帧起就生效**，包括自动跳对话
   （`PlayableMachine.Update` → `NextText`）和 1.5 倍移速。

   → v1.2.0 增加总开关 `Master/Enabled`（默认 false，每次启动强制重置），
   所有 Harmony 补丁第一行都判 `QteTrainerPlugin.On`，总开关关闭时插件是彻底的 no-op。

另外顺手修了一个一直静默失效的补丁：`InfoCreature.SetCurtHP/SetCurtRP` 的真实参数名是
`v` 而不是 `value`，之前 HarmonyX 直接报
`Parameter "value" not found in method void Game.InfoCreature::SetCurtHP(float v)`，
所以「无限血 / 无限体力」从来没生效过。

## 「添加全物品 / 拉满好感」点了没反应（v1.3.0 修复）

这两个都是**静默失败**：不报错、不闪退，只在 `LogOutput.log` 里留一句 warning。
根因都在"怎么拿到游戏的单例"上，靠 dump `BepInEx/interop/Assembly-CSharp.dll` 确认：

### 1. 添加全部物品

```
Game.ProtoMgr : Game.Singleton`1<Game.ProtoMgr>     // Instance 声明在泛型基类上
Game.ProtoMgr+Member : .Lists / .KeyMap
```

旧代码用 `typeof(ProtoMgr).GetProperty("Instance", Static | Public | NonPublic)` 找单例。
但 `Instance` 是**继承来的静态成员**，.NET 反射找继承的静态成员必须带
`BindingFlags.FlattenHierarchy`，否则返回 `null` → `mgr == null` → 一个 Key 都取不到 →
按钮点了只打一句 `No item keys were found.`。

还有第二个独立 bug：枚举用了 `keysObj is System.Collections.IEnumerable`。
Il2CppInterop 生成的 `Dictionary<,>` / `List<>` 只实现 `Il2CppSystem.Collections.IEnumerable`，
**不**实现 `System.Collections.IEnumerable`，所以那个判断恒为 false。

对照 xmod 的做法（`FlowerPicker.dll` → `ItemPanelAdapter.SetupCoroutine` 的 IL）：

```
call     ProtoMgr.get_Instance
callvirt get_Members
ldstr    "Game.ProtoItem"
callvirt get_Item
callvirt get_KeyMap
callvirt GetEnumerator   ← 用 GetEnumerator, 不是 Keys
```

现在改成强类型 `ProtoMgr.Instance`（C# 允许通过派生类名访问基类静态成员），
枚举走新的 `EnumerateAny()`（托管集合走 `IEnumerable`，Il2Cpp 集合反射驱动
`GetEnumerator/MoveNext/Current`），并且每一步都打日志：

```
GetAllItemKeys: Members=NN 项, 找到物品 Key MMM 个。
```

### 2. 拉满 NPC 好感

```
Game.MainMenuForm : Game.UGuiForm : UnityGameFramework.Runtime.UIFormLogic
```

整条继承链上**根本没有 `Instance`**，所以旧代码的 `MainMenuForm.Instance` 反射恒为 null，
按钮只会打 `没有选中的NPC，请先在角色/好感页面选中一个NPC。`

现在改走：

```
Game.GirlMgr : Game.Singleton`1<Game.GirlMgr>
   .Girls / .DicGirls
   .SetFavorStar(key, star)          ← 直接用这个
Game.ProtoGirl.GetProtoAll()         ← 静态, 全部 NPC 表
```

先用存档里已有的 `InfoGirl`，再用 `ProtoGirl.GetProtoAll()` 补齐还没进存档的 NPC，
`GirlMgr.SetFavorStar` 失败时退回 `Commander.CmdSetNPCFavorStar`。

## 新增：办事地点解锁 + 数字编号传送菜单（v1.3.0）

已确认的类型关系：

```
Game.BuildPointMgr : Game.SingletonMono`1<BuildPointMgr>
    .BuildPoints            -> List<BuildPoint>
    .DicBuildPoints         -> Dictionary<string, BuildPoint>
    .IsBuildUnlock(k) / .GetUpgradeRank(k) / .CanUpgrade(k) / .Upgrade(k)
Game.BuildPoint : MonoBehaviour
    .Key / .Info(InfoBuild) / .Proto(ProtoBuild) / .Upgrade() / .RefreshState()
Game.InfoBuild  : .IsUnlock(只读) / .Rank(可写) / .CanBuild(可写) / .Proto
Game.ProtoBuild : .Name / .Desc / .Condition / .Cost
Game.MapAuxAnchorMgr : Game.Singleton`1<...>   .AnchorsByID
Game.Commander.PlayerTranslation(string)       // public static
Game.Entity.CurtPos                            // 可写, 用来兜底坐标传送
```

面板里新增「办事地点 / 隐藏地点」区块：

- `一键解锁全部办事地点`：先走游戏的 `BuildPointMgr.Upgrade(key)` 正常流程
  （执行前先把金钱拉到 9999999，免得因为余额不足失败），仍锁着就直接写
  `InfoBuild.CanBuild = true` / `Rank = 1` 兜底，最后 `RefreshState()` 刷新显示。
  每个地点都会打一行 `解锁 false->true, Rank 0->1`，方便核对。
- **数字编号菜单**：每条前面是绝对编号（`1.`、`2.` …），可以直接点行尾的
  `前往` / `解锁`，也可以用数字键盘输编号后按 `前往该编号` / `解锁该编号`。
- `只看未解锁` 切换（cfg 里 `Build/ShowUnlocked`），`Build/PageSize` 控制每页条数。
- 传送优先走游戏自己的锚点传送（`AnchorsByID.ContainsKey(key)` 命中时用
  `Commander.PlayerTranslation(key)`），命不中就把玩家 `CurtPos` 直接写到该点的
  Transform 坐标上。用了哪条路径都会写进日志。

## 注意事项

- 若同时使用原 xmod 的移速倍率，两处倍率会相乘；关掉其中一个即可。
- 热键名填 `UnityEngine.InputSystem.Key` 的枚举名，例如 `F8` / `F9` / `Insert` / `Home`。
  改在 cfg 的 `Master/ToggleKey`、`Master/PanelKey`。
- 万一热键在你的环境里完全不可用，把 cfg 里 `Master/EnableOnStart` 改成 `true`
  可以让功能在启动时就打开（不推荐，这是黑屏的原始触发条件）。
- 若游戏大版本更新导致方法名变化，需要用 `tools/il_dump.py` 重新核对
  `CompetitionForm`、`DredgeForm`、`CompetitionPlayer`、`DredgePlayer` 的方法名。
