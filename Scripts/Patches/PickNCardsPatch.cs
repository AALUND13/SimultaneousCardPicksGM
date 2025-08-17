using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnboundLib;
using UnboundLib.GameModes;

namespace SimultaneousCardPicksGM.Patches {
    internal class PickNCardsPatch { 
        public static void Patch(Harmony harmony) {
            var nestedType = typeof(PickNCards.PickNCards)
                .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(t => t.Name.Contains("ExtraPicks"));

            var original = AccessTools.Method(nestedType, "MoveNext");
            var prefix = AccessTools.Method(typeof(PickNCardsPatch), nameof(PickNCardsPrefix));

            GameModeManager.AddHook(SimultaneousPicksHooks.OnSimultaneousPickStart, OnSimultaneousPickStart);
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static IEnumerator OnSimultaneousPickStart(IGameModeHandler gm) {
            SimultaneousCardPicksGM.instance.ExecuteAfterSeconds(0.5f, () => {
                List<int> playerIDsToPick = (List<int>)AccessTools.Field(typeof(PickNCards.PickNCards), "playerIDsToPick")
                    .GetValue(null);
                int picks = (int)AccessTools.Field(typeof(PickNCards.PickNCards), "picks")
                    .GetValue(null);

                if(picks <= 1 || playerIDsToPick.Count < 1) return;
                foreach(int playerID in playerIDsToPick) {
                    int extraPickCount = picks - 1;

                    Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
                    SimultaneousPicksHandler.Instance.QueuePlayerForAdditionalPicks(player, extraPickCount);
                }
                return;
            });

            yield break;
        }

        public static bool PickNCardsPrefix() {
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                return false;
            }
            return true;
        }
    }
}
