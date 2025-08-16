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
        ReadyToPick,
        Picking,
        FinishedPick,
        PickingDone
    }

    public class SimultaneousPicksHandler : MonoBehaviour {
        public static SimultaneousPicksHandler Instance { get; private set; }
        public IReadOnlyDictionary<Player, int> PlayerSimultaneousPicksQueue => playerSimultaneousPicksQueue;
        public IReadOnlyDictionary<Player, SimultaneousPickPlayerState> PlayerStates => playerPickStates;

        public bool InSimultaneousPickPhase => inSimultaneousPickPhase;

        private static bool hasAlreadyExitedPickPhase = false;
        
        private Dictionary<Player, int> playerSimultaneousPicksQueue = new Dictionary<Player, int>();
        private Dictionary<Player, int> initialPlayerSimultaneousPicksQueue = new Dictionary<Player, int>();
        private Dictionary<Player, SimultaneousPickPlayerState> playerPickStates = new Dictionary<Player, SimultaneousPickPlayerState>();

        private bool inSimultaneousPickPhase = false;
        private int waitForSyncCounter = 0;

        /// <summary>
        /// This is mainly used in transpilers to check if the simultaneous pick phase is active.
        /// </summary>
        public static bool IsSimultaneousPickPhaseActive() {
            return SimultaneousPicksHandler.Instance.InSimultaneousPickPhase;
        }

        /// <summary>
        /// You can use this method to start the simultaneous pick phase for your own game mode.
        /// </summary>
        public IEnumerator StartSimultaneousPickPhase(Dictionary<Player, int> playerPickCounts, Func<IEnumerator> WaitForSyncUp) {
            Player localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
            initialPlayerSimultaneousPicksQueue = new Dictionary<Player, int>(playerPickCounts);
            playerPickStates.Clear();

            foreach(var player in playerPickCounts.Keys) {
                if(!playerSimultaneousPicksQueue.ContainsKey(player)) {
                    playerSimultaneousPicksQueue[player] = 0;
                }
                playerSimultaneousPicksQueue[player] += playerPickCounts[player];
            }

            foreach(var player in new Dictionary<Player, int>(playerSimultaneousPicksQueue)) {
                if(player.Key == null) {
                    playerSimultaneousPicksQueue.Remove(player.Key);
                    continue;
                } else if(player.Value <= 0 
                    && player.Key.data.view.IsMine 
                    && (!initialPlayerSimultaneousPicksQueue.ContainsKey(player.Key) || initialPlayerSimultaneousPicksQueue[player.Key] <= 0)
                ) {
                    NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), player.Key.playerID, SimultaneousPickPlayerState.PickingDone);
                    OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
                }
            }

            waitForSyncCounter = 0;
            hasAlreadyExitedPickPhase = false;
            inSimultaneousPickPhase = true;
            playerPickStates.Clear();

            ToggleOutOfPickDisplayIfPicking(localPlayer, true);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
            yield return WaitForPlayerSync();
            waitForSyncCounter = 0;

            ToggleOutOfPickDisplayIfPicking(localPlayer, false);
            SetPlayerStateIfPicking(localPlayer, SimultaneousPickPlayerState.Picking);

            foreach(var player in playerPickCounts) {
                if(player.Value <= 0) continue;

                yield return WaitForSyncUp;

                if(player.Key.data.view.IsMine) {
                    CardChoiceVisuals.instance.Show(player.Key.playerID, true);
                }

                yield return PickRoutine(player.Key.playerID, PickerType.Player);
            }

            this.ExecuteAfterFrames(10, () => {
                foreach(Player player in playerPickCounts.Keys) {
                    if(player.data.view.IsMine) {
                        CardChoice.instance.pickrID = player.playerID;
                    }
                }
            });

            yield return new WaitForSecondsRealtime(0.1f);
            yield return WaitForSyncUp;
            CardChoiceVisuals.instance.Hide();


            ToggleOutOfPickDisplayIfPicking(localPlayer, false);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);
            SetPlayerStateIfPicking(localPlayer, SimultaneousPickPlayerState.PickingDone);

            ToggleOutOfPickDisplayIfPicking(localPlayer, true);
            yield return WaitForPlayerSync();
            OutOfPickPhaseDisplay.Instance.SetActive(false);
            inSimultaneousPickPhase = false;

            yield break;
        }
        
        private void Update() {
            if(inSimultaneousPickPhase && OutOfPickPhaseDisplay.Instance.IsActive) {
                Player localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
                SimultaneousPickPlayerState localPlayerState = SimultaneousPickPlayerState.ReadyToPick;
                if(playerPickStates.ContainsKey(localPlayer)) 
                    localPlayerState = playerPickStates[localPlayer];

                Dictionary<Player, int> playersToShow = new Dictionary<Player, int>();
                foreach(var player in playerSimultaneousPicksQueue) {
                    if(player.Key == null) continue;
                    SimultaneousPickPlayerState playerState = SimultaneousPickPlayerState.ReadyToPick;
                    if(playerPickStates.ContainsKey(player.Key))
                        playerState = playerPickStates[player.Key];

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



        public void QueuePlayerForAdditionalPicks(Player player, int pickCount) {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_AddToSimultaneousPickQueue), player.playerID, pickCount);
        }

        private void ToggleOutOfPickDisplayIfPicking(Player localPlayer, bool isActive) {
            if(initialPlayerSimultaneousPicksQueue.ContainsKey(localPlayer) && initialPlayerSimultaneousPicksQueue[localPlayer] > 0 && !isActive) {
                OutOfPickPhaseDisplay.Instance.SetActive(false);
            } else if(isActive) {
                OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
            } 
        }

        private void SetPlayerStateIfPicking(Player player, SimultaneousPickPlayerState state) {
            if(initialPlayerSimultaneousPicksQueue.ContainsKey(player) && initialPlayerSimultaneousPicksQueue[player] > 0) {
                NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), player.playerID, state);
            }
        }



        private IEnumerator WaitForPlayerSync() {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_WaitForPlayerSync));

            while(Instance.waitForSyncCounter < PlayerManager.instance.players.Count) {
                yield return null;
            }
        }

        private IEnumerator PickRoutine(int playerID, PickerType pickerType) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player.data.view.IsMine) {
                int PlayerSimultaneousPicks = 1;
                if(player != null && playerSimultaneousPicksQueue.ContainsKey(player)) {
                    PlayerSimultaneousPicks = playerSimultaneousPicksQueue[player];
                }

                while(PlayerSimultaneousPicks > 0) {
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);
                    yield return CardChoice.instance.DoPick(1, playerID, pickerType);
                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

                    PlayerSimultaneousPicks = playerSimultaneousPicksQueue[player];
                    if(PlayerSimultaneousPicks > 0) {
                        PlayerSimultaneousPicks--;
                        NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_ReduceSimultaneousPickCount), playerID);
                    }
                }

                OutOfPickPhaseDisplay.Instance.SetActive(1, true);
                NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), player.playerID, SimultaneousPickPlayerState.FinishedPick);
            }
        }



        [UnboundRPC]
        private static void RPCS_WaitForPlayerSync() {
            Instance.waitForSyncCounter++;
        }

        [UnboundRPC]
        private static void RPCS_SetPlayerState(int playerID, SimultaneousPickPlayerState state) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(Instance.playerPickStates.ContainsKey(player)) {
                Instance.playerPickStates[player] = state;
            } else {
                Instance.playerPickStates.Add(player, state);
            }
        }

        [UnboundRPC]
        private static void RPCS_ReduceSimultaneousPickCount(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(Instance.playerSimultaneousPicksQueue.ContainsKey(player) && Instance.playerSimultaneousPicksQueue[player] > 0) {
                Instance.playerSimultaneousPicksQueue[player]--;
            }
        }

        [UnboundRPC]
        private static void RPCS_AddToSimultaneousPickQueue(int playerID, int pickCount) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;

            if(!Instance.playerSimultaneousPicksQueue.ContainsKey(player)) {
                Instance.playerSimultaneousPicksQueue[player] = 0;
            }
            Instance.playerSimultaneousPicksQueue[player] += pickCount;

            if(Instance.InSimultaneousPickPhase && pickCount > 0 && !hasAlreadyExitedPickPhase) {
                if(player.data.view.IsMine) {
                    CardChoiceVisuals.instance.Show(player.playerID, true);
                }
                Instance.StartCoroutine(Instance.PickRoutine(playerID, PickerType.Player));
                Instance.playerPickStates.Remove(player);
                hasAlreadyExitedPickPhase = false;
            }
        }
    }
}
