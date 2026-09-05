using System;
using System.Reflection;
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

            // Always create the component first so the in-game panel works even
            // if one optional Harmony patch fails to install.
            try { AddComponent<QteTrainerUi>(); } catch (Exception ex) { LogSource.LogError(ex); }

            // Patch each class independently so a single bad patch cannot kill the plugin.
            var harmony = new Harmony("arena.qte.trainer");
            PatchClass<CompetitionForm_OnUpdate_Patch>(harmony);
            PatchClass<CompetitionForm_RingMiss_Patch>(harmony);
            PatchClass<CompetitionPlayer_Defeat_Patch>(harmony);
            PatchClass<DredgeForm_OnUpdate_Patch>(harmony);
            PatchClass<DredgePlayer_Defeat_Patch>(harmony);
            PatchClass<InfoCreature_SetCurtHP_Patch>(harmony);
            PatchClass<InfoCreature_SetCurtRP_Patch>(harmony);
            PatchClass<Creature_TakeDamage_Patch>(harmony);
            PatchClass<Creature_CurtMoveSpeed_Patch>(harmony);
            PatchClass<PlayableMachine_Update_Patch>(harmony);

            LogSource.LogInfo("QTE Trainer loaded.");
        }

        private static void PatchClass<T>(Harmony harmony) where T : class
        {
            try
            {
                harmony.PatchAll(typeof(T));
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"Patch {typeof(T).Name} failed, continuing: {ex.Message}");
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
                value = __instance.CurtMaxRP;
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
