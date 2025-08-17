using BepInEx;
using HarmonyLib;
using SimultaneousCardPicksGM.GameModes;
using SimultaneousCardPicksGM.Monobehaviours;
using SimultaneousCardPicksGM.Patches;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnboundLib;
using UnboundLib.GameModes;
using UnityEngine;

namespace SimultaneousCardPicksGM {
    [BepInDependency("pykess.rounds.plugins.moddingutils", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("io.olavim.rounds.rwf", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("ot.dan.rounds.picktimer", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(modId, modName, "1.0.0")]
    [BepInProcess("Rounds.exe")]
    public class SimultaneousCardPicksGM : BaseUnityPlugin {
        private const string modId = "com.aalund13.rounds.simultaneously_cards_picks";
        private const string modName = "Simultaneous Card Picks GM";
        internal const string modInitials = "SCP";
        
        internal static SimultaneousCardPicksGM instance;
        internal static AssetBundle assets;
        internal static Harmony harmony;

        void Awake() {
            instance = this;
            harmony = new Harmony(modId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            assets = Jotunn.Utils.AssetUtils.LoadAssetBundleFromResources("simultaneouscardpicksgm_assets", typeof(SimultaneousCardPicksGM).Assembly);
            gameObject.AddComponent<SimultaneousPicksHandler>();

            GameObject outOfPickPhaseDisplay = Instantiate(assets.LoadAsset<GameObject>("OutOfPickPhaseDisplay"));
            outOfPickPhaseDisplay.GetComponent<OutOfPickPhaseDisplay>().SetActive(false);
            DontDestroyOnLoad(outOfPickPhaseDisplay);

            Debug.Log($"{modName} loaded!");
        }
        void Start() {
            List<BaseUnityPlugin> Plugins = (List<BaseUnityPlugin>)typeof(BepInEx.Bootstrap.Chainloader).GetField("_plugins", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if(Plugins.Exists(plugin => plugin.Info.Metadata.GUID == "ot.dan.rounds.picktimer")) {
                PickTimerPatch.Patch(harmony);
            }
            if(Plugins.Exists(plugin => plugin.Info.Metadata.GUID == "pykess.rounds.plugins.pickncards")) {
                PickNCardsPatch.Patch(harmony);
            }
            BaseUnityPlugin mod = Plugins.FirstOrDefault(plugin => plugin.Info.Metadata.GUID == "com.willuwontu.rounds.gamemodes");
            if(mod != null) {
                WillsWackyGamemodesStartingPickPatch.Patch(harmony, mod.GetType().Assembly);
            }

            GameModeManager.AddHandler<SimultaneousCardPicksGameMode>(SimultaneousCardPicksGameModeHandler.GameModeID, new SimultaneousCardPicksGameModeHandler());

            Debug.Log($"{modName} started!");
        }
    }
}