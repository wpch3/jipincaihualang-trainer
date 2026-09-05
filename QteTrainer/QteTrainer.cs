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
    [BepInPlugin("arena.qte.trainer", "QTE / 万能 Trainer", "1.1.0")]
    public class QteTrainerPlugin : BasePlugin
    {
        public static QteTrainerPlugin Instance { get; private set; }

        public static ManualLogSource LogSource { get; private set; }

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

        public override void Load()
        {
            Instance = this;
            LogSource = Log;

            QteAutoWin = Config.Bind("QTE", "AutoWin", true, "自动通过空格节奏/AD平衡两个小游戏");
            SkipDialogue = Config.Bind("Helper", "SkipDialogue", true, "自动推进对话/剧情");
            InfiniteHp = Config.Bind("Combat", "InfiniteHp", true, "玩家无限生命/无敌");
            InfiniteEnergy = Config.Bind("Combat", "InfiniteEnergy", true, "玩家无限体力/精力(取消体力消耗)");
            OneHitBreak = Config.Bind("Combat", "OneHitBreak", false, "一击破防: 非玩家目标被打时 HP 直接清 0");
            MoveSpeedMul = Config.Bind("Helper", "MoveSpeedMul", 1.5f, "移动速度倍率");
            ShowUi = Config.Bind("UI", "ShowPanel", true, "显示训练器面板");
            AllItemsEnabled = Config.Bind("Items", "GiveAllEnabled", true, "允许一键添加全部物品");
            AllItemCount = Config.Bind("Items", "GiveAllCount", 1, "一键添加全部物品时的每种物品数量");
            InfiniteInventory = Config.Bind("Items", "InfiniteInventory", false, "物品数量不减(资源消耗不扣除; 建议进入正常场景后再开启)");
            MaxFavorStars = Config.Bind("NPC", "MaxFavorStars", 5, "一键拉满NPC时设置为多少星");
            TeleportKey = Config.Bind("Teleport", "Key", "", "快捷传送的锚点/传送点 Key(可在游戏内文本框中填写)");
            TeleportPresetDock = Config.Bind("Teleport", "PresetDock", "", "码头预设传送 Key(留空则按钮提示未设置)");
            TeleportPresetSwamp = Config.Bind("Teleport", "PresetSwamp", "", "黑沼泽预设传送 Key(留空则按钮提示未设置)");
            TeleportPresetAltar = Config.Bind("Teleport", "PresetAltar", "", "祭坛/祭神台预设传送 Key(留空则按钮提示未设置)");

            // Always create the component first so the in-game panel works even
            // if one optional Harmony patch fails to install.
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
    }

    [RegisterInIl2Cpp]
    public class QteTrainerUi : MonoBehaviour
    {
        private bool show = true;
        private string teleportInput = string.Empty;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
                show = !show;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F6))
                QteTrainerPlugin.ShowUi.Value = !QteTrainerPlugin.ShowUi.Value;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F4))
                TrainerActions.SetAll(true);
        }

        private void EnsureTeleportInput()
        {
            if (string.IsNullOrEmpty(teleportInput) && !string.IsNullOrEmpty(QteTrainerPlugin.TeleportKey.Value))
                teleportInput = QteTrainerPlugin.TeleportKey.Value;
        }

        private void OnGUI()
        {
            show = QteTrainerPlugin.ShowUi.Value;
            if (!show)
                return;

            EnsureTeleportInput();

            GUILayout.BeginArea(new Rect(8, 8, 360, 460));
            GUILayout.Label("<b>QTE / 万能 Trainer</b>", GUI.skin.label);

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
            if (GUILayout.Button("-")) QteTrainerPlugin.AllItemCount.Value = Math.Max(1, QteTrainerPlugin.AllItemCount.Value - 1);
            if (GUILayout.Button("+")) QteTrainerPlugin.AllItemCount.Value = Math.Min(9999, QteTrainerPlugin.AllItemCount.Value + 1);
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
            GUILayout.Label("快捷传送(填游戏实际锚点/传送点 Key)");
            GUILayout.BeginHorizontal();
            teleportInput = GUILayout.TextField(teleportInput, 120);
            if (GUILayout.Button("传送", GUILayout.Width(60)))
            {
                QteTrainerPlugin.TeleportKey.Value = teleportInput;
                TrainerActions.Teleport(teleportInput);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
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
                GUILayout.Label("提示: 预设 Key 在 cfg 中设置后即可用；或在上面直接输入后传送。", GUI.skin.box);
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

            GUILayout.Label("全部实时开关并保存到 BepInEx/config/arena.qte.trainer.cfg");
            GUILayout.Label("快捷键: F4 一键全开 / F5 显示/隐藏面板");

            GUILayout.EndArea();
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
            return !QteTrainerPlugin.QteAutoWin.Value;
        }
    }

    [HarmonyPatch(typeof(CompetitionPlayer), "Defeat")]
    public static class CompetitionPlayer_Defeat_Patch
    {
        static bool Prefix()
        {
            return !QteTrainerPlugin.QteAutoWin.Value;
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
            return !QteTrainerPlugin.QteAutoWin.Value;
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
        static void Prefix(InfoCreature __instance, ref float value)
        {
            try
            {
                if (__instance == null) return;
                bool local = TrainerActions.IsLocalInfo(__instance);
                bool hasLocal = TrainerActions.HasLocalContext();

                // 无限血: 玩家保持满血；检测不到本地玩家时保守护全(维持旧版行为)。
                if (QteTrainerPlugin.InfiniteHp.Value && (local || !hasLocal))
                {
                    value = __instance.MaxHP;
                    return;
                }

                // 敌人: 一击破防(HP清零)。检测不到本地玩家时不动手, 防止误杀玩家。
                if (QteTrainerPlugin.OneHitBreak.Value && !local && hasLocal)
                {
                    value = 0f;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(InfoCreature), "SetCurtRP")]
    public static class InfoCreature_SetCurtRP_Patch
    {
        static void Prefix(InfoCreature __instance, ref float value)
        {
            if (QteTrainerPlugin.InfiniteEnergy.Value && __instance != null)
            {
                value = __instance.MaxRP;
            }
        }
    }

    [HarmonyPatch(typeof(Creature), "TakeDamage")]
    public static class Creature_TakeDamage_Patch
    {
        static bool Prefix(Creature __instance)
        {
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
        static void Postfix(ref float __result)
        {
            __result *= QteTrainerPlugin.MoveSpeedMul.Value;
        }
    }

    [HarmonyPatch(typeof(PlayableMachine), "Update")]
    public static class PlayableMachine_Update_Patch
    {
        private static float _lastSkip = -1f;

        static void Postfix(PlayableMachine __instance)
        {
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
