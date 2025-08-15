using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;

namespace SimultaneousCardPicksGM.Patches {
    [HarmonyPatch(typeof(CardChoice))]
    public class CardChoicePatch {
        [HarmonyPatch("RPCA_DoEndPick")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> RPCA_DoEndPickTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching RPCA_DoEndPick in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isSimMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPlayerInSimCardPicksMode));
            MethodInfo desroyCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(DesroyCards));
            FieldInfo spawnedCardsField = AccessTools.Field(typeof(CardChoice), "spawnedCards");

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].StoresField(spawnedCardsField)) {
                    var label = il.DefineLabel();
                    codes[i - 4].labels.Add(label);

                    codes.Insert(i - 4, new CodeInstruction(OpCodes.Ldarg_S, 4)); // Load playerID argument
                    codes.Insert(i - 3, new CodeInstruction(OpCodes.Call, isSimMethod)); // Call IsSimultaneousCardPicksGameMode
                    codes.Insert(i - 2, new CodeInstruction(OpCodes.Brtrue, label)); // if true -> skip spawnedCards overwrite
                    codes.Insert(i - 1, new CodeInstruction(OpCodes.Ldarg_1)); // Load cardIDs argument
                    codes.Insert(i    , new CodeInstruction(OpCodes.Call, desroyCardsMethod)); // Call DesroyCards
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ret)); // Return

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to in RPCA_DoEndPick.");
                    i += 6;
                }
            }

            return codes;
        }

        [HarmonyPatch("ReplaceCards", MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ReplaceCardsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching ReplaceCards to HideCardsFromOtherPlayers in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isSimMethod = AccessTools.Method(typeof(Utils), nameof(Utils.IsInSimultaneousPickPhase));
            MethodInfo hideCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(HideCardsFromOtherPlayers));
            MethodInfo getItemFieldMethod = AccessTools.Method(typeof(List<GameObject>), "get_Item");

            FieldInfo spawnedCardsField = AccessTools.Field(typeof(CardChoice), "spawnedCards");
            FieldInfo indexField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), "ReplaceCards"), "<i>5__2");
            FieldInfo stateField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), "ReplaceCards"), "<>1__state");



            MethodInfo PhotonViewRPCMethod = AccessTools.Method(typeof(PhotonView), nameof(PhotonView.RPC), new Type[] { typeof(string), typeof(RpcTarget), typeof(object[]) });
            MethodInfo IsPickerIdIsMeMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPickerIdIsMe));
            MethodInfo RPCA_DonePickingMethod = AccessTools.Method(typeof(CardChoice), "RPCA_DonePicking");

            int index = 0;

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].opcode == OpCodes.Stloc_2 && codes[i - 1].LoadsField(indexField) && codes[i - 2].opcode == OpCodes.Ldarg_0) {
                    if(index == 1) {
                        var skipLabel = il.DefineLabel();
                        codes[i + 1].labels.Add(skipLabel);

                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, isSimMethod)); // Call IsSimultaneousCardPicksGameMode
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Brfalse, skipLabel)); // if false -> continue
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldloc_1)); // Load the this (CardChoice instance)
                        codes.Insert(i + 4, new CodeInstruction(OpCodes.Ldfld, spawnedCardsField)); // Load spawnedCards
                        codes.Insert(i + 5, new CodeInstruction(OpCodes.Ldarg_0)); // Load this ('<IDoEndPick>d__17' instance)
                        codes.Insert(i + 6, new CodeInstruction(OpCodes.Ldfld, indexField)); // Load the index
                        codes.Insert(i + 7, new CodeInstruction(OpCodes.Callvirt, getItemFieldMethod)); // Call get_Item on the list
                        codes.Insert(i + 8, new CodeInstruction(OpCodes.Call, hideCardsMethod)); // Call HideCardsFromOtherPlayers

                        UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to HideCardsFromOtherPlayers in ReplaceCards.");
                        i += 9;
                    }
                    index++;
                }

                if(codes[i].Calls(PhotonViewRPCMethod) && codes[i - 3].opcode == OpCodes.Ldstr && codes[i - 3].operand.ToString() == "RPCA_DonePicking") {
                    var originalLabelsAtContinue = codes[i - 5].labels;

                    var skipLabel = il.DefineLabel();
                    var continueLabel = il.DefineLabel();

                    codes[i + 1].labels.Add(skipLabel);

                    codes.Insert(i - 5, new CodeInstruction(OpCodes.Call, isSimMethod) { labels = originalLabelsAtContinue }); // Call IsSimultaneousCardPicksGameMode
                    codes.Insert(i - 4, new CodeInstruction(OpCodes.Brfalse, continueLabel)); // if false -> continue
                    codes.Insert(i - 3, new CodeInstruction(OpCodes.Ldloc_1)); // Load the this (CardChoice instance)
                    codes.Insert(i - 2, new CodeInstruction(OpCodes.Call, RPCA_DonePickingMethod)); // Call RPCA_DonePicking locally
                    codes.Insert(i - 1, new CodeInstruction(OpCodes.Br, skipLabel)); // Branch to skip the RPC call
                    codes.Insert(i    , new CodeInstruction(OpCodes.Nop) { labels = new List<Label> { continueLabel } }); // Continue label for the original code path

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to RPCA_DonePicking in ReplaceCards.");
                    i += 6;
                }
            }

            return codes;
        }

        [HarmonyPatch(nameof(CardChoice.DoPick), MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DoPickTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching DoPick to prevent \"CardChoiceVisuals.Hide()\" from getting called in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isMyPlayerInGameModeMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPlayerIsMyAndInSimultaneousPicksGameMode));
            MethodInfo cardChoiceVisualsHideMethod = AccessTools.Method(typeof(CardChoiceVisuals), nameof(CardChoiceVisuals.Hide));
            FieldInfo cardChoiceInstanceField = AccessTools.Field(typeof(CardChoiceVisuals), "instance");
            FieldInfo picketIDToSetField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), nameof(CardChoice.DoPick)), "picketIDToSet");

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].Calls(cardChoiceVisualsHideMethod) && codes[i - 1].LoadsField(cardChoiceInstanceField)) {
                    var skipLabel = il.DefineLabel();
                    codes[i + 1].labels.Add(skipLabel);

                    codes.Insert(i - 1, new CodeInstruction(OpCodes.Ldarg_0)); // Load this (CardChoice instance)
                    codes.Insert(i    , new CodeInstruction(OpCodes.Ldfld, picketIDToSetField)); // Load picketIDToSet
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, isMyPlayerInGameModeMethod)); // Call IsMyPlayerInGameMode
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Brtrue, skipLabel)); // if true -> skip CardChoiceVisuals.Hide()

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to CardChoiceVisuals.Hide() in DoPick.");
                    i += 4;
                }
            }
            return codes;
        }

        public static void DesroyCards(int[] cardIDs) {
            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", cardIDs);
            foreach(GameObject card in cards) {
                GameObject.Destroy(card);
            }
        }

        public static bool IsPlayerInSimCardPicksMode(int playerID) {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == playerID);

            bool checkResult = !Utils.IsInSimultaneousPickPhase() || player == null || player.data.view.IsMine;
            return checkResult;
        }

        private static void HideCardsFromOtherPlayers(GameObject card) {
            NetworkingManager.RPC_Others(
                typeof(CardChoicePatch),
                nameof(HideCards),
                card.GetComponent<PhotonView>().ViewID
            );
        }

        private static bool IsPlayerIsMyAndInSimultaneousPicksGameMode(int playerID) {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == playerID);
            bool checkResult = Utils.IsInSimultaneousPickPhase() && player != null && !player.data.view.IsMine;
            return checkResult;
        }

        private static bool IsPickerIdIsMe() {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == CardChoice.instance.pickrID);
            return player != null && player.data.view.IsMine;
        }

        [UnboundRPC]
        private static void HideCards(int cardID) {
            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", new int[] { cardID });
            foreach(GameObject card in cards) {
                GameObject cardBase = card.GetComponentInChildren<CardInfoDisplayer>().gameObject;
                foreach(Transform child in cardBase.transform) {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }
}
