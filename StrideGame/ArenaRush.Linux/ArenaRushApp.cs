using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Stride.Assets.Effect;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;
using Stride.Shaders.Compiler;

// Desktop build of the game. With ARENARUSH_EFFECT_LOG=<file> it becomes an "effect recorder":
// it flies a camera around the level, records every shader permutation the renderer asks for and
// exits. CI turns that file into EffectCompileLog.sdeffectlog so Android ships precompiled shaders.
var logPath = Environment.GetEnvironmentVariable("ARENARUSH_EFFECT_LOG");
using var game = string.IsNullOrEmpty(logPath) ? new Game() : new EffectRecordingGame(logPath);
game.Run();

internal class EffectRecordingGame : Game
{
    private readonly string path;
    private EffectLogStore store;
    private int recorded;

    public EffectRecordingGame(string path) => this.path = path;

    protected override void BeginRun()
    {
        base.BeginRun();
        store = new EffectLogStore(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
        var field = typeof(EffectSystem).GetField("EffectUsed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("EffectSystem.EffectUsed not found");
        var existing = (Action<EffectCompileRequest, CompilerResults>)field.GetValue(EffectSystem);
        field.SetValue(EffectSystem, existing + Record);
        Script.AddTask(AutoPilot);
    }

    private void Record(EffectCompileRequest request, CompilerResults result)
    {
        lock (store)
        {
            if (store.Contains(request))
                return;
            store[request] = true;
            recorded++;
            Console.WriteLine($"[effects] #{recorded} {request.EffectName}");
        }
    }

    private async Task Wait(double seconds)
    {
        var end = UpdateTime.Total.TotalSeconds + seconds;
        while (UpdateTime.Total.TotalSeconds < end)
            await Script.NextFrame();
    }

    private async Task AutoPilot()
    {
        while (SceneSystem.SceneInstance?.RootScene == null)
            await Script.NextFrame();
        var scene = SceneSystem.SceneInstance.RootScene;

        // 1. the normal player view
        await Wait(10);

        // 2. a free camera orbiting over the whole level
        var playerCamera = scene.Entities.SelectMany(Descendants)
            .Select(e => e.Get<CameraComponent>()).FirstOrDefault(c => c != null);
        var slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId();
        var orbit = new CameraComponent { Slot = slot, FarClipPlane = 400, VerticalFieldOfView = 60 };
        var orbitEntity = new Entity("RecorderCamera") { orbit };
        if (playerCamera != null)
            playerCamera.Enabled = false;
        scene.Entities.Add(orbitEntity);

        var center = new Vector3(0, 0, -14);
        foreach (var (radius, height) in new[] { (28f, 12f), (14f, 4f), (45f, 18f) })
        {
            for (var angle = 0; angle < 360; angle += 30)
            {
                var a = MathUtil.DegreesToRadians(angle);
                var pos = center + new Vector3(MathF.Cos(a) * radius, height, MathF.Sin(a) * radius);
                orbitEntity.Transform.Position = pos;
                orbitEntity.Transform.Rotation = LookAt(pos, center + new Vector3(0, 2, 0));
                await Wait(1.5);
            }
        }

        if (playerCamera != null)
            playerCamera.Enabled = true;
        scene.Entities.Remove(orbitEntity);
        await Wait(5);

        lock (store)
            store.Dispose();
        Console.WriteLine($"[effects] recorded {recorded} effect permutations -> {path}");
        Exit();
    }

    private static System.Collections.Generic.IEnumerable<Entity> Descendants(Entity e)
    {
        yield return e;
        foreach (var c in e.GetChildren())
            foreach (var d in Descendants(c))
                yield return d;
    }

    private static Quaternion LookAt(Vector3 eye, Vector3 target)
    {
        var view = Matrix.LookAtRH(eye, target, Vector3.UnitY);
        view.Invert();
        view.Decompose(out _, out Quaternion rotation, out _);
        return rotation;
    }
}
