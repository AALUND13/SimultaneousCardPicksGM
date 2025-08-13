using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnboundLib;

namespace SimultaneousCardPicksGM.Patches {
    [HarmonyPatch(typeof(CardChoiceVisuals))]
    public class CardChoiceVisualPatch {
        [HarmonyPatch("SetCurrentSelected")]
        [HarmonyPrefix]
        public static bool SetCurrentSelectedPrefix(CardChoiceVisuals __instance, int toSet) {
            if (Utils.IsSimultaneousCardPicksGameMode()) {
                __instance.InvokeMethod("RPCA_SetCurrentSelected", toSet);
                return false;
            }
            return true;
        }
    }
}
