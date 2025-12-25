using InControl;
using SimultaneousCardPicksGM.Extensions;
using SimultaneousCardPicksGM.Monobehaviours;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using static GeneralInput;

namespace SimultaneousCardPicksGM.Handlers {
    public class SimultaneousPickPhaseSpectatingHandler : MonoBehaviour {
        public const float MAX_NON_PICKING_PLAYER_TIME_FOR_SPECTATING = 10f; // once this amount of time has passed since a player finished picking, they can no longer be spectated

        public static SimultaneousPickPhaseSpectatingHandler Instance { get; private set; }

        public IReadOnlyDictionary<Player, Player> PlayerSpectatingMap => new ReadOnlyDictionary<Player, Player>(playerSpectatingMap);
        public Player[] SpectatablePlayers => PlayerManager.instance.players
            .Where(p => !p.data.view.IsMine && SimultaneousPicksHandler.Instance.GetPlayerState(p) != SimultaneousPickPlayerState.PickingDone && playerSpectatingTimerMap[p] > 0f)
            .OrderBy(p => p.playerID)
            .ToArray();

        public event Action<Player> OnSpectatedPlayerChanged;

        public Player SpectatedPlayer => spectatedPlayer;
        public bool IsSpectating => spectatedPlayer != null;

        private Dictionary<Player, Player> playerSpectatingMap = new Dictionary<Player, Player>();
        private Dictionary<Player, float> playerSpectatingTimerMap = new Dictionary<Player, float>();
        private Player spectatedPlayer;
        private StickDirection lastStickDirection = StickDirection.None;

        public void SetSpectatedPlayer(Player player) {
            Player firstLocalPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);

            if(spectatedPlayer) {
                SimultaneousPicksHandler.Instance.HideCards(SimultaneousPicksHandler.Instance.PlayerSpawnedCards[spectatedPlayer].ToArray());
            }

            if(player != null && SimultaneousPicksHandler.Instance.GetPlayerState(player) != SimultaneousPickPlayerState.PickingDone) {
                spectatedPlayer = player;

                List<GameObject> spawnedCards = SimultaneousPicksHandler.Instance.PlayerSpawnedCards[player].ToList();

                CardChoice.instance.pickrID = player.playerID;
                CardChoiceVisuals.instance.Show(player.playerID, true);
                SimultaneousPicksHandler.Instance.ShowCards(spawnedCards.ToArray());
                CardChoice.instance.SetFieldValue("spawnedCards", spawnedCards);
                NetworkingManager.RPC(typeof(SimultaneousPickPhaseSpectatingHandler), nameof(RPC_SetSpectatedPlayer), firstLocalPlayer.playerID, player.playerID);
                OutOfPickPhaseDisplay.Instance.SetActive(false);
                OnSpectatedPlayerChanged?.Invoke(spectatedPlayer);
            }
        }

        public void StopSpectating(bool showOutOfPickPhaseDisplay = true) {
            Player firstLocalPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);

