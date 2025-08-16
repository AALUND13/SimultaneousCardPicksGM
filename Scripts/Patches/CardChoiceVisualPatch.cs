using HarmonyLib;
using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnboundLib;
using UnityEngine;

namespace SimultaneousCardPicksGM.Patches {
    [HarmonyPatch(typeof(CardChoiceVisuals))]
    internal class CardChoiceVisualPatch {
        [HarmonyPatch("SetCurrentSelected")]
        [HarmonyPrefix]
        public static bool SetCurrentSelectedPrefix(CardChoiceVisuals __instance, int toSet) {
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                __instance.InvokeMethod("RPCA_SetCurrentSelected", toSet);
                return false;
            }
            return true;
        }

        [HarmonyPatch("Show")]
        [HarmonyPrefix]
        public static bool ShowPrefix(CardChoiceVisuals __instance, int pickerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == pickerID);
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive() && !player.data.view.IsMine) {
                return false;
            }
            return true;
        }

        [HarmonyPatch("Show")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> ShowTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo IsSimultaneousPickPhaseInProgressMethod = AccessTools.Method(typeof(SimultaneousPicksHandler), nameof(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive));
            MethodInfo PhotonViewRPCMethod = AccessTools.Method(typeof(PhotonView), nameof(PhotonView.RPC), new[] { typeof(string), typeof(RpcTarget), typeof(object[]) });
            MethodInfo SetFaceLocallyMethod = AccessTools.Method(typeof(CardChoiceVisualPatch), nameof(CalledRPCA_SetFaceLocally));
            MethodInfo GetComponentPhotonViewMethod = typeof(Component)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(m => m.Name == nameof(Component.GetComponent) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(PhotonView));

            for(int i = 0; i < codes.Count; i++) {
                if(codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].Calls(GetComponentPhotonViewMethod)) {
                    CodeInstruction rpcInstruction;

                    int offset = 0;
                    for(int j = i + 0; j < codes.Count; j++) {
                        if(codes[j].Calls(PhotonViewRPCMethod)) {
                            offset = j - i;
                            rpcInstruction = codes[j];
                            break;
                        }
                    }

                    var originalLabel = codes[i].labels;

                    var skipLabel = il.DefineLabel();
                    var continueLabel = il.DefineLabel();

                    codes[i + offset + 1].labels.Add(skipLabel);

                    CodeInstruction[] injectedInstructions = new CodeInstruction[] {
                        new CodeInstruction(OpCodes.Call, IsSimultaneousPickPhaseInProgressMethod), // Called IsSimultaneousPickPhaseInProgress
                        new CodeInstruction(OpCodes.Brfalse, continueLabel), // if false -> continue
                        new CodeInstruction(OpCodes.Ldarg_0), // Load 'this' (CardChoiceVisuals instance)
                        new CodeInstruction(OpCodes.Ldloc_0), // Load 'face' (PlayerFace instance)
                        new CodeInstruction(OpCodes.Call, SetFaceLocallyMethod), // Call the method to set face locally
                        new CodeInstruction(OpCodes.Br, skipLabel), // Skip the original RPC call
                        new CodeInstruction(OpCodes.Nop) { labels = new List<Label> { continueLabel } } // Continue here if not in simultaneous pick phase
                    };

                    codes.InsertRange(i, injectedInstructions);

                    i += injectedInstructions.Length; // Adjust index to account for inserted instructions
                }
            }

            return codes;
        }

        private static void CalledRPCA_SetFaceLocally(CardChoiceVisuals instance, PlayerFace face) {
            if(SimultaneousPicksHandler.IsSimultaneousPickPhaseActive()) {
                CharacterCreatorItemEquipper itemEquipper = instance.GetComponent<CharacterCreatorItemEquipper>();
                itemEquipper.InvokeMethod("RPCA_SetFace", face.eyeID, face.eyeOffset, face.mouthID, face.mouthOffset, face.detailID, face.detailOffset, face.detail2ID, face.detail2Offset);
            }
        }
    }
}
