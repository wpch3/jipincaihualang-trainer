using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
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
        public static ConfigEntry<float> MoveSpeedMul;
        public static ConfigEntry<bool> ShowUi;
        public static ConfigEntry<int> AllItemCount;
        public static ConfigEntry<bool> AllItemsEnabled;

        public override void Load()
        {
            Instance = this;
            LogSource = Log;

            QteAutoWin = Config.Bind("QTE", "AutoWin", true, "自动通过空格节奏/AD平衡两个小游戏");
            SkipDialogue = Config.Bind("Helper", "SkipDialogue", true, "自动推进对话/剧情");
            InfiniteHp = Config.Bind("Combat", "InfiniteHp", true, "玩家无限生命");
            InfiniteEnergy = Config.Bind("Combat", "InfiniteEnergy", true, "玩家无限体力/精力");
            MoveSpeedMul = Config.Bind("Helper", "MoveSpeedMul", 1.5f, "移动速度倍率");
            ShowUi = Config.Bind("UI", "ShowPanel", true, "显示训练器面板");
            AllItemsEnabled = Config.Bind("Items", "GiveAllEnabled", true, "允许一键添加全部物品");
            AllItemCount = Config.Bind("Items", "GiveAllCount", 1, "一键添加全部物品时的每种物品数量");

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
            var commander = Commander.Instance;
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
                var commander = Commander.Instance;
                if (commander != null) commander.CmdJumpTime(hours);
            }
            catch (Exception ex)
            {
                QteTrainerPlugin.LogSource?.LogWarning($"JumpTime: {ex.Message}");
            }
        }
    }

    [RegisterInIl2Cpp]
    public class QteTrainerUi : MonoBehaviour
    {
        private bool show = true;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
                show = !show;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F6))
                QteTrainerPlugin.ShowUi.Value = !QteTrainerPlugin.ShowUi.Value;
        }

        private void OnGUI()
        {
            show = QteTrainerPlugin.ShowUi.Value;
            if (!show)
                return;

            GUILayout.BeginArea(new Rect(8, 8, 330, 320));
            GUILayout.Label("<b>QTE / 万能 Trainer</b>", GUI.skin.label);

            QteTrainerPlugin.QteAutoWin.Value = GUILayout.Toggle(QteTrainerPlugin.QteAutoWin.Value, "QTE 自动通关（空格节奏 + AD 平衡）");
            QteTrainerPlugin.InfiniteHp.Value = GUILayout.Toggle(QteTrainerPlugin.InfiniteHp.Value, "无限生命");
            QteTrainerPlugin.InfiniteEnergy.Value = GUILayout.Toggle(QteTrainerPlugin.InfiniteEnergy.Value, "无限体力/精力");
            QteTrainerPlugin.SkipDialogue.Value = GUILayout.Toggle(QteTrainerPlugin.SkipDialogue.Value, "自动跳过对话/剧情");

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
                LogSource?.LogInfo($"Added {n} item stacks.");
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
            GUILayout.Space(4);
            GUILayout.Label("全部实时开关并保存到 BepInEx/config/arena.qte.trainer.cfg");
            GUILayout.Label("快捷键: F5 显示/隐藏面板");

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
     * 通用优化 - 无限血 / 无限体力
     * 注意: 这里必须 patch 真实方法 InfoCreature.SetCurtHP/SetCurtRP,
     *       不能 patch 属性 setter (IL2CPP field accessor 无法被 Harmony 补)。
     * -------------------------------------------------------------------- */
    [HarmonyPatch(typeof(InfoCreature), "SetCurtHP")]
    public static class InfoCreature_SetCurtHP_Patch
    {
        static void Prefix(InfoCreature __instance, ref float value)
        {
            if (QteTrainerPlugin.InfiniteHp.Value && __instance != null)
            {
                value = __instance.MaxHP;
            }
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
        static bool Prefix()
        {
            return !QteTrainerPlugin.InfiniteHp.Value;
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
}
