using SimultaneousCardPicksGM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace SimultaneousCardPicksGM.Monobehaviours {
    internal class OutOfPickPhaseDisplay : MonoBehaviour {
        public static OutOfPickPhaseDisplay Instance { get; private set; }

        [TextArea(3, 15)]
        public string OutOfPickPhaseText = "please wait for all players to finish picking cards.\n\n{0}";
        public TextMeshProUGUI OutOfPickPhaseTextMesh;
        public CanvasGroup CanvasGroup;

        public bool IsActive => active;

        private bool active = false;

        public void SetActive(float duration, bool isActive) {
            StopAllCoroutines();
            StartCoroutine(SetActiveCoroutine(duration, isActive));
        }

        public void SetActive(bool isActive) {
            StopAllCoroutines();

            active = isActive;
            if (CanvasGroup != null) {
                CanvasGroup.alpha = isActive ? 1f : 0f;
                CanvasGroup.interactable = isActive;
                CanvasGroup.blocksRaycasts = isActive;
            }
        }

        public void SetText(Dictionary<Player, int> playerPickCounts) {
            StringBuilder PlayerList = new StringBuilder();

            foreach(var player in playerPickCounts) {

                string PickLeft = $"Picks Left: {player.Value}";
                if(player.Value <= 0) PickLeft = "In Extra Picks";

                PlayerList.AppendLine($"<b><color=#ffff>{GetPlayerNickname(player.Key)}</b> | </color>{PickLeft}");
            }

            if(OutOfPickPhaseTextMesh != null) {
                OutOfPickPhaseTextMesh.text = string.Format(OutOfPickPhaseText, PlayerList.ToString());
            }
        }

        private void Awake() {
            if (Instance == null) {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            } else {
                Destroy(gameObject);
            }
        }

        private string GetPlayerNickname(Player player) {
            return player != null ? player.data.view.Owner.NickName : "Unknown Player";
        }

        private IEnumerator SetActiveCoroutine(float duration, bool isActive) {
            yield return new WaitForSecondsRealtime(duration);
            SetActive(isActive);
            yield break;
        }
    }
}
