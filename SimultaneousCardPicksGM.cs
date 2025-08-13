using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnboundLib;
using UnboundLib.GameModes;

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

            Debug.Log($"{modName} loaded!");
        }
        void Start() {
            GameModeManager.AddHandler<SimultaneousCardPicksGameMode>("Simultaneous Card Picks Team Deathmatch", new SimultaneousCardPicksGameModeHandler());

            Debug.Log($"{modName} started!");
        }
    }
}