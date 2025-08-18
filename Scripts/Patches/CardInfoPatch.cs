using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace SimultaneousCardPicksGM.Patches {
    [HarmonyPatch(typeof(CardInfo))]
    public class CardInfoPatch {
        static Dictionary<GameObject, bool> wasSelectedCards = new Dictionary<GameObject, bool>();

        [HarmonyPatch("RPCA_ChangeSelected")]
        [HarmonyPostfix]
        public static void RPCA_ChangeSelectedPrefix(CardInfo __instance, bool setSelected) {
            if(!wasSelectedCards.ContainsKey(__instance.gameObject) || !wasSelectedCards[__instance.gameObject]) {
                wasSelectedCards[__instance.gameObject] = setSelected;
            }
        }

        public static bool WasCardSelected(GameObject card) {
            if(!wasSelectedCards.ContainsKey(card)) {
                wasSelectedCards[card] = false;
            }

            return wasSelectedCards[card];
        }
    }
}
