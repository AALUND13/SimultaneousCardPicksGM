using SimultaneousCardPicksGM.Monobehaviours;
using SimultaneousCardPicksGM.Patches;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;

namespace SimultaneousCardPicksGM.Handlers {
    public enum SimultaneousPickPlayerState {
        ReadyToPick,
        Picking,
        FinishedPick,
        PickingDone
    }

    public class SimultaneousPicksHandler : MonoBehaviour {
        public static SimultaneousPicksHandler Instance { get; private set; }
        public IReadOnlyDictionary<Player, int> PlayerSimultaneousPicksQueue => new ReadOnlyDictionary<Player, int>(playerSimultaneousPicksQueue);
        public IReadOnlyDictionary<Player, SimultaneousPickPlayerState> PlayerStates => new ReadOnlyDictionary<Player, SimultaneousPickPlayerState>(playerPickStates);
        public IReadOnlyDictionary<Player, IReadOnlyList<GameObject>> PlayerSpawnedCards => new ReadOnlyDictionary<Player, IReadOnlyList<GameObject>>(playerSpwnedCards
            .ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<GameObject>)kvp.Value));
        public IReadOnlyList<Player> CurrentPickingPlayers => currentPickingPlayers.AsReadOnly();

        public bool InSimultaneousPickPhase => inSimultaneousPickPhase;


        internal Dictionary<Player, List<GameObject>> playerSpwnedCards = new Dictionary<Player, List<GameObject>>();

        private static bool hasAlreadyExitedPickPhase = false;

        private Dictionary<Player, int> playerSimultaneousPicksQueue = new Dictionary<Player, int>();
        private Dictionary<Player, int> initialPlayerSimultaneousPicksQueue = new Dictionary<Player, int>();
        private Dictionary<Player, SimultaneousPickPlayerState> playerPickStates = new Dictionary<Player, SimultaneousPickPlayerState>();
        private List<Player> currentPickingPlayers = new List<Player>();

        private bool inSimultaneousPickPhase = false;
        private int waitForSyncCounter = 0;
        Dictionary<int, int> playerSyncCounters = new Dictionary<int, int>();


        /// <summary>
        /// This is mainly used in transpilers to check if the simultaneous pick phase is active.
        /// </summary>
        public static bool IsSimultaneousPickPhaseActive() {
            return SimultaneousPicksHandler.Instance.InSimultaneousPickPhase;
        }

        /// <summary>
        /// You can use this method to start the simultaneous pick phase for your own game mode.
        /// </summary>
        public IEnumerator StartSimultaneousPickPhase(Dictionary<Player, int> playerPickCounts) {
            Player[] localPlayers = PlayerManager.instance.players.Where(p => p.data.view.IsMine).ToArray();

            initialPlayerSimultaneousPicksQueue = new Dictionary<Player, int>(playerPickCounts);
            playerSpwnedCards.Clear();
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

            hasAlreadyExitedPickPhase = false;
            inSimultaneousPickPhase = true;

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
            ToggleOutOfPickDisplayIfPicking(true);
            yield return WaitForAllPlayerSync();
            ToggleOutOfPickDisplayIfPicking(false);

            SetPlayerStateIfPicking(SimultaneousPickPlayerState.Picking);

            foreach(var player in playerPickCounts) {
                if(player.Value <= 0) continue;

                StartCoroutine(PickRoutine(player.Key.playerID, PickerType.Player));
            }

            // Wait for all players to finish picking
            // Without this could cause desyncs issues if players finish at different times
            while(!playerSimultaneousPicksQueue.All(p => p.Value <= 0)) {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.1f);
            CardChoiceVisuals.instance.Hide();

            ToggleOutOfPickDisplayIfPicking(false);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);
            SetPlayerStateIfPicking(SimultaneousPickPlayerState.PickingDone);
            ToggleOutOfPickDisplayIfPicking(true);

            yield return WaitForAllPlayerSync();
            currentPickingPlayers.Clear();
            SimultaneousPickPhaseSpectatingHandler.Instance.StopSpectating(false);
            OutOfPickPhaseDisplay.Instance.SetActive(false);
            inSimultaneousPickPhase = false;

            yield break;
        }


        internal SimultaneousPickPlayerState GetPlayerState(Player player) {
            if(player == null) throw new ArgumentNullException(nameof(player), "Player cannot be null.");
            if(playerPickStates.TryGetValue(player, out SimultaneousPickPlayerState state)) {
                return state;
            } else {
                return SimultaneousPickPlayerState.ReadyToPick;
            }
        }


        private void Update() {
            if(inSimultaneousPickPhase && OutOfPickPhaseDisplay.Instance.IsActive) {
                Player localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
                SimultaneousPickPlayerState localPlayerState = SimultaneousPickPlayerState.ReadyToPick;

                if(playerPickStates.ContainsKey(localPlayer)) {
                    localPlayerState = playerPickStates[localPlayer];
                }

                Dictionary<Player, int> playersToShow = new Dictionary<Player, int>();
                foreach(var player in playerSimultaneousPicksQueue) {
                    if(player.Key == null) continue;
                    SimultaneousPickPlayerState playerState = SimultaneousPickPlayerState.ReadyToPick;

                    if(playerPickStates.ContainsKey(player.Key)) {
                        playerState = playerPickStates[player.Key];
                    }
                    if((int)playerState < (int)localPlayerState) {
                        playersToShow.Add(player.Key, player.Value);
                    }
                }

                OutOfPickPhaseDisplay.Instance.SetText(playersToShow);
            }
        }

        internal void HideCards(GameObject[] cards) {
            if(cards == null || cards.Length == 0) return;

            foreach(GameObject card in cards) {
                if(card == null) continue;

                GameObject cardBase = card.GetComponentInChildren<CardInfoDisplayer>().gameObject;
                foreach(Transform child in cardBase.transform) {
                    child.gameObject.SetActive(false);
                }
            }
        }

        internal void ShowCards(GameObject[] cards) {
            if(cards == null || cards.Length == 0) return;
            foreach(GameObject card in cards) {
                GameObject cardBase = card.GetComponentInChildren<CardInfoDisplayer>().gameObject;
                foreach(Transform child in cardBase.transform) {
                    child.gameObject.SetActive(true);
                }

                GeneralParticleSystem[] particles = card.GetComponentsInChildren<GeneralParticleSystem>();
                foreach(GeneralParticleSystem particle in particles) {
                    particle.Play();
                }

                if(CardInfoPatch.WasCardSelected(card)) {
                    card.GetComponent<CardInfo>().RPCA_ChangeSelected(true);
                    card.GetComponent<CardInfo>().RPCA_ChangeSelected(false);

                    card.GetComponentInChildren<CurveAnimation>().PlayIn();
                }
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

        private void ToggleOutOfPickDisplayIfPicking(bool isActive) {
            Player[] localPlayers = PlayerManager.instance.players.Where(p => p.data.view.IsMine).ToArray();

            foreach(Player localPlayer in localPlayers) {
                if(initialPlayerSimultaneousPicksQueue.ContainsKey(localPlayer) && initialPlayerSimultaneousPicksQueue[localPlayer] > 0 && !isActive) {
                    OutOfPickPhaseDisplay.Instance.SetActive(false);
                    break;
                } else if(isActive) {
                    OutOfPickPhaseDisplay.Instance.SetActive(0.5f, true);
                    break;
                }
            }
        }

        private void SetPlayerStateIfPicking(SimultaneousPickPlayerState state) {
            Player[] localPlayers = PlayerManager.instance.players.Where(p => p.data.view.IsMine).ToArray();
            foreach(Player localPlayer in localPlayers) {
                if(initialPlayerSimultaneousPicksQueue.ContainsKey(localPlayer) && initialPlayerSimultaneousPicksQueue[localPlayer] > 0) {
                    NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_SetPlayerState), localPlayer.playerID, state);
                }
            }
        }

        private IEnumerator WaitForPlayerSync(int playerID) {
            if(!Instance.playerSyncCounters.ContainsKey(playerID)) {
                Instance.playerSyncCounters[playerID] = 0;
            }

            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_WaitForPlayerToSync), playerID);
            while(Instance.playerSyncCounters[playerID] < PlayerManager.instance.players.Count) {
                yield return null;
            }

            if(Instance.playerSyncCounters.ContainsKey(playerID)) {
                Instance.playerSyncCounters[playerID] = 0;
            }
        }

        private IEnumerator WaitForAllPlayerSync() {
            NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_WaitForAllPlayersToSync));

            while(Instance.waitForSyncCounter < PlayerManager.instance.players.Count) {
                yield return null;
            }

            waitForSyncCounter = 0;
        }

        private IEnumerator PickRoutine(int playerID, PickerType pickerType) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            yield return GameModeManager.TriggerHook(SimultaneousPicksHooks.OnSimultaneousPickStart);
            if(player.data.view.IsMine) {

                int PlayerSimultaneousPicks = 1;
                if(player != null && playerSimultaneousPicksQueue.ContainsKey(player)) {
                    PlayerSimultaneousPicks = playerSimultaneousPicksQueue[player];
                }

                while(PlayerSimultaneousPicks > 0) {
                    CardChoiceVisuals.instance.Show(player.playerID, true);
                    NetworkingManager.RPC(typeof(SimultaneousPicksHandler), nameof(RPCS_ToggleCardChoiceVisualsIfIBeingSpectated), player.playerID, true);

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
            yield return WaitForPlayerSync(playerID);
            yield return GameModeManager.TriggerHook(SimultaneousPicksHooks.OnSimultaneousPickEnd);
        }

        #region 
        [UnboundRPC]
        internal static void RPCS_ToggleCardChoiceVisualsIfIBeingSpectated(int playerID, bool isActive) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;

            if(SimultaneousPickPhaseSpectatingHandler.Instance.SpectatedPlayer == player) {
                if(isActive) {
                    CardChoiceVisuals.instance.Show(playerID, true);
                } else {
                    CardChoiceVisuals.instance.Hide();
                }
            }
        }

        [UnboundRPC]
        internal static void RPCS_SetCurrentPickingPlayer(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(!Instance.currentPickingPlayers.Contains(player)) {
                Instance.currentPickingPlayers.Add(player);
            }
        }

        [UnboundRPC]
        internal static void RPCS_RemoveCurrentPickingPlayer(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;
            if(Instance.currentPickingPlayers.Contains(player)) {
                Instance.currentPickingPlayers.Remove(player);
            }
        }

        [UnboundRPC]
        private static void RPCS_WaitForAllPlayersToSync() {
            Instance.waitForSyncCounter++;
        }

        [UnboundRPC]
        private static void RPCS_WaitForPlayerToSync(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player == null) return;

            if(!Instance.playerSyncCounters.ContainsKey(playerID)) {
                Instance.playerSyncCounters[playerID] = 0;
            }
            Instance.playerSyncCounters[playerID]++;
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

            bool previouslyInQueue = Instance.playerSimultaneousPicksQueue.ContainsKey(player) && Instance.playerSimultaneousPicksQueue[player] > 0;

            if(!Instance.playerSimultaneousPicksQueue.ContainsKey(player)) {
                Instance.playerSimultaneousPicksQueue[player] = 0;
            }
            Instance.playerSimultaneousPicksQueue[player] += pickCount;

            if(Instance.InSimultaneousPickPhase && pickCount > 0 && !hasAlreadyExitedPickPhase && !previouslyInQueue) {
                Instance.StartCoroutine(Instance.PickRoutine(playerID, PickerType.Player));
                Instance.playerPickStates.Remove(player);
                hasAlreadyExitedPickPhase = false;
            }
        }
        #endregion
    }
}
