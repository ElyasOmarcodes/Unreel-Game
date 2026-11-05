using UnrealBuildTool;

public class PerfectlyAndroidTarget : TargetRules
{
	public PerfectlyAndroidTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V4;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_3;
		ExtraModuleNames.Add("PerfectlyAndroid");
	}
}
