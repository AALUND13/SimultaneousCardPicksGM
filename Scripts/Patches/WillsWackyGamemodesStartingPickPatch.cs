using HarmonyLib;
using Photon.Pun;
using RWF;
using SimultaneousCardPicksGM;
using SimultaneousCardPicksGM.Handlers;
using SimultaneousCardPicksGM.Patches;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnboundLib.GameModes;
using UnityEngine;

namespace SimultaneousCardPicksGM.Patches {
    /*
     * We have use pure reflection here because we cannot import `WillsWackyGamemodes` into the unity project
     * do to the fact one it dependency using incompatible versions .NET Framework.
    **/
    internal class WillsWackyGamemodesStartingPickPatch {
        private static Type ExtraStartingPicksType;

        public static void Patch(Harmony harmony, Assembly assembly) {
            ExtraStartingPicksType = assembly.GetType("WWGM.GameModeModifiers.ExtraStartingPicks");
            var nestedType = ExtraStartingPicksType
                .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static)
                .First(t => t.Name.Contains("StartingPicks"));

            var original = AccessTools.Method(nestedType, "MoveNext");
            var prefix = AccessTools.Method(typeof(WillsWackyGamemodesStartingPickPatch), nameof(WillsWackyGamemodesStartingPickPrefix));

            GameModeManager.AddHook(GameModeHooks.HookPickStart, OnSimultaneousPickPhaseStart);
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static IEnumerator OnSimultaneousPickPhaseStart(IGameModeHandler gm) {
            FieldInfo extraPicksField = AccessTools.Field(ExtraStartingPicksType, "extraPicks");
            MethodInfo extraPicksCurrentValueField = AccessTools.PropertyGetter(extraPicksField.FieldType, "CurrentValue");
            FieldInfo extraPicksHasRunField = AccessTools.Field(ExtraStartingPicksType, "pickHasRun");

            object extraPicksInstance = extraPicksField.GetValue(null);
            
            int extraPicksValue = (int)extraPicksCurrentValueField.Invoke(extraPicksInstance, null);
            bool pickHasRun = (bool)extraPicksHasRunField.GetValue(null);

            if(pickHasRun || !PhotonNetwork.IsMasterClient || !SimultaneousPicksHandler.Instance.InSimultaneousPickPhase) { yield break; }

            List<Player> pickOrder = PlayerManager.instance.GetPickOrder(null);
            foreach(Player player in pickOrder) {
                SimultaneousPicksHandler.Instance.QueuePlayerForAdditionalPicks(player, extraPicksValue);
            }

            extraPicksHasRunField.SetValue(null, true);

            yield break;
        }

        public static bool WillsWackyGamemodesStartingPickPrefix() {
            if (SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                return false;
            }
            return true;
        }
    }
}
