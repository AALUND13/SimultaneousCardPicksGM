using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;

namespace SimultaneousCardPicksGM {
    public class SimultaneousPicksHandler : MonoBehaviour {
        public static SimultaneousPicksHandler Instance { get; private set; }
        public IReadOnlyDictionary<Player, int> PlayerSimultaneousPicksQueue => playeSimultaneousPicksQueue;
        public bool IsInSimultaneousPickPhase => isInSimultaneousPickPhase;

        private static bool IsAlreadyOutOfPickPhase = false;
        
        private Dictionary<Player, int> playeSimultaneousPicksQueue = new Dictionary<Player, int>();
        private bool isInSimultaneousPickPhase = false;
        private int WaitForSyncUpCounter = 0;

        /// <summary>
        /// This is mainly used in transpilers to check if the simultaneous pick phase is active.
        /// </summary>
        public static bool IsSimultaneousPickPhaseInProgress() {
            return SimultaneousPicksHandler.Instance.IsInSimultaneousPickPhase;
        }

        /// <summary>
        /// You can use this method to start the simultaneous pick phase for your own game mode.
        /// </summary>
        public IEnumerator StartSimultaneousPickPhase(Dictionary<Player, int> playerPickCounts, Func<IEnumerator> WaitForSyncUp) {
            foreach(var player in playerPickCounts.Keys) {
                if(!playeSimultaneousPicksQueue.ContainsKey(player)) {
                    playeSimultaneousPicksQueue[player] = 0;
                }
                playeSimultaneousPicksQueue[player] += playerPickCounts[player];
            }

            WaitForSyncUpCounter = 0;
            IsAlreadyOutOfPickPhase = false;
            isInSimultaneousPickPhase = true;
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
            yield return WaitForAllPlayersSync();

            foreach(var player in playerPickCounts) {
                if(player.Value <= 0) continue;

                yield return WaitForSyncUp;

                if(player.Key.data.view.IsMine) {
                    CardChoiceVisuals.instance.Show(player.Key.playerID, true);
                }

                StartCoroutine(DoPick(player.Key.playerID, PickerType.Player));
            }

            this.ExecuteAfterFrames(10, () => {
                foreach(Player player in playerPickCounts.Keys) {
                    if(player.data.view.IsMine) {
                        CardChoice.instance.pickrID = player.playerID;
                    }
                }
            });

            while(!playeSimultaneousPicksQueue.All(p => p.Value <= 0)) {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.1f);
            yield return WaitForSyncUp;
            CardChoiceVisuals.instance.Hide();

            WaitForSyncUpCounter = 0;
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);
            yield return WaitForAllPlayersSync();
            isInSimultaneousPickPhase = false;

            yield break;
        }

        public void AddPlayerToSimultaneousPickQueue(Player player, int pickCount) {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_AddToSimultaneousPickQueue), player.playerID, pickCount);
        }

        private void Awake() {
            if(Instance != null) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }
        }

        private static IEnumerator WaitForAllPlayersSync() {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_WaitForAllPlayersSync));

            while(Instance.WaitForSyncUpCounter < PlayerManager.instance.players.Count) {
                yield return null;
            }
        }

        private IEnumerator DoPick(int playerID, PickerType pickerType) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player.data.view.IsMine) {
                int PlayerSimultaneousPicks = 1;
                if(player != null && playeSimultaneousPicksQueue.ContainsKey(player)) {
                    PlayerSimultaneousPicks = playeSimultaneousPicksQueue[player];
                }

                while(PlayerSimultaneousPicks > 0) {
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);
                    yield return CardChoice.instance.DoPick(1, playerID, pickerType);
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

                    PlayerSimultaneousPicks = playeSimultaneousPicksQueue[player];
                    if(PlayerSimultaneousPicks > 0) {
                        PlayerSimultaneousPicks--;
                        NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_ReduceSimultaneousPickCount), playerID);
                    }
                }
            }
        }

        [UnboundRPC]
        private static void RPCS_WaitForAllPlayersSync() {
            Instance.WaitForSyncUpCounter++;
        }

        [UnboundRPC]
        private static void RPCS_ReduceSimultaneousPickCount(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(Instance.playeSimultaneousPicksQueue.ContainsKey(player) && Instance.playeSimultaneousPicksQueue[player] > 0) {
                Instance.playeSimultaneousPicksQueue[player]--;
            }
        }

        [UnboundRPC]
        private static void RPCS_AddToSimultaneousPickQueue(int playerID, int pickCount) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;

            if(!Instance.playeSimultaneousPicksQueue.ContainsKey(player)) {
                Instance.playeSimultaneousPicksQueue[player] = 0;
            }
            Instance.playeSimultaneousPicksQueue[player] += pickCount;

            if(Instance.IsInSimultaneousPickPhase && pickCount > 0 && !IsAlreadyOutOfPickPhase) {
                if(player.data.view.IsMine) {
                    CardChoiceVisuals.instance.Show(player.playerID, true);
                }
                Instance.StartCoroutine(Instance.DoPick(playerID, PickerType.Player));
                IsAlreadyOutOfPickPhase = false;
            }
        }
    }
}
