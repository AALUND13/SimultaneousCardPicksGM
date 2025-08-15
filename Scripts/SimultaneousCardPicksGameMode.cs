using RWF.GameModes;
using RWF.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnboundLib;
using UnboundLib.GameModes;
using UnityEngine;
using RWF;
using UnboundLib.Networking;

namespace SimultaneousCardPicksGM {
    public class SimultaneousCardPicksGameMode : RWFGameMode {
        public override IEnumerator DoStartGame() {
            CardBarHandler.instance.Rebuild();
            UIHandler.instance.InvokeMethod("SetNumberOfRounds", (int)GameModeManager.CurrentHandler.Settings["roundsToWinGame"]);
            ArtHandler.instance.NextArt();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookGameStart);

            GameManager.instance.battleOngoing = false;

            UIHandler.instance.ShowJoinGameText("LETS GOO!", PlayerSkinBank.GetPlayerSkinColors(1).winText);
            yield return new WaitForSecondsRealtime(0.25f);
            UIHandler.instance.HideJoinGameText();

            PlayerSpotlight.CancelFade(true);

            PlayerManager.instance.SetPlayersSimulated(false);
            PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);
            MapManager.instance.LoadNextLevel(false, false);
            TimeHandler.instance.DoSpeedUp();

            yield return new WaitForSecondsRealtime(1f);

            yield return StartPickPhase();

            PlayerSpotlight.FadeIn();
            MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
            TimeHandler.instance.DoSpeedUp();
            TimeHandler.instance.StartGame();
            GameManager.instance.battleOngoing = true;
            UIHandler.instance.ShowRoundCounterSmall(teamPoints, teamRounds);
            PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);

            StartCoroutine(DoRoundStart());
        }

        public override IEnumerator RoundTransition(int[] winningTeamIDs) {
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPointEnd);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookRoundEnd);

            int[] winningTeams = GameModeManager.CurrentHandler.GetGameWinners();
            if(winningTeams.Any()) {
                GameOver(winningTeamIDs);
                yield break;
            }

            StartCoroutine(PointVisualizer.instance.DoWinSequence(teamPoints, teamRounds, winningTeamIDs));

            yield return new WaitForSecondsRealtime(1f);
            MapManager.instance.LoadNextLevel(false, false);

            yield return new WaitForSecondsRealtime(1.3f);

            PlayerManager.instance.SetPlayersSimulated(false);
            TimeHandler.instance.DoSpeedUp();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);

            PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);

            yield return StartPickPhase(winningTeamIDs);

            PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);
            PlayerSpotlight.FadeIn();

            TimeHandler.instance.DoSlowDown();
            MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
            PlayerManager.instance.RevivePlayers();

            yield return new WaitForSecondsRealtime(0.3f);

            TimeHandler.instance.DoSpeedUp();
            GameManager.instance.battleOngoing = true;
            isTransitioning = false;
            UIHandler.instance.ShowRoundCounterSmall(teamPoints, teamRounds);

            StartCoroutine(DoRoundStart());
        }


        private IEnumerator StartPickPhase(int[] winningTeamIDs = null) {
            List<Player> pickOrder = PlayerManager.instance.GetPickOrder(winningTeamIDs);

            Dictionary<Player, int> playerPickCounts = new Dictionary<Player, int>();
            foreach(Player player in pickOrder) {
                if(winningTeamIDs == null || !winningTeamIDs.Contains(player.teamID)) {
                    playerPickCounts[player] = 1;
                }
            }

            yield return SimultaneousPicksHandler.Instance.StartSimultaneousPickPhase(playerPickCounts, WaitForSyncUp);
        }
    }
}
