﻿using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using SimultaneousCardPicksGM.Handlers;
using SimultaneousCardPicksGM.Monobehaviours;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

namespace SimultaneousCardPicksGM.Patches {
    [HarmonyPatch(typeof(CardChoice))]
    internal class CardChoicePatch {
        public static Player LastPickerPlayer { get; private set; }

        public static Dictionary<Player, List<Player>> PlayerToClientMates {
            get {
                var clientMatesByPlayer = new Dictionary<Player, List<Player>>();

                foreach(Player player in PlayerManager.instance.players) {
                    if(!clientMatesByPlayer.ContainsKey(player)) {
                        clientMatesByPlayer[player] = new List<Player>();
                    }
                }

                foreach(var entry in clientMatesByPlayer) {
                    Player mainPlayer = entry.Key;
                    foreach(Player otherPlayer in clientMatesByPlayer.Keys) {
                        if(otherPlayer != mainPlayer &&
                            otherPlayer.data.view.Owner == mainPlayer.data.view.Owner) {
                            clientMatesByPlayer[mainPlayer].Add(otherPlayer);
                        }
                    }
                }

                return clientMatesByPlayer;
            }
        }


        [HarmonyPatch(nameof(CardChoice.StartPick))]
        [HarmonyPostfix]
        public static void StartPickPostfix(CardChoice __instance, int pickerIDToSet) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching StartPick to set pickrID in Simultaneous Card Picks Game Mode.");
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                Player player = PlayerManager.instance.players.Find(p => p.playerID == pickerIDToSet);
                if(player != null && !player.data.view.IsMine) {
                    __instance.IsPicking = false;
                } else {
                    UnityEngine.Debug.LogWarning("SimultaneousCardPicksGM: No player found or player is not mine, pickrID will not be set.");
                }
            }
        }

        [HarmonyPatch("RPCA_DoEndPick")]
        [HarmonyTranspiler]
        [HarmonyDebug]
        public static IEnumerable<CodeInstruction> RPCA_DoEndPickTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching RPCA_DoEndPick in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isSimMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPlayerInSimCardPicksMode));
            MethodInfo desroyCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(DesroyCards));
            MethodInfo removeCardToPlayerSpawnedCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(RemoveCardToPlayerSpawnedCards));
            FieldInfo spawnedCardsField = AccessTools.Field(typeof(CardChoice), "spawnedCards");

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].StoresField(spawnedCardsField)) {
                    var label = il.DefineLabel();
                    codes[i - 4].labels.Add(label);

                    CodeInstruction[] injectedInstructions = new CodeInstruction[] {
                        // Remove cards from player spawned cards
                        new CodeInstruction(OpCodes.Ldarg_1), // Load cardIDs argument
                        new CodeInstruction(OpCodes.Ldarg_S, 4), // Load playerID argument
                        new CodeInstruction(OpCodes.Callvirt, removeCardToPlayerSpawnedCardsMethod),

                        // Check if player is not in Simultaneous Card Picks or is the local player
                        new CodeInstruction(OpCodes.Ldarg_S, 4), // Load playerID argument
                        new CodeInstruction(OpCodes.Call, isSimMethod), // Call IsSimultaneousCardPicksGameMode
                        new CodeInstruction(OpCodes.Brtrue, label), // if true -> skip spawnedCards overwrite

                        // Destroy cards
                        new CodeInstruction(OpCodes.Ldarg_1), // Load cardIDs argument
                        new CodeInstruction(OpCodes.Call, desroyCardsMethod), // Call DesroyCards
                        new CodeInstruction(OpCodes.Ret) // Return
                    };
                    codes.InsertRange(i - 4, injectedInstructions);

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to in RPCA_DoEndPick.");
                    i += injectedInstructions.Length;
                }
            }

            return codes;
        }

        [HarmonyPatch("ReplaceCards", MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ReplaceCardsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching ReplaceCards to HideCardsFromOtherPlayers in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo IsSimultaneousPickPhaseInProgressMethod = AccessTools.Method(typeof(SimultaneousPicksHandler), nameof(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive));
            MethodInfo hideCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(HideCardsFromOtherPlayers));
            MethodInfo AddToPlayerSpawnedCardsMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(AddCardToPlayerSpawnedCards));
            MethodInfo getItemFieldMethod = AccessTools.Method(typeof(List<GameObject>), "get_Item");

            FieldInfo spawnedCardsField = AccessTools.Field(typeof(CardChoice), "spawnedCards");
            FieldInfo pickrIDField = AccessTools.Field(typeof(CardChoice), nameof(CardChoice.pickrID));
            FieldInfo indexField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), "ReplaceCards"), "<i>5__2");
            FieldInfo stateField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), "ReplaceCards"), "<>1__state");



            MethodInfo PhotonViewRPCMethod = AccessTools.Method(typeof(PhotonView), nameof(PhotonView.RPC), new Type[] { typeof(string), typeof(RpcTarget), typeof(object[]) });
            MethodInfo IsPickerIdIsMeMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPickerIdIsMe));
            MethodInfo RPCA_DonePickingMethod = AccessTools.Method(typeof(CardChoice), "RPCA_DonePicking");

            int index = 0;

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].opcode == OpCodes.Stloc_2 && codes[i - 1].LoadsField(indexField) && codes[i - 2].opcode == OpCodes.Ldarg_0) {
                    if(index == 1) {
                        var continueLable = il.DefineLabel();
                        codes[i + 1].labels.Add(continueLable);

                        CodeInstruction[] injectedInstructions = new CodeInstruction[] {
                            // Check if Simultaneous Card Picks is active
                            new CodeInstruction(OpCodes.Call, IsSimultaneousPickPhaseInProgressMethod), // Call IsSimultaneousCardPicksGameMode
                            new CodeInstruction(OpCodes.Brfalse, continueLable), // if false -> continue

                            // Hide cards from other players
                            new CodeInstruction(OpCodes.Ldloc_1), // Load the this (CardChoice instance)
                            new CodeInstruction(OpCodes.Ldfld, spawnedCardsField), // Load spawnedCards
                            new CodeInstruction(OpCodes.Ldarg_0), // Load this ('<IDoEndPick>d__17' instance)
                            new CodeInstruction(OpCodes.Ldfld, indexField), // Load the index
                            new CodeInstruction(OpCodes.Callvirt, getItemFieldMethod), // Call get_Item on the list
                            new CodeInstruction(OpCodes.Ldloc_1), // Load the this (CardChoice instance)
                            new CodeInstruction(OpCodes.Ldfld, pickrIDField), // Load pickrID
                            new CodeInstruction(OpCodes.Call, hideCardsMethod), // Call HideCardsFromOtherPlayers

                            // Add card to player spawned cards
                            new CodeInstruction(OpCodes.Ldloc_1), // Load the this (CardChoice instance)
                            new CodeInstruction(OpCodes.Ldfld, spawnedCardsField), // Load spawnedCards
                            new CodeInstruction(OpCodes.Ldarg_0), // Load this ('<IDoEndPick>d__17' instance)
                            new CodeInstruction(OpCodes.Ldfld, indexField), // Load the index
                            new CodeInstruction(OpCodes.Callvirt, getItemFieldMethod), // Call get_Item on the list
                            new CodeInstruction(OpCodes.Ldloc_1), // Load the this (CardChoice instance)
                            new CodeInstruction(OpCodes.Ldfld, pickrIDField), // Load pickrID
                            new CodeInstruction(OpCodes.Call, AddToPlayerSpawnedCardsMethod) // Call RPCS_AddCardToPlayerSpawnedCards
                        };
                        codes.InsertRange(i + 1, injectedInstructions);

                        UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to HideCardsFromOtherPlayers in ReplaceCards.");
                        i += injectedInstructions.Length;
                    }
                    index++;
                }

                if(codes[i].Calls(PhotonViewRPCMethod) && codes[i - 3].opcode == OpCodes.Ldstr && codes[i - 3].operand.ToString() == "RPCA_DonePicking") {
                    var originalLabelsAtContinue = codes[i - 5].labels;

                    var skipLabel = il.DefineLabel();
                    var continueLabel = il.DefineLabel();

                    codes[i + 1].labels.Add(skipLabel);

                    CodeInstruction[] injectedInstructions = new CodeInstruction[] {
                        new CodeInstruction(OpCodes.Call, IsSimultaneousPickPhaseInProgressMethod) { labels = originalLabelsAtContinue }, // Call IsSimultaneousCardPicksGameMode
                        new CodeInstruction(OpCodes.Brfalse, continueLabel), // if false -> continue
                        new CodeInstruction(OpCodes.Ldloc_1), // Load the this (CardChoice instance)
                        new CodeInstruction(OpCodes.Call, RPCA_DonePickingMethod), // Call IsPickerIdIsMe
                        new CodeInstruction(OpCodes.Br, skipLabel), // Branch to skip the RPC call
                        new CodeInstruction(OpCodes.Nop) { labels = new List<Label> { continueLabel } } // Continue label for the original code path
                    };
                    codes.InsertRange(i - 5, injectedInstructions);

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to RPCA_DonePicking in ReplaceCards.");
                    i += injectedInstructions.Length;
                }
            }

            return codes;
        }

        [HarmonyPatch(nameof(CardChoice.DoPick), MethodType.Enumerator)]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> DoPickTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching PickRoutine to prevent \"CardChoiceVisuals.Hide()\" from getting called in Simultaneous Card Picks Game Mode.");
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo isSimultaneousPickPhaseInProgressMethod = AccessTools.Method(typeof(SimultaneousPicksHandler), nameof(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive));
            MethodInfo IsPlayerNotMeMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(IsPlayerNotMe));
            MethodInfo cardChoiceVisualsHideMethod = AccessTools.Method(typeof(CardChoiceVisuals), nameof(CardChoiceVisuals.Hide));
            MethodInfo toggleCardChoiceVisualsIfIBeingSpectatedMethod = AccessTools.Method(typeof(CardChoicePatch), nameof(CardChoicePatch.ToggleCardChoiceVisualsIfIBeingSpectated));

            FieldInfo cardChoiceInstanceField = AccessTools.Field(typeof(CardChoiceVisuals), "instance");
            FieldInfo picketIDToSetField = Utils.FindNestedField(AccessTools.Method(typeof(CardChoice), nameof(CardChoice.DoPick)), "picketIDToSet");

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].Calls(cardChoiceVisualsHideMethod) && codes[i - 1].LoadsField(cardChoiceInstanceField)) {
                    var skipLabel = il.DefineLabel();
                    codes[i + 1].labels.Add(skipLabel);
                    var continueLabel = il.DefineLabel();
                    codes[i - 1].labels.Add(continueLabel);

                    CodeInstruction[] injectedInstructions = new CodeInstruction[] {
                        // Check if Simultaneous Card Picks is active, else continue
                        new CodeInstruction(OpCodes.Call, isSimultaneousPickPhaseInProgressMethod), // Call IsSimultaneousCardPicksGameMode
                        new CodeInstruction(OpCodes.Brfalse, continueLabel), // if false -> continue

                        // Toggle CardChoiceVisuals if I am being spectated
                        new CodeInstruction(OpCodes.Ldarg_0), // Load this (CardChoice instance)
                        new CodeInstruction(OpCodes.Ldfld, picketIDToSetField), // Load picketIDToSet
                        new CodeInstruction(OpCodes.Ldc_I4_0), // Load false
                        new CodeInstruction(OpCodes.Call, toggleCardChoiceVisualsIfIBeingSpectatedMethod), // Call RPCS_ToggleCardChoiceVisualsIfIBeingSpectated

                        // Check if player is not me, else skip CardChoiceVisuals.Hide()
                        new CodeInstruction(OpCodes.Ldarg_0), // Load this (CardChoice instance)
                        new CodeInstruction(OpCodes.Ldfld, picketIDToSetField), // Load picketIDToSet
                        new CodeInstruction(OpCodes.Call, IsPlayerNotMeMethod), // Call IsPlayerIsMyAndInSimultaneousPicksGameMode
                        new CodeInstruction(OpCodes.Brtrue, skipLabel), // if true -> skip CardChoiceVisuals.Hide()
                    };
                    codes.InsertRange(i - 1, injectedInstructions);

                    UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patched a call to CardChoiceVisuals.Hide() in PickRoutine.");
                    i += injectedInstructions.Length;
                }
            }
            return codes;
        }

        [HarmonyPatch(nameof(CardChoice.DoPick))]
        [HarmonyPrefix]
        public static void DoPickPrefix(CardChoice __instance, int picketIDToSet) {
            UnityEngine.Debug.Log("SimultaneousCardPicksGM: Patching PickRoutine to set pickrID in Simultaneous Card Picks Game Mode.");

            Player player = PlayerManager.instance.players.Find(p => p.playerID == picketIDToSet);
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive() && player != null && player.data.view.IsMine) {
                OutOfPickPhaseDisplay.Instance.SetActive(false);
            }

            NetworkingManager.RPC(
                typeof(SimultaneousPicksHandler),
                nameof(SimultaneousPicksHandler.RPCS_SetCurrentPickingPlayer),
                picketIDToSet
            );

            if(SimultaneousPickPhaseSpectatingHandler.Instance.IsSpectating && PlayerToClientMates[player].Contains(SimultaneousPickPhaseSpectatingHandler.Instance.SpectatedPlayer)) {
                SimultaneousPickPhaseSpectatingHandler.Instance.SetSpectatedPlayer(player);
            }
        }



        [HarmonyPatch("RPCA_DoEndPick")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void RPCA_DoEndPickPrefix(CardChoice __instance, int pickId) {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == pickId);
            if(player != null) {
                LastPickerPlayer = player;
            }

            NetworkingManager.RPC(
                typeof(SimultaneousPicksHandler),
                nameof(SimultaneousPicksHandler.RPCS_RemoveCurrentPickingPlayer),
                pickId
            );
        }

        private static void DesroyCards(int[] cardIDs) {
            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", cardIDs);
            foreach(GameObject card in cards) {
                GameObject.Destroy(card);
            }
        }

        private static bool IsPlayerInSimCardPicksMode(int playerID) {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == playerID);

            bool checkResult = player == null || player.data.view.IsMine || SimultaneousPickPhaseSpectatingHandler.Instance.SpectatedPlayer == player;
            return checkResult;
        }

        private static void HideCardsFromOtherPlayers(GameObject card, int pickerID) {
            NetworkingManager.RPC_Others(
                typeof(CardChoicePatch),
                nameof(RPCS_HideCards),
                card.GetComponent<PhotonView>().ViewID,
                pickerID
            );
        }

        private static void AddCardToPlayerSpawnedCards(GameObject card, int pickerID) {
            NetworkingManager.RPC(
                typeof(CardChoicePatch),
                nameof(RPCS_AddCardToPlayerSpawnedCards),
                card.GetComponent<PhotonView>().ViewID,
                pickerID
            );
        }

        private static bool IsPlayerNotMe(int playerID) {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == playerID);
            bool checkResult = player != null && !player.data.view.IsMine;
            return checkResult;
        }

        private static bool IsPickerIdIsMe() {
            Player player = PlayerManager.instance.players.Find(p => p.playerID == CardChoice.instance.pickrID);
            return player != null && player.data.view.IsMine;
        }

        private static void RemoveCardToPlayerSpawnedCards(int[] cardIDs, int pickerID) {
            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", cardIDs);
            Player picker = PlayerManager.instance.players.Find(p => p.playerID == pickerID);

            foreach(GameObject card in cards) {
                SimultaneousPicksHandler.Instance.playerSpwnedCards[picker].Remove(card);
            }
        }

        private static void ToggleCardChoiceVisualsIfIBeingSpectated(int playerID, bool isActive) {
            UnityEngine.Debug.Log($"SimultaneousCardPicksGM: Toggling CardChoiceVisuals for playerID {playerID} to {(isActive ? "show" : "hide")} because I am being spectated.");

            NetworkingManager.RPC(
                typeof(SimultaneousPicksHandler),
                nameof(SimultaneousPicksHandler.RPCS_ToggleCardChoiceVisualsIfIBeingSpectated),
                playerID,
                isActive
            );
        }

        [UnboundRPC]
        private static void RPCS_AddCardToPlayerSpawnedCards(int cardID, int pickerID) {
            Player picker = PlayerManager.instance.players.Find(p => p.playerID == pickerID);

            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", new int[] { cardID });
            foreach(GameObject card in cards) {
                if(!SimultaneousPicksHandler.Instance.playerSpwnedCards.ContainsKey(picker)) {
                    SimultaneousPicksHandler.Instance.playerSpwnedCards[picker] = new List<GameObject>();
                }
                SimultaneousPicksHandler.Instance.playerSpwnedCards[picker].Add(card);
            }
        }

        [UnboundRPC]
        private static void RPCS_HideCards(int cardID, int pickerID) {
            Player picker = PlayerManager.instance.players.Find(p => p.playerID == pickerID);
            if(picker == SimultaneousPickPhaseSpectatingHandler.Instance.SpectatedPlayer) {
                return; // Don't hide cards for the spectated player
            }

            List<GameObject> cards = (List<GameObject>)CardChoice.instance.InvokeMethod("CardFromIDs", new int[] { cardID });
            foreach(GameObject card in cards) {
                SimultaneousPicksHandler.Instance.HideCards(new GameObject[] { card });
            }

            Unbound.Instance.ExecuteAfterFrames(10, () => {
                foreach(GameObject card in cards) {
                    SimultaneousPicksHandler.Instance.HideCards(new GameObject[] { card });
                }
            });
        }
    }
}
