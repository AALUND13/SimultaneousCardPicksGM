using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnboundLib;
using UnboundLib.GameModes;
using SimultaneousCardPicksGM.GameModes;

namespace SimultaneousCardPicksGM {
    [BepInDependency("pykess.rounds.plugins.moddingutils", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("io.olavim.rounds.rwf", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(modId, modName, "1.0.0")]
    [BepInProcess("Rounds.exe")]
    public class SimultaneousCardPicksGM : BaseUnityPlugin {
        private const string modId = "com.aalund13.rounds.simultaneously_cards_picks";
        private const string modName = "Simultaneous Card Picks GM";
        internal const string modInitials = "SCP";
        
        internal static SimultaneousCardPicksGM instance;
        internal static AssetBundle assets;
        
        void Awake() {
            instance = this;
            new Harmony(modId).PatchAll();

            assets = Jotunn.Utils.AssetUtils.LoadAssetBundleFromResources("simultaneouscardpicksgm_assets", typeof(SimultaneousCardPicksGM).Assembly);
            gameObject.AddComponent<SimultaneousPicksHandler>();

            GameObject outOfPickPhaseDisplay = Instantiate(assets.LoadAsset<GameObject>("OutOfPickPhaseDisplay"));
            outOfPickPhaseDisplay.GetComponent<OutOfPickPhaseDisplay>().SetActive(false);
            DontDestroyOnLoad(outOfPickPhaseDisplay);

            Debug.Log($"{modName} loaded!");
        }
        void Start() {
            GameModeManager.AddHandler<SimultaneousCardPicksGameMode>(SimultaneousCardPicksGameModeHandler.GameModeID, new SimultaneousCardPicksGameModeHandler());

            Debug.Log($"{modName} started!");
        }
    }
}