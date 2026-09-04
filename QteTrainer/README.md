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
- 无限生命。
- 无限体力/精力。
- 移动速度倍率滑块（0.1 ~ 10）。
- 游戏内 ONGUI 面板，`F5` 显示/隐藏，每个功能可实时开关。
- 全部选项写入 `BepInEx/config/arena.qte.trainer.cfg`，重启后保留。

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

把仓库中的 `QteTrainer/github-actions-build.yml` 复制到
`.github/workflows/build-qte-trainer.yml` 并推送（推 workflow 需要仓库工作流权限 /
Actions 开启），完成后在 Actions 的 Artifact 里下载 `QteTrainer`。

> 当前沙箱内以 GitHub App 身份登录，缺少 `workflows` 权限，所以只能把 workflow
> 以普通文件保留在 `QteTrainer/github-actions-build.yml`，不能替你写入
> `.github/workflows/`。需要你手动放入并开启 Actions。

## 源码结构

- `QteTrainer/QteTrainer.cs`: 插件主逻辑 + Harmony 补丁 + OnGUI 面板。
- `QteTrainer/QteTrainer.csproj`: 引用 `BepInEx/interop` 与 `BepInEx/core` 里的真实 DLL。
- `QteTrainer/build-locally.ps1`: Windows 一键构建。
- `QteTrainer/github-actions-build.yml`: 备用 GitHub Actions workflow。
- `analysis_game_symbols.txt`: 由 Cpp2IL 假程序集抽取的关键符号清单。
- `tools/il_dump.py`: Python 读 IL 的小工具（`dnfile + dncil`）。

## 注意事项

- 若同时使用原 xmod 的移速倍率，两处倍率会相乘；关掉其中一个即可。
- 若游戏大版本更新导致方法名变化，需要用 `tools/il_dump.py` 重新核对
  `CompetitionForm`、`DredgeForm`、`CompetitionPlayer`、`DredgePlayer` 的方法名。
