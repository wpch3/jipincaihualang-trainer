using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ModFramework.Utilities.Attributes;
using UnityEngine;
using Game;

namespace QteTrainer
{
    [BepInPlugin("arena.qte.trainer", "QTE / 万能 Trainer", "1.2.0")]
    public class QteTrainerPlugin : BasePlugin
    {
        public static QteTrainerPlugin Instance { get; private set; }

        public static ManualLogSource LogSource { get; private set; }

        public static ConfigEntry<bool> Master;
        public static ConfigEntry<bool> EnableOnStart;
        public static ConfigEntry<bool> ShowHint;
        public static ConfigEntry<string> ToggleKey;
        public static ConfigEntry<string> PanelKey;
        public static ConfigEntry<bool> QteAutoWin;
        public static ConfigEntry<bool> SkipDialogue;
        public static ConfigEntry<bool> InfiniteHp;
        public static ConfigEntry<bool> InfiniteEnergy;
        public static ConfigEntry<bool> OneHitBreak;
        public static ConfigEntry<float> MoveSpeedMul;
        public static ConfigEntry<bool> ShowUi;
        public static ConfigEntry<int> AllItemCount;
        public static ConfigEntry<bool> AllItemsEnabled;
        public static ConfigEntry<bool> InfiniteInventory;
        public static ConfigEntry<int> MaxFavorStars;
        public static ConfigEntry<string> TeleportKey;
        public static ConfigEntry<string> TeleportPresetDock;
        public static ConfigEntry<string> TeleportPresetSwamp;
        public static ConfigEntry<string> TeleportPresetAltar;

        /// <summary>
        /// 总开关。只有它为 true 时, 任何 Harmony 补丁效果与面板绘制才会真正生效。
        /// 关闭时整个插件对游戏是彻底的 no-op: 不做反射, 不改数值, 不画任何 GUILayout 结构。
        /// 游戏每次启动都会被强制重置为 false (见 Load()), 除非 EnableOnStart=true。
        /// </summary>
        public static bool On => Master != null && Master.Value;

        public override void Load()
        {
            Instance = this;
            LogSource = Log;

            Master = Config.Bind("Master", "Enabled", false,
                "总开关。false = 所有功能关闭, 插件对游戏完全不介入(启动/读档阶段安全)。游戏内按 ToggleKey(默认 F8)切换。");
            EnableOnStart = Config.Bind("Master", "EnableOnStart", false,
                "启动时自动打开总开关。默认 false, 即每次进游戏都是全关状态, 需要手动按 F8 开启。" +
                "只有在热键完全不可用时才建议改成 true。");
            ShowHint = Config.Bind("Master", "ShowHint", true,
                "总开关关闭时, 在屏幕左上角显示一行按键提示(只用 GUI.Label, 不涉及任何 GUILayout 结构)。");
            ToggleKey = Config.Bind("Master", "ToggleKey", "F8",
                "总开关热键, 填 UnityEngine.InputSystem.Key 的枚举名, 例如 F8 / F9 / F10 / Insert / Home / PageUp。");
            PanelKey = Config.Bind("Master", "PanelKey", "F9",
                "面板显示/隐藏热键(仅在总开关开启后有效)。");

            QteAutoWin = Config.Bind("QTE", "AutoWin", false, "自动通过空格节奏/AD平衡两个小游戏");
            SkipDialogue = Config.Bind("Helper", "SkipDialogue", false, "自动推进对话/剧情");
            InfiniteHp = Config.Bind("Combat", "InfiniteHp", false, "玩家无限生命/无敌");
            InfiniteEnergy = Config.Bind("Combat", "InfiniteEnergy", false, "玩家无限体力/精力(取消体力消耗)");
            OneHitBreak = Config.Bind("Combat", "OneHitBreak", false, "一击破防: 非玩家目标被打时 HP 直接清 0");
            MoveSpeedMul = Config.Bind("Helper", "MoveSpeedMul", 1.0f, "移动速度倍率(1.00 = 不改速度)");
            ShowUi = Config.Bind("UI", "ShowPanel", false, "显示训练器面板");
            AllItemsEnabled = Config.Bind("Items", "GiveAllEnabled", false, "允许一键添加全部物品");
            AllItemCount = Config.Bind("Items", "GiveAllCount", 1, "一键添加全部物品时的每种物品数量");
            InfiniteInventory = Config.Bind("Items", "InfiniteInventory", false, "物品数量不减(资源消耗不扣除; 建议进入正常场景后再开启)");
            MaxFavorStars = Config.Bind("NPC", "MaxFavorStars", 5, "一键拉满NPC时设置为多少星");
            TeleportKey = Config.Bind("Teleport", "Key", "", "快捷传送的锚点/传送点 Key(游戏内用面板上的字符键盘输入, 也可直接写在这里)");
            TeleportPresetDock = Config.Bind("Teleport", "PresetDock", "", "码头预设传送 Key(留空则按钮提示未设置)");
            TeleportPresetSwamp = Config.Bind("Teleport", "PresetSwamp", "", "黑沼泽预设传送 Key(留空则按钮提示未设置)");
            TeleportPresetAltar = Config.Bind("Teleport", "PresetAltar", "", "祭坛/祭神台预设传送 Key(留空则按钮提示未设置)");

            // 关键: 每次启动都把总开关重置为关闭。
            // 上一次会话保存下来的 Enabled=true / ShowPanel=true 不会带进这次启动,
            // 保证"游戏刚打开时所有功能都是关闭的, 进入游戏后再手动按键开启"。
            if (!EnableOnStart.Value)
            {
                Master.Value = false;
                ShowUi.Value = false;
            }

            // Always create the component first so the in-game panel works even
            // if one optional Harmony patch fails to install.
            // 组件本身在启动时就存在, 但它的 Update/OnGUI 在总开关关闭时是完全的 no-op。
            try { AddComponent<QteTrainerUi>(); } catch (Exception ex) { LogSource.LogError(ex); }

            // Patch each class independently so a single bad patch cannot kill the plugin.
            var harmony = new Harmony("arena.qte.trainer");
            PatchAll(harmony, typeof(CompetitionForm_OnUpdate_Patch));
            PatchAll(harmony, typeof(CompetitionForm_RingMiss_Patch));
            PatchAll(harmony, typeof(CompetitionPlayer_Defeat_Patch));
            PatchAll(harmony, typeof(DredgeForm_OnUpdate_Patch));
            PatchAll(harmony, typeof(DredgePlayer_Defeat_Patch));
            PatchAll(harmony, typeof(InfoCreature_SetCurtHP_Patch));
            PatchAll(harmony, typeof(InfoCreature_SetCurtRP_Patch));
            PatchAll(harmony, typeof(Creature_TakeDamage_Patch));
            PatchAll(harmony, typeof(Creature_CurtMoveSpeed_Patch));
            PatchAll(harmony, typeof(PlayableMachine_Update_Patch));
            PatchAll(harmony, typeof(Package_RemoveItem_Patch));
            PatchAll(harmony, typeof(Package_CutItem_Patch));
            PatchAll(harmony, typeof(Commander_CmdRemoveItem_Patch));

            LogSource.LogInfo("QTE Trainer loaded.");
            LogSource.LogInfo(
                $"总开关 = {(On ? "开启" : "关闭(全部功能停用)")} | " +
                $"开关热键 = {ToggleKey.Value} | 面板热键 = {PanelKey.Value} | " +
                $"EnableOnStart = {EnableOnStart.Value}");
            LogSource.LogInfo(
                "提示: 本游戏 Active Input Handling = Input System Package(New), " +
                "所以热键走 UnityEngine.InputSystem.Keyboard; 旧版 UnityEngine.Input 在本游戏会抛异常, 已不再使用。");
        }

