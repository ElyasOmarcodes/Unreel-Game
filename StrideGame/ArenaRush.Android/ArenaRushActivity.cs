using System;
using Android.App;
using Android.Content.PM;
using Android.OS;

using Stride.Engine;
using Stride.Starter;

namespace ArenaRush.Android;

[Activity(MainLauncher = true,
          Label = "Arena Rush",
          ScreenOrientation = ScreenOrientation.SensorLandscape,
          Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
          ConfigurationChanges = ConfigChanges.UiMode | ConfigChanges.Orientation | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard |
                                 ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.Density)]
public class ArenaRushActivity : StrideActivity
{
    protected Game Game;

    protected override void OnCreate(Bundle bundle)
    {
        // Make any crash visible in logcat (tag "ArenaRush").
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            global::Android.Util.Log.Error("ArenaRush", "Unhandled exception: " + e.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            global::Android.Util.Log.Error("ArenaRush", "Unobserved task exception: " + e.Exception);
        base.OnCreate(bundle);
    }

    protected override void OnRun()
    {
        base.OnRun();

        try
        {
            Game = new Game();
            Game.Run(GameContext);
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("ArenaRush", "Game.Run failed: " + e);
            throw;
        }
    }

    protected override void OnDestroy()
    {
        try
        {
            Game?.Dispose();
            base.OnDestroy();
        }
        catch (Exception e)
        {
            // Android can destroy the activity before Stride has started (e.g. a configuration change at launch)
            global::Android.Util.Log.Warn("ArenaRush", "OnDestroy: " + e.Message);
        }
    }
}
