using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The player's Mixamo animations (Rifle 8-Way Locomotion Pack: idle, walk, run and sprint in eight directions,
/// crouch, jump, death) on the game's skeleton. Models/Animations/mixamo_clips.txt holds, per clip and frame,
/// the turn of every bone from the T-pose (made in Blender by Tools/retarget.py from the Mixamo files): here
/// each turn is laid on the same bone of the agent's own T-pose (its bind pose) and kept as the bone's local
/// rotation in an animation clip of the agent; the hips also move as they did. The clips take the place of
/// the Quaternius ones of the same use (AssetSetup.Clip asks here first).
/// </summary>
public static class MixamoClips
{
    private static string File => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Models", "Animations", "mixamo_clips.txt"));
    private const string Folder = AssetSetup.Generated + "/Mixamo";

    /// <summary>The game's clip names and the Mixamo clip that plays each, with whether it loops.</summary>
    public static readonly Dictionary<string, (string mixamo, bool loop)> Uses = new Dictionary<string, (string, bool)>
    {
        { "Idle_Loop", ("idle", true) },
        { "Walk_Fwd_Loop", ("walk_forward", true) }, { "Walk_Bwd_Loop", ("walk_backward", true) },
        { "Walk_L_Loop", ("walk_left", true) }, { "Walk_R_Loop", ("walk_right", true) },
        { "Walk_Fwd_L_Loop", ("walk_forward_left", true) }, { "Walk_Fwd_R_Loop", ("walk_forward_right", true) },
        { "Walk_Bwd_L_Loop", ("walk_backward_left", true) }, { "Walk_Bwd_R_Loop", ("walk_backward_right", true) },
        { "Jog_Fwd_Loop", ("run_forward", true) }, { "Jog_Bwd_Loop", ("run_backward", true) },
        { "Jog_Left_Loop", ("run_left", true) }, { "Jog_Right_Loop", ("run_right", true) },
        { "Jog_Fwd_L_Loop", ("run_forward_left", true) }, { "Jog_Fwd_R_Loop", ("run_forward_right", true) },
        { "Jog_Bwd_L_Loop", ("run_backward_left", true) }, { "Jog_Bwd_R_Loop", ("run_backward_right", true) },
        { "Sprint_Loop", ("sprint_forward", true) },
        { "Crouch_Idle_Loop", ("idle_crouching", true) },
        { "Crouch_Fwd_Loop", ("walk_crouching_forward", true) }, { "Crouch_Bwd_Loop", ("walk_crouching_backward", true) },
        { "Crouch_Left_Loop", ("walk_crouching_left", true) }, { "Crouch_Right_Loop", ("walk_crouching_right", true) },
        { "Crouch_Fwd_L_Loop", ("walk_crouching_forward_left", true) }, { "Crouch_Fwd_R_Loop", ("walk_crouching_forward_right", true) },
        { "Crouch_Bwd_L_Loop", ("walk_crouching_backward_left", true) }, { "Crouch_Bwd_R_Loop", ("walk_crouching_backward_right", true) },
        { "Jump_Loop", ("jump_loop", true) },
        { "Death01", ("death_from_the_front", false) },
    };

    private static Dictionary<string, AnimationClip> built;
    private static readonly Dictionary<string, float> speeds = new Dictionary<string, float>();

    /// <summary>The Mixamo clip for a game clip name (null: none, the Quaternius one plays).</summary>
    public static AnimationClip Get(string name) => built != null && built.TryGetValue(name, out var c) ? c : null;

    /// <summary>How fast the clip's body walks over the ground (metres a second, 0 standing).</summary>
    public static float Speed(string name) => Uses.TryGetValue(name, out var u) && speeds.TryGetValue(u.mixamo, out float s) ? s : 0f;

    private class Clip
    {
        public string Name;
        public float Fps, Speed;
        public string[] Bones;
        public readonly List<float[]> Frames = new List<float[]>();
    }

