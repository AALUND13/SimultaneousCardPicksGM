using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SimultaneousCardPicksGM.Patches {
    public class PickTimerPatch {
        public static void ApplyPatch(Harmony harmony) {
            Type targetType = typeof(PickTimer.PickTimer)
                .Assembly
                .GetType("PickTimer.Util.CardChoicePatchRPCA_DoEndPick", true);

            MethodInfo target = AccessTools.Method(targetType, "Postfix");
            var prefix = AccessTools.Method(typeof(PickTimerPatch), nameof(PickTimerPrefix));

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        public static bool PickTimerPrefix() {
            if(!CardChoicePatch.LastPickerPlayer.data.view.IsMine && SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                return false;
            } 
            return true;
        }
    }
}
