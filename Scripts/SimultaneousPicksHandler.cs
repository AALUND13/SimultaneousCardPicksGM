using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;

namespace SimultaneousCardPicksGM {
    public enum SimultaneousPickPlayerState {
        PickStart,
        Picking,
        PickEnd,
        PickingDone
    }

    public class SimultaneousPicksHandler : MonoBehaviour {
        public static SimultaneousPicksHandler Instance { get; private set; }
        public IReadOnlyDictionary<Player, int> PlayerSimultaneousPicksQueue => playeSimultaneousPicksQueue;
        public IReadOnlyDictionary<Player, SimultaneousPickPlayerState> PlayerStates => playerStates;

        public bool IsInSimultaneousPickPhase => isInSimultaneousPickPhase;

        private static bool IsAlreadyOutOfPickPhase = false;
        
        private Dictionary<Player, int> playeSimultaneousPicksQueue = new Dictionary<Player, int>();
        private Dictionary<Player, SimultaneousPickPlayerState> playerStates = new Dictionary<Player, SimultaneousPickPlayerState>();
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
            Player localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
            
            playerStates.Clear();
            foreach(var player in playerPickCounts.Keys) {
                playerStates[player] = SimultaneousPickPlayerState.PickStart;
            }

            foreach(var player in playerPickCounts.Keys) {
                if(!playeSimultaneousPicksQueue.ContainsKey(player)) {
                    playeSimultaneousPicksQueue[player] = 0;
                }
                playeSimultaneousPicksQueue[player] += playerPickCounts[player];
            }

            foreach(var player in new Dictionary<Player, int>(playeSimultaneousPicksQueue)) {
                if(player.Key == null) {
                    playeSimultaneousPicksQueue.Remove(player.Key);
                    continue;
                } else if(player.Value <= 0 && player.Key.data.view.IsMine) {
                    this.ExecuteAfterSeconds(0.5f, () => {
                        OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
                        playerStates[player.Key] = SimultaneousPickPlayerState.PickEnd;
                    });
                }
            }

            WaitForSyncUpCounter = 0;
            IsAlreadyOutOfPickPhase = false;
            isInSimultaneousPickPhase = true;
            playerStates.Clear();

            OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
            yield return WaitForAllPlayersSync();
            OutOfPickPhaseDisplay.Instance.SetActive(false);

            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), localPlayer.playerID, SimultaneousPickPlayerState.Picking);
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
            OutOfPickPhaseDisplay.Instance.SetActive(false);

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), localPlayer.playerID, SimultaneousPickPlayerState.PickingDone);

            OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
            yield return WaitForAllPlayersSync();
            OutOfPickPhaseDisplay.Instance.SetActive(false);
            isInSimultaneousPickPhase = false;

            yield break;
        }

        public void AddPlayerToSimultaneousPickQueue(Player player, int pickCount) {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_AddToSimultaneousPickQueue), player.playerID, pickCount);
        }

        private void Update() {
            if(isInSimultaneousPickPhase && OutOfPickPhaseDisplay.Instance.IsActive) {
                Player localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
                SimultaneousPickPlayerState localPlayerState = SimultaneousPickPlayerState.PickStart;
                if(playerStates.ContainsKey(localPlayer)) 
                    localPlayerState = playerStates[localPlayer];

                Dictionary<Player, int> playersToShow = new Dictionary<Player, int>();
                foreach(var player in playeSimultaneousPicksQueue) {
                    if(player.Key == null) continue;
                    SimultaneousPickPlayerState playerState = SimultaneousPickPlayerState.PickStart;
                    if(playerStates.ContainsKey(player.Key))
                        playerState = playerStates[player.Key];

                    if((int)playerState < (int)localPlayerState) {
                        playersToShow.Add(player.Key, player.Value);
                    }
                }

                OutOfPickPhaseDisplay.Instance.SetText(playersToShow);
            }
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

                OutOfPickPhaseDisplay.Instance.SetActive(1, true);
                NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), player.playerID, SimultaneousPickPlayerState.PickEnd);
            }
        }

        [UnboundRPC]
        private static void RPCS_WaitForAllPlayersSync() {
            Instance.WaitForSyncUpCounter++;
        }

        [UnboundRPC]
        private static void RPCS_SetPlayerState(int playerID, SimultaneousPickPlayerState state) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(Instance.playerStates.ContainsKey(player)) {
                Instance.playerStates[player] = state;
            } else {
                Instance.playerStates.Add(player, state);
            }
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
                Instance.playerStates.Remove(player);
                IsAlreadyOutOfPickPhase = false;
            }
        }
    }
}