        private static void PatchAll(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.PatchAll(patchType);
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"Patch {patchType.Name} failed, continuing: {ex.Message}");
            }
        }
    }

    public static class TrainerActions
    {
        private static InfoCreature _cachedLocalInfo;
        private static Creature _cachedLocalCrt;
        private static float _nextLocalRefresh = -1f;

        private static object ReflectGet(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(obj);
        }

        private static object ReflectIndex(object obj, object key)
        {
            if (obj == null) return null;
            var p = obj.GetType().GetProperty("Item");
            return p?.GetValue(obj, new object[] { key });
        }

        private static object GetSingleton(Type type)
        {
            string[] candidates = { "Instance", "s_instance", "SInstance", "Main" };
            foreach (var name in candidates)
            {
                try
                {
                    var p = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                    {
                        var v = p.GetValue(null);
                        if (v != null) return v;
                    }
                }
                catch { }
                try
                {
                    var f = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        var v = f.GetValue(null);
                        if (v != null) return v;
                    }
                }
                catch { }
            }
            return null;
        }

        private static Commander GetCommander()
        {
            try { return Commander.Instance; }
            catch { return null; }
        }

        private static object ReflectGetAny(object obj, Type declaring, string name)
        {
            object v = ReflectGet(obj, name);
            if (v != null) return v;
            try
            {
                var p = declaring.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return p?.GetValue(null);
            }
            catch { return null; }
        }

        private static InfoCreature GetLocalInfo()
        {
            try
            {
                object pm = GetSingleton(typeof(PlayerMgr));
                object lp = ReflectGetAny(pm, typeof(PlayerMgr), "LocalPlayer");
                return ReflectGetAny(lp, typeof(Player), "Info") as InfoCreature;
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"GetLocalInfo: {ex.Message}");
                return null;
            }
        }

        private static Creature GetLocalCrt()
        {
            try
            {
                object pm = GetSingleton(typeof(PlayerMgr));
                return ReflectGetAny(pm, typeof(PlayerMgr), "LocalCrt") as Creature;
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"GetLocalCrt: {ex.Message}");
                return null;
            }
        }

        // 战斗补丁每帧会被高频调用, 必须缓存本地玩家引用, 避免每帧反射导致卡死/黑屏。
        private static void RefreshLocalReferences()
        {
            if (UnityEngine.Time.time < _nextLocalRefresh)
                return;
            _nextLocalRefresh = UnityEngine.Time.time + 0.5f;
            try
            {
                _cachedLocalInfo = GetLocalInfo();
                _cachedLocalCrt = GetLocalCrt();
            }
            catch { }
        }

        public static bool IsLocalInfo(InfoCreature info)
        {
            if (info == null) return false;
            try
            {
                RefreshLocalReferences();
                return _cachedLocalInfo != null && ReferenceEquals(_cachedLocalInfo, info);
            }
            catch { return false; }
        }

        public static bool IsLocalCrt(Creature crt)
        {
            if (crt == null) return false;
            try
            {
                RefreshLocalReferences();
                return _cachedLocalCrt != null && ReferenceEquals(_cachedLocalCrt, crt);
            }
            catch { return false; }
        }

        public static bool HasLocalContext()
        {
            try
            {
                RefreshLocalReferences();
                return _cachedLocalInfo != null || _cachedLocalCrt != null;
            }
            catch { return false; }
        }

        private static List<string> GetAllItemKeys()
        {
            var result = new List<string>();
            try
            {
                var mgrType = typeof(ProtoMgr);
                var instProp = mgrType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (instProp == null)
                {
                    // Some Il2Cpp singleton implementations expose it through the generic base type as well.
                    instProp = mgrType.BaseType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                object mgr = instProp?.GetValue(null);
                if (mgr == null) return result;

                object members = ReflectGet(mgr, "Members");
                if (members == null) return result;

                // Find the entry whose key's ToString ends with ProtoItem.
                object membersKeys = ReflectGet(members, "Keys");
                object itemMember = null;
                if (membersKeys is IEnumerable enumKeys)
                {
                    var e = enumKeys.GetEnumerator();
                    while (e.MoveNext())
                    {
                        object k = e.Current;
                        string name = k?.ToString() ?? string.Empty;
                        if (name.IndexOf("ProtoItem", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            itemMember = ReflectIndex(members, k);
                            break;
                        }
                    }
                }
                if (itemMember == null) return result;

                object keyMap = ReflectGet(itemMember, "KeyMap");
                if (keyMap == null) return result;

                object keysObj = ReflectGet(keyMap, "Keys");
                if (keysObj is IEnumerable keyEnum)
                {
                    var e = keyEnum.GetEnumerator();
                    while (e.MoveNext())
                    {
                        string key = e.Current?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(key))
                            result.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"GetAllItemKeys: {ex.Message}");
            }
            return result;
        }

        public static int AddAllItems(int countPerItem)
        {
            if (countPerItem < 1) countPerItem = 1;
            var keys = GetAllItemKeys();
            if (keys.Count == 0)
            {
                QteTrainerPlugin.LogSource?.LogWarning("No item keys were found.");
                return 0;
            }
            int added = 0;
            var commander = GetCommander();
            if (commander == null)
            {
                QteTrainerPlugin.LogSource?.LogWarning("Commander.Instance is null.");
                return 0;
            }
            foreach (var key in keys)
            {
                try
                {
                    commander.CmdAddItem(key, countPerItem);
                    added++;
                }
                catch (Exception ex)
                {
                    QteTrainerPlugin.LogSource?.LogWarning($"Add item {key} failed: {ex.Message}");
                }
            }
            return added;
        }

        public static void SetGold(int amount)
        {
            try
            {
                var pkg = Package.Instance;
                if (pkg == null || pkg.m_Gold == null) return;
                pkg.m_Gold.Count = amount;
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"SetGold: {ex.Message}");
            }
        }

        public static void AddExp(int amount)
        {
            try
            {
                var cur = RecordData.Current;
                if (cur != null) cur.IncreTrainEXP(amount);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"AddExp: {ex.Message}");
            }
        }

        public static void JumpTime(float hours)
        {
            try
            {
                var commander = GetCommander();
                if (commander != null) commander.CmdJumpTime(hours);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"JumpTime: {ex.Message}");
            }
        }

        private static string GetSelectedGirlKey()
        {
            try
            {
                var mfType = typeof(MainMenuForm);
                var instanceProp = mfType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object mainMenu = instanceProp?.GetValue(null);
                if (mainMenu == null) return null;
                object page = ReflectGet(mainMenu, "CharInfoPage");
                if (page == null) return null;
                object current = ReflectGet(page, "m_Current");
                if (current == null) return null;
                var keyProp = current.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return keyProp?.GetValue(current)?.ToString();
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"GetSelectedGirlKey: {ex.Message}");
                return null;
            }
        }

        public static void MaxSelectedNpc()
        {
            try
            {
                var key = GetSelectedGirlKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    QteTrainerPlugin.LogSource?.LogWarning("没有选中的NPC，请先在角色/好感页面选中一个NPC。");
                    return;
                }
                var commander = GetCommander();
                if (commander == null) return;
                int stars = QteTrainerPlugin.MaxFavorStars.Value;
                if (stars < 0) stars = 0;
                if (stars > 5) stars = 5;
                commander.CmdSetNPCFavorStar(key, stars);
                QteTrainerPlugin.LogSource?.LogInfo($"Set {key} favor star to {stars}.");
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"MaxSelectedNpc: {ex.Message}");
            }
        }

        public static void Teleport(string key)
        {
            key = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                QteTrainerPlugin.LogSource?.LogWarning("请先填写传送 Key(锚点/传送点 Key)。");
                return;
            }
            try
            {
                var commander = GetCommander();
                if (commander == null)
                {
                    QteTrainerPlugin.LogSource?.LogWarning("Commander.Instance is null, 无法传送。");
                    return;
                }
                commander.CmdPlayerTranslation(key);
                QteTrainerPlugin.LogSource?.LogInfo($"Teleport requested: {key}");
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"Teleport({key}): {ex.Message}");
            }
        }

        public static string PresetPath
        {
            get
            {
                try { return Path.Combine(Paths.ConfigPath, "arena.qte.trainer.preset.txt"); }
                catch { return "arena.qte.trainer.preset.txt"; }
            }
        }

        public static void ExportPreset()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("QteTrainerPreset 1.0");
                sb.AppendLine("QteAutoWin=" + QteTrainerPlugin.QteAutoWin.Value);
                sb.AppendLine("SkipDialogue=" + QteTrainerPlugin.SkipDialogue.Value);
                sb.AppendLine("InfiniteHp=" + QteTrainerPlugin.InfiniteHp.Value);
                sb.AppendLine("InfiniteEnergy=" + QteTrainerPlugin.InfiniteEnergy.Value);
                sb.AppendLine("OneHitBreak=" + QteTrainerPlugin.OneHitBreak.Value);
                sb.AppendLine("MoveSpeedMul=" + QteTrainerPlugin.MoveSpeedMul.Value.ToString("0.####"));
                sb.AppendLine("AllItemsEnabled=" + QteTrainerPlugin.AllItemsEnabled.Value);
                sb.AppendLine("AllItemCount=" + QteTrainerPlugin.AllItemCount.Value);
                sb.AppendLine("InfiniteInventory=" + QteTrainerPlugin.InfiniteInventory.Value);
                sb.AppendLine("MaxFavorStars=" + QteTrainerPlugin.MaxFavorStars.Value);
                sb.AppendLine("TeleportKey=" + QteTrainerPlugin.TeleportKey.Value);
                sb.AppendLine("TeleportPresetDock=" + QteTrainerPlugin.TeleportPresetDock.Value);
                sb.AppendLine("TeleportPresetSwamp=" + QteTrainerPlugin.TeleportPresetSwamp.Value);
                sb.AppendLine("TeleportPresetAltar=" + QteTrainerPlugin.TeleportPresetAltar.Value);
                File.WriteAllText(PresetPath, sb.ToString());
                QteTrainerPlugin.LogSource?.LogInfo($"配置已导出: {PresetPath}");
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"ExportPreset: {ex.Message}");
            }
        }

        public static void ImportPreset()
        {
            try
            {
                if (!File.Exists(PresetPath))
                {
                    QteTrainerPlugin.LogSource?.LogWarning($"未找到配置文件: {PresetPath}");
                    return;
                }
                foreach (var raw in File.ReadAllLines(PresetPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("QteTrainerPreset"))
                        continue;
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();

                    void SetB(string target, ConfigEntry<bool> e) { if (key == target && bool.TryParse(value, out var v)) e.Value = v; }
                    void SetI(string target, ConfigEntry<int> e) { if (key == target && int.TryParse(value, out var v)) e.Value = v; }
                    void SetF(string target, ConfigEntry<float> e) { if (key == target && float.TryParse(value, out var v)) e.Value = v; }
                    void SetS(string target, ConfigEntry<string> e) { if (key == target) e.Value = value; }

                    SetB("QteAutoWin", QteTrainerPlugin.QteAutoWin);
                    SetB("SkipDialogue", QteTrainerPlugin.SkipDialogue);
                    SetB("InfiniteHp", QteTrainerPlugin.InfiniteHp);
                    SetB("InfiniteEnergy", QteTrainerPlugin.InfiniteEnergy);
                    SetB("OneHitBreak", QteTrainerPlugin.OneHitBreak);
                    SetF("MoveSpeedMul", QteTrainerPlugin.MoveSpeedMul);
                    SetB("AllItemsEnabled", QteTrainerPlugin.AllItemsEnabled);
                    SetI("AllItemCount", QteTrainerPlugin.AllItemCount);
                    SetB("InfiniteInventory", QteTrainerPlugin.InfiniteInventory);
                    SetI("MaxFavorStars", QteTrainerPlugin.MaxFavorStars);
                    SetS("TeleportKey", QteTrainerPlugin.TeleportKey);
                    SetS("TeleportPresetDock", QteTrainerPlugin.TeleportPresetDock);
                    SetS("TeleportPresetSwamp", QteTrainerPlugin.TeleportPresetSwamp);
                    SetS("TeleportPresetAltar", QteTrainerPlugin.TeleportPresetAltar);
                }
                QteTrainerPlugin.LogSource?.LogInfo($"配置已导入: {PresetPath}");
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"ImportPreset: {ex.Message}");
            }
        }

        public static void SetAll(bool enabled)
        {
            QteTrainerPlugin.QteAutoWin.Value = enabled;
            QteTrainerPlugin.SkipDialogue.Value = enabled;
            QteTrainerPlugin.InfiniteHp.Value = enabled;
            QteTrainerPlugin.InfiniteEnergy.Value = enabled;
            QteTrainerPlugin.InfiniteInventory.Value = enabled;
            QteTrainerPlugin.OneHitBreak.Value = enabled;
        }

        /// <summary>
        /// 总开关。关闭时所有 Harmony 补丁立即回到"原方法照常执行"的状态:
        /// 移速倍率不再乘算、对话不再自动跳过、HP/RP 不再改写。
        /// 同时隐藏面板, 所以关掉之后游戏画面上不会残留任何插件 UI。
        /// </summary>
        public static void ToggleMaster()
        {
            bool next = !QteTrainerPlugin.On;
            QteTrainerPlugin.Master.Value = next;
            QteTrainerPlugin.ShowUi.Value = next;
            if (next)
            {
                QteTrainerPlugin.LogSource?.LogInfo(
                    $"Trainer 总开关: 开启。面板已显示, 按 {QteTrainerPlugin.PanelKey.Value} 可隐藏。");
            }
            else
            {
                QteTrainerPlugin.LogSource?.LogInfo(
                    "Trainer 总开关: 关闭。所有功能已停用, 游戏回到未修改状态。");
            }
        }

        public static void TogglePanel()
        {
            QteTrainerPlugin.ShowUi.Value = !QteTrainerPlugin.ShowUi.Value;
        }
    }

    /* --------------------------------------------------------------------
     * 热键
     *
     * 游戏 Player Settings 的 Active Input Handling 只有 Input System Package(New),
     * 所以 UnityEngine.Input.GetKeyDown 每次调用都会抛
     *   System.InvalidOperationException: You are trying to read Input using the
     *   UnityEngine.Input class, but you have switched active Input handling to
     *   Input System package in Player Settings.
     * (见 BepInEx/LogOutput.log)。这里只走 UnityEngine.InputSystem.Keyboard,
     * 并且:
     *   1) 整段包在 try/catch 里, 异常永远不会逃出 MonoBehaviour.Update 的 IL2CPP trampoline;
     *   2) 某个后端失败就把它永久标记为不可用, 不再每帧重试刷日志;
     *   3) 只有 Input System 确实不可用时, 才去探测一次旧版 Input(本游戏不会走到)。
     * -------------------------------------------------------------------- */
    public static class Hotkeys
    {
        private const int MaxFailures = 3;

        private static bool _inputSystemBroken;
        private static bool _legacyProbed;
        private static bool _legacyWorks;
        private static bool _reported;
        private static int _failures;

        public static void Poll()
        {
            if (_failures >= MaxFailures)
                return;

            try
            {
                if (PollInputSystem())
                    return;

                // 仅当 Input System 明确不可用时, 才退回旧版 Input(只探测一次)。
                if (_inputSystemBroken)
                    PollLegacy();
            }
            catch (Exception ex)
            {
                _failures++;
                if (_failures >= MaxFailures)
                {
                    _inputSystemBroken = true;
                    _legacyWorks = false;
                    QteTrainerPlugin.LogSource?.LogError(
                        $"热键轮询连续失败 {_failures} 次, 已彻底停用热键(面板不会再有异常)。" +
                        $"如需仍要使用, 请把 cfg 里 Master/EnableOnStart 设为 true。最后错误: {ex.GetType().Name}: {ex.Message}");
                }
                else
                {
                    QteTrainerPlugin.LogSource?.LogWarning(
                        $"Hotkeys.Poll 失败 ({_failures}/{MaxFailures}): {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static bool PollInputSystem()
        {
            if (_inputSystemBroken)
                return false;

            UnityEngine.InputSystem.Keyboard kb;
            try
            {
                kb = UnityEngine.InputSystem.Keyboard.current;
            }
            catch (Exception ex)
            {
                _inputSystemBroken = true;
                QteTrainerPlugin.LogSource?.LogWarning(
                    $"UnityEngine.InputSystem.Keyboard.current 不可用: {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            // 设备还没注册(例如刚进主菜单), 下一帧再试, 不算失败。
            if (kb == null)
                return false;

            if (!_reported)
            {
                _reported = true;
                QteTrainerPlugin.LogSource?.LogInfo(
                    $"热键后端: Input System (UnityEngine.InputSystem.Keyboard)。" +
                    $"总开关 = {QteTrainerPlugin.ToggleKey.Value}, 面板 = {QteTrainerPlugin.PanelKey.Value}");
            }

            if (Pressed(kb, ToggleBinding, QteTrainerPlugin.ToggleKey.Value))
            {
                TrainerActions.ToggleMaster();
                return true;
            }

            if (QteTrainerPlugin.On && Pressed(kb, PanelBinding, QteTrainerPlugin.PanelKey.Value))
            {
                TrainerActions.TogglePanel();
                return true;
            }

            return true;
        }

        private static readonly KeyBinding ToggleBinding = new KeyBinding();
        private static readonly KeyBinding PanelBinding = new KeyBinding();

        private static bool Pressed(UnityEngine.InputSystem.Keyboard kb, KeyBinding binding, string keyName)
        {
            if (kb == null || !binding.TryGet(keyName, out var key))
                return false;

            // Key.None(0) 不能拿去索引 Keyboard, 会抛 ArgumentOutOfRangeException。
            if (key == UnityEngine.InputSystem.Key.None)
                return false;

            var ctl = kb[key];
            return ctl != null && ctl.wasPressedThisFrame;
        }

        /// <summary>
        /// 把 cfg 里的热键名解析成 UnityEngine.InputSystem.Key, 并缓存结果。
        /// 解析只在配置字符串变化时做一次, 避免每帧反射(这个插件之前就是被
        /// "每帧反射" 拖垮的, 不要再犯)。
        /// </summary>
        private sealed class KeyBinding
        {
            private string _name;
            private UnityEngine.InputSystem.Key _key;
            private bool _ok;

            public bool TryGet(string configured, out UnityEngine.InputSystem.Key key)
            {
                key = default;
                if (_name != configured)
                {
                    _name = configured;
                    _key = default;
                    _ok = !string.IsNullOrWhiteSpace(configured)
                          && Enum.TryParse(configured.Trim(), true, out _key)
                          && Enum.IsDefined(typeof(UnityEngine.InputSystem.Key), _key);
                    if (!_ok)
                    {
                        QteTrainerPlugin.LogSource?.LogWarning(
                            $"热键名 \"{configured}\" 不是有效的 UnityEngine.InputSystem.Key 枚举值, 该热键已忽略。" +
                            $"可用示例: F1..F12, Insert, Home, End, PageUp, PageDown, Backquote。");
                    }
                }
                if (!_ok)
                    return false;
                key = _key;
                return true;
            }
        }

        private static void PollLegacy()
        {
            if (!_legacyProbed)
            {
                _legacyProbed = true;
                try
                {
                    // 本游戏会在这里抛 InvalidOperationException; 抛了就永久放弃旧版 Input。
                    bool any = UnityEngine.Input.anyKey;
                    _legacyWorks = true;
                    QteTrainerPlugin.LogSource?.LogInfo(
                        $"热键后端: 旧版 UnityEngine.Input (anyKey={any})。");
                }
                catch (Exception ex)
                {
                    _legacyWorks = false;
                    QteTrainerPlugin.LogSource?.LogWarning(
                        $"旧版 UnityEngine.Input 也不可用: {ex.GetType().Name}: {ex.Message}。热键已停用, " +
                        $"如需强制开启功能请设置 cfg 里 Master/EnableOnStart=true。");
                    return;
                }
            }

            if (!_legacyWorks)
                return;

            try
            {
                if (Enum.TryParse((QteTrainerPlugin.ToggleKey.Value ?? string.Empty).Trim(), true, out KeyCode toggle)
                    && UnityEngine.Input.GetKeyDown(toggle))
                {
                    TrainerActions.ToggleMaster();
                    return;
                }
                if (QteTrainerPlugin.On
                    && Enum.TryParse((QteTrainerPlugin.PanelKey.Value ?? string.Empty).Trim(), true, out KeyCode panel)
                    && UnityEngine.Input.GetKeyDown(panel))
                {
                    TrainerActions.TogglePanel();
                }
            }
            catch (Exception ex)
            {
                _legacyWorks = false;
                QteTrainerPlugin.LogSource?.LogWarning(
                    $"旧版 UnityEngine.Input 轮询失败, 已停用: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [RegisterInIl2Cpp]
    public class QteTrainerUi : MonoBehaviour
    {
        // 面板里不再使用 GUILayout.TextField: 本游戏 IL2CPP 里
        // UnityEngine.TextEditor.set_position / UpdateScrollOffset 被裁剪掉了,
        // Il2CppInterop 无法 unstrip, 一调用就抛
        //   System.NotSupportedException: Method unstripping failed
        // 而且这个异常会从 OnGUI 中途抛出, BeginArea/BeginHorizontal 没有配对的
        // EndHorizontal/EndArea, IMGUI 的 layout/GUIClip 栈每帧都残留一层 —— 这是
        // "进程还在跑、没闪退、但画面黑掉" 最直接的来源(见 BepInEx/LogOutput.log)。
        // 所以传送 Key 改用纯按钮组成的字符键盘输入。
        private const string KeypadRow1 = "0123456789";
        private const string KeypadRow2 = "ABCDEFGHIJKLM";
        private const string KeypadRow3 = "NOPQRSTUVWXYZ";
        private const string KeypadRow4 = "_-.:/";

        private const int MaxGuiFailures = 2;
        private const int MaxUpdateFailures = 3;

        private string teleportInput = string.Empty;
        private bool guiDisabled;
        private int guiFailures;
        private int updateFailures;

        private void Update()
        {
            // 总开关关闭时也要能按键开启, 所以 Update 永远只做一件事: 轮询热键。
            // 热键内部已经自带 try/catch, 这里再兜一层, 确保任何异常都不会
            // 逃出 IL2CPP trampoline(逃出去就是每帧刷一条错误日志 + 原生侧状态错乱)。
            if (updateFailures >= MaxUpdateFailures)
                return;

            try
            {
                Hotkeys.Poll();
            }
            catch (Exception ex)
            {
                updateFailures++;
                QteTrainerPlugin.LogSource?.LogWarning(
                    $"QteTrainerUi.Update 异常 ({updateFailures}/{MaxUpdateFailures}): {ex.GetType().Name}: {ex.Message}");
                if (updateFailures >= MaxUpdateFailures)
                    QteTrainerPlugin.LogSource?.LogError("QteTrainerUi.Update 已停用(功能仍可用, 只是热键不再轮询)。");
            }
        }

        private void EnsureTeleportInput()
        {
            if (string.IsNullOrEmpty(teleportInput) && !string.IsNullOrEmpty(QteTrainerPlugin.TeleportKey.Value))
                teleportInput = QteTrainerPlugin.TeleportKey.Value;
        }

        private void OnGUI()
        {
            // 一旦 IMGUI 抛过异常就永久不再画, 避免每帧把 layout 栈弄脏。
            if (guiDisabled)
                return;

            try
            {
                if (!QteTrainerPlugin.On)
                {
                    // 总开关关闭: 不建任何 GUILayout 结构, 只用一条自包含的 GUI.Label 提示按键。
                    if (QteTrainerPlugin.ShowHint.Value)
                        GUI.Label(new Rect(8, 8, 460, 22),
                            $"QTE Trainer 已停用 —— 按 {QteTrainerPlugin.ToggleKey.Value} 开启全部功能",
                            GUI.skin.label);
                    return;
                }

                if (!QteTrainerPlugin.ShowUi.Value)
                    return;

                DrawPanel();
            }
            catch (Exception ex)
            {
                guiFailures++;
                QteTrainerPlugin.LogSource?.LogWarning(
                    $"QteTrainerUi.OnGUI 异常 ({guiFailures}/{MaxGuiFailures}), 已按原样中止本帧绘制: {ex.GetType().Name}: {ex.Message}");
                if (guiFailures >= MaxGuiFailures)
                {
                    guiDisabled = true;
                    QteTrainerPlugin.LogSource?.LogError(
                        "OnGUI 连续异常, 面板已永久关闭(功能仍然可用, 只是不再绘制 UI)。" +
                        "请把这条日志发给开发者。");
                }
            }
        }

        private void DrawPanel()
        {
            EnsureTeleportInput();

            GUILayout.BeginArea(new Rect(8, 8, 440, 600));
            GUILayout.Label("<b>QTE / 万能 Trainer</b>");
            GUILayout.Label($"总开关: 开  ({QteTrainerPlugin.ToggleKey.Value} 关闭全部 / {QteTrainerPlugin.PanelKey.Value} 隐藏面板)");

            QteTrainerPlugin.QteAutoWin.Value = GUILayout.Toggle(QteTrainerPlugin.QteAutoWin.Value, "QTE 自动通关（空格节奏 + AD 平衡）");
            QteTrainerPlugin.InfiniteHp.Value = GUILayout.Toggle(QteTrainerPlugin.InfiniteHp.Value, "无限生命 / 无敌");
            QteTrainerPlugin.InfiniteEnergy.Value = GUILayout.Toggle(QteTrainerPlugin.InfiniteEnergy.Value, "无限体力/精力（取消消耗）");
            QteTrainerPlugin.OneHitBreak.Value = GUILayout.Toggle(QteTrainerPlugin.OneHitBreak.Value, "战斗破解：一击破防（敌人 HP 清零）");
            QteTrainerPlugin.SkipDialogue.Value = GUILayout.Toggle(QteTrainerPlugin.SkipDialogue.Value, "自动跳过对话/剧情");
            QteTrainerPlugin.InfiniteInventory.Value = GUILayout.Toggle(QteTrainerPlugin.InfiniteInventory.Value, "物品数量不减");

            GUILayout.Label("移动速度倍率: " + QteTrainerPlugin.MoveSpeedMul.Value.ToString("F2"));
            QteTrainerPlugin.MoveSpeedMul.Value = GUILayout.HorizontalSlider(QteTrainerPlugin.MoveSpeedMul.Value, 0.1f, 10f);

            GUILayout.Space(6);
            GUILayout.Label("一键全物品 - 数量: " + QteTrainerPlugin.AllItemCount.Value);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", GUILayout.Width(40))) QteTrainerPlugin.AllItemCount.Value = Math.Max(1, QteTrainerPlugin.AllItemCount.Value - 1);
            if (GUILayout.Button("+", GUILayout.Width(40))) QteTrainerPlugin.AllItemCount.Value = Math.Min(9999, QteTrainerPlugin.AllItemCount.Value + 1);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("添加全部物品"))
            {
                int n = TrainerActions.AddAllItems(QteTrainerPlugin.AllItemCount.Value);
                QteTrainerPlugin.LogSource?.LogInfo($"Added {n} item stacks.");
            }
            if (GUILayout.Button("拉满当前NPC好感/星星 (" + QteTrainerPlugin.MaxFavorStars.Value + "星)"))
            {
                TrainerActions.MaxSelectedNpc();
            }
            if (GUILayout.Button("金钱设为 99999"))
            {
                TrainerActions.SetGold(99999);
            }
            if (GUILayout.Button("训练经验 +10000"))
            {
                TrainerActions.AddExp(10000);
            }
            if (GUILayout.Button("时间 +8 小时"))
            {
                TrainerActions.JumpTime(8f);
            }

            GUILayout.Space(6);
            GUILayout.Label("快捷传送 Key: [" + (string.IsNullOrEmpty(teleportInput) ? "(空)" : teleportInput) + "]");
            KeypadRow(KeypadRow1);
            KeypadRow(KeypadRow2);
            KeypadRow(KeypadRow3);
            GUILayout.BeginHorizontal();
            KeypadRowInline(KeypadRow4);
            if (GUILayout.Button("退格", GUILayout.Width(64)))
            {
                if (!string.IsNullOrEmpty(teleportInput))
                    teleportInput = teleportInput.Substring(0, teleportInput.Length - 1);
            }
            if (GUILayout.Button("清空", GUILayout.Width(64)))
                teleportInput = string.Empty;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("传送"))
            {
                QteTrainerPlugin.TeleportKey.Value = teleportInput;
                TrainerActions.Teleport(teleportInput);
            }
            if (GUILayout.Button("码头") && !string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetDock.Value))
                TrainerActions.Teleport(QteTrainerPlugin.TeleportPresetDock.Value);
            if (GUILayout.Button("黑沼泽") && !string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetSwamp.Value))
                TrainerActions.Teleport(QteTrainerPlugin.TeleportPresetSwamp.Value);
            if (GUILayout.Button("祭坛") && !string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetAltar.Value))
                TrainerActions.Teleport(QteTrainerPlugin.TeleportPresetAltar.Value);
            GUILayout.EndHorizontal();
            if (string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetDock.Value)
                && string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetSwamp.Value)
                && string.IsNullOrWhiteSpace(QteTrainerPlugin.TeleportPresetAltar.Value))
            {
                GUILayout.Label("提示: 预设 Key 在 cfg 中设置后即可用；或用上面的字符键盘输入后传送。", GUI.skin.box);
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("一键全开"))
                TrainerActions.SetAll(true);
            if (GUILayout.Button("一键全关"))
                TrainerActions.SetAll(false);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("导出配置"))
                TrainerActions.ExportPreset();
            if (GUILayout.Button("导入配置"))
                TrainerActions.ImportPreset();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("关闭总开关（全部功能停用）"))
                TrainerActions.ToggleMaster();

            GUILayout.Label("全部实时开关并保存到 BepInEx/config/arena.qte.trainer.cfg");
            GUILayout.Label($"快捷键: {QteTrainerPlugin.ToggleKey.Value} 总开关 / {QteTrainerPlugin.PanelKey.Value} 显示或隐藏面板");

            GUILayout.EndArea();
        }

        private void KeypadRow(string chars)
        {
            GUILayout.BeginHorizontal();
            KeypadRowInline(chars);
            GUILayout.EndHorizontal();
        }

        private void KeypadRowInline(string chars)
        {
            for (int i = 0; i < chars.Length; i++)
            {
                string c = chars[i].ToString();
                if (GUILayout.Button(c, GUILayout.Width(26), GUILayout.Height(22)))
                    teleportInput += c;
            }
        }
    }

    /* --------------------------------------------------------------------
     * QTE 1 - 空格打节奏 (Game.CompetitionForm / Game.CompetitionPlayer)
     * 破解思路: 禁掉 RingMiss, 每次 OnUpdate 直接把男女进度/能量拉满,
     *          游戏自己的进度检查就会进入 Victory。
     * -------------------------------------------------------------------- */
    [HarmonyPatch(typeof(CompetitionForm), "OnUpdate")]
    public static class CompetitionForm_OnUpdate_Patch
    {
        static void Postfix(CompetitionForm __instance)
        {
            if (!QteTrainerPlugin.On)
                return;
            if (!QteTrainerPlugin.QteAutoWin.Value || __instance == null)
                return;
            var p = __instance.MyPlayer;
            if (p == null)
                return;
            if (p.CurtProgressMale < 1f)
                p.CurtProgressMale = 1f;
            if (p.CurtProgressFemale < 1f)
                p.CurtProgressFemale = 1f;
            if (p.CurtEnergy < 999f)
                p.CurtEnergy = 999f;
            if (p.AtkCounter < 999f)
                p.AtkCounter = 999f;
        }
    }

    [HarmonyPatch(typeof(CompetitionForm), "RingMiss")]
    public static class CompetitionForm_RingMiss_Patch
    {
        static bool Prefix()
        {
            return !QteTrainerPlugin.On || !QteTrainerPlugin.QteAutoWin.Value;
        }
    }

    [HarmonyPatch(typeof(CompetitionPlayer), "Defeat")]
    public static class CompetitionPlayer_Defeat_Patch
    {
        static bool Prefix()
        {
            return !QteTrainerPlugin.On || !QteTrainerPlugin.QteAutoWin.Value;
        }
    }

    /* --------------------------------------------------------------------
     * QTE 2 - A/D 左右平衡 (Game.DredgeForm / Game.DredgePlayer)
     * 破解思路: 保持击打器在安全区、超时条拉高、失败条清零，
     *          并直接把女方进度拉满触发 Victory。
     * -------------------------------------------------------------------- */
    [HarmonyPatch(typeof(DredgeForm), "OnUpdate")]
    public static class DredgeForm_OnUpdate_Patch
    {
        static void Postfix(DredgeForm __instance)
        {
            if (!QteTrainerPlugin.On)
                return;
            if (!QteTrainerPlugin.QteAutoWin.Value || __instance == null)
                return;
            try { __instance.CurtHitterVel = 0f; } catch { }
            try { __instance.CurtTimeoutCounter = 9999f; } catch { }
            try { __instance.CurtDeadlingCounter = 0f; } catch { }
            var p = __instance.MyPlayer;
            if (p != null)
            {
                if (p.CurtProgressFemale < 1f)
                    p.CurtProgressFemale = 1f;
                if (p.CurtSweatProgress < 1f)
                    p.CurtSweatProgress = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(DredgePlayer), "Defeat")]
    public static class DredgePlayer_Defeat_Patch
    {
        static bool Prefix()
        {
            return !QteTrainerPlugin.On || !QteTrainerPlugin.QteAutoWin.Value;
        }
    }

    /* --------------------------------------------------------------------
     * 通用优化 - 无限血 / 无限体力 / 一击破防
     * 注意: 这里必须 patch 真实方法 InfoCreature.SetCurtHP/SetCurtRP,
     *       不能 patch 属性 setter (IL2CPP field accessor 无法被 Harmony 补)。
     * -------------------------------------------------------------------- */
    [HarmonyPatch(typeof(InfoCreature), "SetCurtHP")]
    public static class InfoCreature_SetCurtHP_Patch
    {
        // 注意: 参数名必须叫 v, 不能叫 value。
        // 游戏里的真实签名是 void Game.InfoCreature::SetCurtHP(float v),
        // 之前写成 value 会让 HarmonyX 直接
        //   Failed to patch ...: Parameter "value" not found in method void Game.InfoCreature::SetCurtHP(float v)
        // 整个补丁装不上, 无限血其实一直是失效的。
        static void Prefix(InfoCreature __instance, ref float v)
        {
            if (!QteTrainerPlugin.On) return;
            try
            {
                if (__instance == null) return;
                bool local = TrainerActions.IsLocalInfo(__instance);
                bool hasLocal = TrainerActions.HasLocalContext();

                // 无限血: 玩家保持满血；检测不到本地玩家时保守护全(维持旧版行为)。
                if (QteTrainerPlugin.InfiniteHp.Value && (local || !hasLocal))
                {
                    v = __instance.MaxHP;
                    return;
                }

                // 敌人: 一击破防(HP清零)。检测不到本地玩家时不动手, 防止误杀玩家。
                if (QteTrainerPlugin.OneHitBreak.Value && !local && hasLocal)
                {
                    v = 0f;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InfoCreature), "SetCurtRP")]
    public static class InfoCreature_SetCurtRP_Patch
    {
        // 同上: 真实签名是 SetCurtRP(float v)。
        static void Prefix(InfoCreature __instance, ref float v)
        {
            if (!QteTrainerPlugin.On) return;
            if (QteTrainerPlugin.InfiniteEnergy.Value && __instance != null)
            {
                v = __instance.MaxRP;
            }
        }
    }

    [HarmonyPatch(typeof(Creature), "TakeDamage")]
    public static class Creature_TakeDamage_Patch
    {
        static bool Prefix(Creature __instance)
        {
            if (!QteTrainerPlugin.On) return true;
            try
            {
                bool local = TrainerActions.IsLocalCrt(__instance);
                bool hasLocal = TrainerActions.HasLocalContext();

                // 玩家不受伤害
                if (local && (QteTrainerPlugin.InfiniteHp.Value || QteTrainerPlugin.OneHitBreak.Value))
                    return false;

                // 一击破防开且能识别本地玩家: 允许伤害进入 SetCurtHP -> 由 HP 补丁清零
                if (!local && QteTrainerPlugin.OneHitBreak.Value && hasLocal)
                    return true;

                // 无限血但未开一击破防时(或无法识别本地玩家), 连怪也不掉血(最保守/旧版行为)
                if (QteTrainerPlugin.InfiniteHp.Value && (!local || !hasLocal))
                    return false;
            }
            catch { }

            return true;
        }
    }

    [HarmonyPatch(typeof(Creature), "CurtMoveSpeed", MethodType.Getter)]
    public static class Creature_CurtMoveSpeed_Patch
    {
        // 之前这里是无条件乘算: 即使面板关着, 玩家从进游戏第一帧起就是 1.5 倍速。
        // 现在必须总开关开启才生效, 关闭时 __result 原样返回。
        static void Postfix(ref float __result)
        {
            if (!QteTrainerPlugin.On) return;
            __result *= QteTrainerPlugin.MoveSpeedMul.Value;
        }
    }

    [HarmonyPatch(typeof(PlayableMachine), "Update")]
    public static class PlayableMachine_Update_Patch
    {
        private static float _lastSkip = -1f;

        static void Postfix(PlayableMachine __instance)
        {
            // 自动跳对话以前默认就是开的, 从主菜单/开场剧情第一帧起就在推进 NextText。
            // 这本身就是一个"画面停在黑屏、进程却还在跑"的独立嫌疑点, 现在必须总开关开启才生效。
            if (!QteTrainerPlugin.On)
                return;
            if (!QteTrainerPlugin.SkipDialogue.Value || __instance == null)
                return;
            if (!__instance.IsPlaying)
                return;
            if (UnityEngine.Time.time - _lastSkip < 0.08f)
                return;
            _lastSkip = UnityEngine.Time.time;
            try { __instance.NextText(); } catch { }
        }
    }

    /* --------------------------------------------------------------------
     * 无限背包 / 物品数量不减
     * 注意: 不能直接跳过原方法(会让正常初始化/任务流程卡死或黑屏),
     *       而是让原方法照常执行, 随后再把扣掉的数量加回来。
     * -------------------------------------------------------------------- */
    [HarmonyPatch(typeof(Package), "RemoveItem")]
    public static class Package_RemoveItem_Patch
    {
        static void Postfix(object[] __args)
        {
            if (!QteTrainerPlugin.On) return;
            try
            {
                if (!QteTrainerPlugin.InfiniteInventory.Value || __args == null || __args.Length < 2)
                    return;
                string key = __args[0] as string;
                int count = Convert.ToInt32(__args[1]);
                if (string.IsNullOrEmpty(key) || count <= 0) return;
                var c = Commander.Instance;
                if (c != null) c.CmdAddItem(key, count);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"Restore RemoveItem: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Package), "CutItem")]
    public static class Package_CutItem_Patch
    {
        static void Prefix(object[] __args, out string __state)
        {
            __state = null;
            if (!QteTrainerPlugin.On) return;
            try
            {
                if (!QteTrainerPlugin.InfiniteInventory.Value || __args == null || __args.Length < 1)
                    return;
                var item = __args[0] as Item;
                if (item == null) return;
                var info = item.Info;
                if (info != null && info.Count > 0)
                    __state = info.Id + "|" + info.Count;
            }
            catch { }
        }

        // 无需再判总开关: 总开关关闭时上面的 Prefix 会留下 __state = null, 这里直接返回,
        // 从而让本次调用原样完成(不会把已经扣掉的数量再补回去)。
        static void Postfix(string __state)
        {
            if (string.IsNullOrEmpty(__state))
                return;
            try
            {
                int split = __state.IndexOf('|');
                if (split <= 0) return;
                string key = __state.Substring(0, split);
                int count = int.Parse(__state.Substring(split + 1));
                if (!QteTrainerPlugin.InfiniteInventory.Value || count <= 0 || string.IsNullOrEmpty(key))
                    return;
                var c = Commander.Instance;
                if (c != null) c.CmdAddItem(key, count);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"Restore CutItem: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Commander), "CmdRemoveItem")]
    public static class Commander_CmdRemoveItem_Patch
    {
        static void Prefix(object[] __args, out bool __state)
        {
            __state = false;
            if (!QteTrainerPlugin.On) return;
            try
            {
                if (!QteTrainerPlugin.InfiniteInventory.Value || __args == null || __args.Length < 2)
                    return;
                string key = __args[0] as string;
                int count = Convert.ToInt32(__args[1]);
                __state = !string.IsNullOrEmpty(key) && count > 0;
            }
            catch { }
        }

        // 同上: 总开关关闭时 Prefix 留下 __state = false, 这里直接返回。
        static void Postfix(object[] __args, bool __state)
        {
            if (!__state || __args == null || __args.Length < 2)
                return;
            try
            {
                string key = __args[0] as string;
                int count = Convert.ToInt32(__args[1]);
                if (string.IsNullOrEmpty(key) || count <= 0) return;
                var c = Commander.Instance;
                if (c != null) c.CmdAddItem(key, count);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"Restore CmdRemoveItem: {ex.Message}");
            }
        }
    }
}
