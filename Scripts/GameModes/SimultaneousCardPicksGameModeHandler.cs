using RWF.GameModes;

namespace SimultaneousCardPicksGM.GameModes {
    public class SimultaneousCardPicksGameModeHandler : RWFGameModeHandler<SimultaneousCardPicksGameMode> {
        internal const string GameModeName = "Simultaneous Picks Team Deathmatch";
        internal const string GameModeID = "Simultaneous Picks Team Deathmatch";

        public override bool OnlineOnly => true;

        public SimultaneousCardPicksGameModeHandler() : base(
            name: GameModeName,
            gameModeId: GameModeID,
            allowTeams: true,
            pointsToWinRound: 2,
            roundsToWinGame: 5,
            // null values mean RWF's instance values
            playersRequiredToStartGame: null,
            maxPlayers: null,
            maxTeams: null,
            maxClients: null,
            description: "Simultaneous Card Picks Team Deathmatch is a game mode where players pick cards simultaneously, Each player selects their cards at the same time.\n<color=red>Some cards may break in this gamemode.</color>",
            videoURL: "https://github.com/olavim/RoundsWithFriends/raw/main/Media/TeamDeathmatch.mp4"
            ) { }
    }
}
