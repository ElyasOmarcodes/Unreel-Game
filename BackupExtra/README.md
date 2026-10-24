# BackupExtra

Parts of Agent Warfare kept out of the game for later use. Nothing here is built into the APK.

## Downtown/

The Downtown city map (Quaternius Downtown City kit, enterable towers, metro under the south street).
`DowntownBuilder.cs` is the old `UnityGame/Assets/Editor/MapBuilder.cs` as it was when the map was
taken out (it also built the main scene then).

To bring the map back:

1. Copy the map methods from `DowntownBuilder.cs` into a `DowntownBuilder` class under
   `UnityGame/Assets/Editor/`, returning a `MapLayout` like the other builders.
2. In `MapBuilder.BuildScene` build it first and put it first in `world.Layouts` and `names`
   (`"Map_Downtown"`).
3. Put `"DOWNTOWN - DAY", "DOWNTOWN - SUNSET", "DOWNTOWN - NIGHT"` first in `MatchConfig.MapNames`
   (`Session.cs`), raise `Session.Version`, and add 3 to the map numbers in `World.cs` and `AutoTest.cs`.

The city kit itself is still downloaded by `.github/scripts/fetch-assets.sh` (into
`Assets/ThirdParty/City`); unused assets are not packed into the APK.
