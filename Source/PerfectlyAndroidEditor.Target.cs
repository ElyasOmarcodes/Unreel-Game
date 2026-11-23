using UnrealBuildTool;

public class PerfectlyAndroidEditorTarget : TargetRules
{
	public PerfectlyAndroidEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V4;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_3;
		ExtraModuleNames.Add("PerfectlyAndroid");
	}
}