            if(spectatedPlayer != null) {
                CardChoice.instance.pickrID = -1;
                CardChoiceVisuals.instance.Hide();
                SimultaneousPicksHandler.Instance.HideCards(SimultaneousPicksHandler.Instance.PlayerSpawnedCards[spectatedPlayer].ToArray());
                CardChoice.instance.SetFieldValue("spawnedCards", new List<GameObject>());
                NetworkingManager.RPC(typeof(SimultaneousPickPhaseSpectatingHandler), nameof(RPC_SetSpectatedPlayer), firstLocalPlayer.playerID, -1);
                if(showOutOfPickPhaseDisplay) OutOfPickPhaseDisplay.Instance.SetActive(true);

                spectatedPlayer = null;
                lastStickDirection = StickDirection.None;
                OnSpectatedPlayerChanged?.Invoke(null);
            }
        }

        public void StopSpectating(Player player) {
            if(playerSpectatingMap.TryGetValue(player, out Player spectatedPlayer)) {
                NetworkingManager.RPC(typeof(SimultaneousPickPhaseSpectatingHandler), nameof(RPC_StopPlayerSpectating), player.playerID);
            }
        }

        private void Awake() {
            if(Instance == null) {
                Instance = this;
            } else {
                Destroy(this);
                return;
            }
        }

        private void Update() {
            foreach(var player in PlayerManager.instance.players) {
                if(!playerSpectatingTimerMap.ContainsKey(player)) {
                    playerSpectatingTimerMap[player] = MAX_NON_PICKING_PLAYER_TIME_FOR_SPECTATING;
                }

                if(!SimultaneousPicksHandler.Instance.CurrentPickingPlayers.Contains(player)) {
                    playerSpectatingTimerMap[player] -= Time.deltaTime;
                    if(playerSpectatingTimerMap[player] <= 0f && spectatedPlayer == player) {
                        StopSpectating();
                    }
                } else {
                    playerSpectatingTimerMap[player] = MAX_NON_PICKING_PLAYER_TIME_FOR_SPECTATING;
                }
            }

            Player[] localPlayers = PlayerManager.instance.players.Where(p => p.data.view.IsMine).ToArray();

            if(localPlayers.All(p => IsPlayerStatusDoneOrFinished(p) && !SimultaneousPicksHandler.Instance.CurrentPickingPlayers.Contains(p))) {
                List<PlayerActions> actions = GetActionsFormLocalPlayer();

                StickDirection stickDirection = StickDirection.None;
                for(int i = 0; i < actions.Count; i++) {
                    if(actions[i] == null) {
                        continue;
                    }

                    if(((OneAxisInputControl)actions[i].Right).Value > 0.7f) {
                        stickDirection = StickDirection.Right;
                    }

                    if(((OneAxisInputControl)actions[i].Left).Value > 0.7f) {
                        stickDirection = StickDirection.Left;
                    }

                    if(stickDirection != lastStickDirection) {
                        if(stickDirection == StickDirection.Right) {
                            if(SpectatedPlayer == null) {
                                SetSpectatedPlayer(SpectatablePlayers.FirstOrDefault());
                            } else if(SpectatablePlayers.Length > 1) {
                                int currentIndex = Array.IndexOf(SpectatablePlayers, SpectatedPlayer);
                                Player[] spectatablePlayersShifted = SpectatablePlayers.Shift(-1);
                                SetSpectatedPlayer(spectatablePlayersShifted[currentIndex]);
                            }
                        } else if(stickDirection == StickDirection.Left) {
                            if(SpectatedPlayer == null) {
                                SetSpectatedPlayer(SpectatablePlayers.LastOrDefault());
                            } else if(SpectatablePlayers.Length > 1) {
                                int currentIndex = Array.IndexOf(SpectatablePlayers, SpectatedPlayer);
                                Player[] spectatablePlayersShifted = SpectatablePlayers.Shift(1);
                                SetSpectatedPlayer(spectatablePlayersShifted[currentIndex]);
                            }
                        }


                        lastStickDirection = stickDirection;
                    }
                }
            }
        }

        private bool IsPlayerStatusDoneOrFinished(Player player) {
            SimultaneousPickPlayerState state = SimultaneousPicksHandler.Instance.GetPlayerState(player);
            return state == SimultaneousPickPlayerState.PickingDone || state == SimultaneousPickPlayerState.FinishedPick;
        }

        private List<PlayerActions> GetActionsFormLocalPlayer() {
            Player[] localPlayers = PlayerManager.instance.players.Where(p => p.data.view.IsMine).ToArray();
            List<PlayerActions> actions = new List<PlayerActions>();
            foreach(Player localPlayer in localPlayers) {
                actions.AddRange(PlayerManager.instance.GetActionsFromPlayer(localPlayer.playerID));
            }
            return actions;
        }
        private static void RPC_StopPlayerSpectating(int playerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            if(player != null && Instance.spectatedPlayer == player) {
                Instance.StopSpectating();
            }
        }

        [UnboundRPC]
        private static void RPC_SetSpectatedPlayer(int playerID, int spectatedPlayerID) {
            Player player = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == playerID);
            Player spectatedPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == spectatedPlayerID);

            if(spectatedPlayer != null) {
                Instance.playerSpectatingMap[player] = spectatedPlayer;
            } else if(Instance.playerSpectatingMap.ContainsKey(player)) {
                Instance.playerSpectatingMap.Remove(player);
            }
        }
    }
}