    private static List<Clip> Read(out float ualHips)
    {
        var inv = CultureInfo.InvariantCulture;
        var clips = new List<Clip>();
        ualHips = 0.9167f;
        Clip c = null;
        foreach (var line in System.IO.File.ReadLines(File))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var t = line.Split(' ');
            if (t[0] == "ual_hips") ualHips = float.Parse(t[1], inv);
            else if (t[0] == "clip") { c = new Clip { Name = t[1], Fps = float.Parse(t[2], inv), Speed = float.Parse(t[3], inv) }; clips.Add(c); }
            else if (t[0] == "bones" && c != null) c.Bones = t.Skip(1).ToArray();
            else if (c != null) c.Frames.Add(t.Select(x => float.Parse(x, inv)).ToArray());
        }
        return clips;
    }

    /// <summary>Make the clips for the agent's model (its bind pose is the T-pose the turns start from).</summary>
    public static void Build(string modelPath)
    {
        built = new Dictionary<string, AnimationClip>();
        if (!System.IO.File.Exists(File)) { Debug.LogWarning("[Setup] no Mixamo animations: " + File); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) return;
        AssetSetup.Folder(Folder);
        var clips = Read(out float ualHips);
        var go = (GameObject)Object.Instantiate(model);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        try
        {
            var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
            var bones = smr.bones;
            var bind = smr.sharedMesh.bindposes;
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) byName[bones[i].name.ToLowerInvariant()] = i;
            // every bone's world turn in the T-pose (bind pose), and the hips' place
            Quaternion RestRot(int i)
            {
                var m = smr.transform.localToWorldMatrix * bind[i].inverse;
                return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
            }
            Vector3 RestPos(int i) => (smr.transform.localToWorldMatrix * bind[i].inverse).GetColumn(3);
            if (!byName.TryGetValue("pelvis", out int pelvis) || !byName.TryGetValue("foot_l", out int footL) || !byName.TryGetValue("ball_l", out int ballL))
            { Debug.LogWarning("[Setup] Mixamo animations: the model has no pelvis / foot bones"); return; }
            // the character's own frame in the model: forward where the toes point, up the world's up
            var toes = RestPos(ballL) - RestPos(footL); toes.y = 0;
            var frame = Quaternion.LookRotation(toes.sqrMagnitude > 1e-6f ? toes.normalized : Vector3.forward, Vector3.up);
            float floor = Mathf.Min(RestPos(footL).y, RestPos(ballL).y);
            float hipsScale = (RestPos(pelvis).y - floor) / Mathf.Max(0.1f, ualHips);
            var restPelvis = RestPos(pelvis);
            int made = 0;
            foreach (var clip in clips)
            {
                speeds[clip.Name] = clip.Speed * hipsScale;
                var game = Uses.Where(u => u.Value.mixamo == clip.Name).Select(u => u.Key).ToList();
                if (game.Count == 0 || clip.Bones == null || clip.Frames.Count == 0) continue;
                var index = clip.Bones.Select(b => byName.TryGetValue(b.ToLowerInvariant(), out int i) ? i : -1).ToArray();
                var targets = clip.Bones.Select((b, k) => index[k] >= 0 ? bones[index[k]] : null).ToArray();
                // parents before children
                int Depth(Transform t) { int d = 0; for (; t != null; t = t.parent) d++; return d; }
                var order = Enumerable.Range(0, clip.Bones.Length).Where(k => targets[k] != null).OrderBy(k => Depth(targets[k])).ToArray();
                var curves = new Dictionary<int, AnimationCurve[]>();
                foreach (int k in order) curves[k] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var hips = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var last = new Dictionary<int, Quaternion>();
                for (int f = 0; f < clip.Frames.Count; f++)
                {
                    float time = f / clip.Fps;
                    var row = clip.Frames[f];
                    var world = new Dictionary<Transform, Quaternion>();
                    foreach (int k in order)
                    {
                        var turn = new Quaternion(row[k * 4 + 1], row[k * 4 + 2], row[k * 4 + 3], row[k * 4]);
                        turn = frame * turn * Quaternion.Inverse(frame);
                        var w = turn * RestRot(index[k]);
                        var t = targets[k];
                        world[t] = w;
                        var parent = t.parent;
                        var parentWorld = parent != null && world.TryGetValue(parent, out var pw) ? pw : parent != null ? parent.rotation : Quaternion.identity;
                        var local = Quaternion.Inverse(parentWorld) * w;
                        // the shorter way from the frame before (no flips between keys)
                        if (last.TryGetValue(k, out var before) && Quaternion.Dot(before, local) < 0) local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
                        last[k] = local;
                        curves[k][0].AddKey(time, local.x); curves[k][1].AddKey(time, local.y);
                        curves[k][2].AddKey(time, local.z); curves[k][3].AddKey(time, local.w);
                    }
                    int n = clip.Bones.Length * 4;
                    var move = frame * new Vector3(row[n], row[n + 1], row[n + 2]) * hipsScale;
                    var hipsLocal = bones[pelvis].parent != null ? bones[pelvis].parent.InverseTransformPoint(restPelvis + move) : restPelvis + move;
                    hips[0].AddKey(time, hipsLocal.x); hips[1].AddKey(time, hipsLocal.y); hips[2].AddKey(time, hipsLocal.z);
                }
                foreach (var name in game)
                {
                    var anim = new AnimationClip { name = name, frameRate = clip.Fps };
                    foreach (int k in order)
                    {
                        string path = AnimationUtility.CalculateTransformPath(targets[k], go.transform);
                        anim.SetCurve(path, typeof(Transform), "localRotation.x", curves[k][0]);
                        anim.SetCurve(path, typeof(Transform), "localRotation.y", curves[k][1]);
                        anim.SetCurve(path, typeof(Transform), "localRotation.z", curves[k][2]);
                        anim.SetCurve(path, typeof(Transform), "localRotation.w", curves[k][3]);
                    }
                    string hipsPath = AnimationUtility.CalculateTransformPath(bones[pelvis], go.transform);
                    anim.SetCurve(hipsPath, typeof(Transform), "localPosition.x", hips[0]);
                    anim.SetCurve(hipsPath, typeof(Transform), "localPosition.y", hips[1]);
                    anim.SetCurve(hipsPath, typeof(Transform), "localPosition.z", hips[2]);
                    anim.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(anim);
                    settings.loopTime = Uses[name].loop;
                    AnimationUtility.SetAnimationClipSettings(anim, settings);
                    string asset = $"{Folder}/{name}.anim";
                    AssetDatabase.DeleteAsset(asset);
                    AssetDatabase.CreateAsset(anim, asset);
                    built[name] = anim;
                    made++;
                }
            }
            Debug.Log($"[Setup] Mixamo animations: {made} clips on the agent's skeleton (hips x{hipsScale:0.00}, facing {frame.eulerAngles.y:0})");
        }
        finally { Object.DestroyImmediate(go); }
    }
}
