using UA;
using UnityEngine;

/// <summary>
/// Cover pieces of different kinds that make the open parts of the maps more interesting to fight in:
/// sandbag walls and bunkers, burned out cars, concrete pipes, anti-tank hedgehogs, crate stacks and
/// low concrete blocks. All of them use real PBR textures and have colliders.
/// </summary>
public static class Obstacles
{
    private static Material Sandbag => PortBuilder.Pbr("Sandbag", "Concrete042A", new Color(0.72f, 0.64f, 0.46f), 0.25f);
    private static Material Rust => PortBuilder.Pbr("RustPlates", "MetalPlates013", Color.white, 0.7f);
    private static Material Burned => PortBuilder.Pbr("Burned", "MetalPlates013", new Color(0.32f, 0.29f, 0.27f), 0.5f);
    private static Material Concrete => PortBuilder.Pbr("Concrete", "Concrete034", Color.white, 0.6f);
    private static Material Steel => PortBuilder.Pbr("HedgehogSteel", "Metal032", new Color(0.35f, 0.36f, 0.37f), 0.7f);
    private static Material Crate => PortBuilder.Pbr("Wood", "Wood049", new Color(0.85f, 0.75f, 0.6f), 0.5f);

    private static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, float tex, float localYaw = 0f, bool collider = true)
        => MeshGen.TexBox(name, parent.TransformPoint(localPos), size, m, tex, parent, parent.eulerAngles.y + localYaw, collider);

    private static Transform Root(Transform parent, string name, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        return t;
    }

    /// <summary>Straight wall of sandbags, three rows staggered like brickwork (about 1.1 m high).</summary>
    public static void SandbagWall(Transform parent, Vector3 pos, float yaw, float length)
    {
        var t = Root(parent, "Sandbags", pos, yaw);
        const float bag = 0.6f, h = 0.2f;
        for (int row = 0; row < 5; row++)
        {
            float shift = row % 2 == 0 ? 0f : bag / 2f;
            int count = Mathf.FloorToInt((length - shift) / bag);
            for (int i = 0; i < count; i++)
            {
                float x = -length / 2f + shift + bag * (i + 0.5f);
                Box("Bag", t, new Vector3(x, row * h + h / 2f, 0), new Vector3(bag * 0.96f, h * 0.95f, 0.42f - row * 0.03f), Sandbag, 0.6f, (i * 7 % 5 - 2) * 1.5f, false);
            }
        }
        // one collider for the whole wall
        var c = new GameObject("SandbagCollider");
        c.transform.SetParent(t, false);
        c.transform.localPosition = new Vector3(0, 0.5f, 0);
        c.AddComponent<BoxCollider>().size = new Vector3(length, 1f, 0.42f);
    }

    /// <summary>U shaped sandbag position open to the back.</summary>
    public static void SandbagBunker(Transform parent, Vector3 pos, float yaw)
    {
        var t = Root(parent, "SandbagBunker", pos, yaw);
        SandbagWall(t, t.TransformPoint(new Vector3(0, 0, 1.4f)), yaw, 3.6f);
        SandbagWall(t, t.TransformPoint(new Vector3(-1.6f, 0, 0.2f)), yaw + 90f, 2.2f);
        SandbagWall(t, t.TransformPoint(new Vector3(1.6f, 0, 0.2f)), yaw + 90f, 2.2f);
    }

    /// <summary>Burned out car on its rims.</summary>
    public static void CarWreck(Transform parent, Vector3 pos, float yaw)
    {
        var t = Root(parent, "CarWreck", pos, yaw);
        Box("Body", t, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 0.62f, 4.3f), Burned, 1.2f);
        Box("Cabin", t, new Vector3(0, 1.08f, -0.25f), new Vector3(1.55f, 0.48f, 2.1f), Burned, 1.2f);
        Box("Hood", t, new Vector3(0.05f, 0.9f, 1.55f), new Vector3(1.6f, 0.06f, 1.1f), Rust, 1f, 8f, false);
        foreach (var (x, z) in new[] { (0.85f, 1.35f), (-0.85f, 1.35f), (0.85f, -1.35f), (-0.85f, -1.35f) })
        {
            var rim = MeshGen.Object("Rim", MeshGen.Cylinder(0.24f, 0.2f, 1f, 14), Rust, t, t.TransformPoint(new Vector3(x - Mathf.Sign(x) * 0.1f, 0.24f, z)));
            rim.transform.rotation = t.rotation * Quaternion.Euler(0, 0, 90);
        }
    }

    /// <summary>Two big concrete pipes lying side by side (players can hide in and behind them).</summary>
    public static void Pipes(Transform parent, Vector3 pos, float yaw)
    {
        var t = Root(parent, "ConcretePipes", pos, yaw);
        foreach (float x in new[] { -0.85f, 0.85f })
        {
            var go = MeshGen.Object("Pipe", MeshGen.Cylinder(0.8f, 3.2f, 2f, 24), Concrete, t, t.TransformPoint(new Vector3(x, 0.8f, -1.6f)));
            go.transform.rotation = t.rotation * Quaternion.Euler(90, 0, 0);
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.8f;
            col.height = 3.2f;
            col.center = new Vector3(0, 1.6f, 0);
        }
        Box("Chock", t, new Vector3(0, 0.12f, -1.2f), new Vector3(3.4f, 0.24f, 0.25f), Crate, 1f);
        Box("Chock", t, new Vector3(0, 0.12f, 1.2f), new Vector3(3.4f, 0.24f, 0.25f), Crate, 1f);
    }

    /// <summary>Czech hedgehog: three crossed steel beams.</summary>
    public static void Hedgehog(Transform parent, Vector3 pos, float yaw)
    {
        var t = Root(parent, "Hedgehog", pos, yaw);
        Box("Beam", t, new Vector3(0, 0.7f, 0), new Vector3(0.14f, 2f, 0.14f), Steel, 1f, 0f, false).transform.localRotation = Quaternion.Euler(0, 0, 55);
        Box("Beam", t, new Vector3(0, 0.7f, 0), new Vector3(0.14f, 2f, 0.14f), Steel, 1f, 0f, false).transform.localRotation = Quaternion.Euler(0, 0, -55);
        Box("Beam", t, new Vector3(0, 0.7f, 0), new Vector3(0.14f, 2f, 0.14f), Steel, 1f, 0f, false).transform.localRotation = Quaternion.Euler(55, 0, 0);
        var c = new GameObject("HedgehogCollider");
        c.transform.SetParent(t, false);
        c.transform.localPosition = new Vector3(0, 0.6f, 0);
        c.AddComponent<BoxCollider>().size = new Vector3(1.4f, 1.2f, 1.4f);
    }

    /// <summary>Wooden crates stacked two or three high, with a step to climb on.</summary>
    public static void CrateStack(Transform parent, Vector3 pos, float yaw, System.Random rng)
    {
        var t = Root(parent, "CrateStack", pos, yaw);
        Box("Crate", t, new Vector3(0, 0.6f, 0), Vector3.one * 1.2f, Crate, 1.2f);
        Box("Crate", t, new Vector3(1.25f, 0.5f, 0.1f), Vector3.one * 1f, Crate, 1.2f, 6f);
        Box("Crate", t, new Vector3(0.2f, 1.75f, 0.05f), Vector3.one * 1.1f, Crate, 1.2f, (float)rng.NextDouble() * 20f - 10f);
        if (rng.NextDouble() < 0.5) Box("Crate", t, new Vector3(-1.05f, 0.4f, -0.1f), Vector3.one * 0.8f, Crate, 1.2f, 15f);
    }

    /// <summary>A row of low concrete blocks with gaps to shoot through.</summary>
    public static void Blocks(Transform parent, Vector3 pos, float yaw, int count)
    {
        var t = Root(parent, "ConcreteBlocks", pos, yaw);
        for (int i = 0; i < count; i++)
            Box("Block", t, new Vector3((i - (count - 1) / 2f) * 1.9f, 0.45f, 0), new Vector3(1.5f, 0.9f, 0.9f), Concrete, 1.5f, i % 2 == 0 ? 0f : 4f);
    }

    /// <summary>
    /// Olive ammunition crate with a yellow stripe and a small lamp, so it can be seen from afar.
    /// Players standing next to it can refill their ammunition and grenades.
    /// </summary>
    public static void AmmoCrate(Transform parent, Vector3 pos, float yaw)
    {
        var t = Root(parent, "AmmoCrate", pos, yaw);
        var olive = PortBuilder.Pbr("AmmoOlive", "Wood049", new Color(0.36f, 0.42f, 0.26f), 0.4f);
        var stripe = AssetSetup.Mat("AmmoStripe", new Color(0.95f, 0.75f, 0.1f), 0.4f, 0f, null, null, null, null, new Color(0.5f, 0.38f, 0.05f));
        Box("Crate", t, new Vector3(0, 0.35f, 0), new Vector3(1.2f, 0.7f, 0.7f), olive, 0.8f);
        Box("Lid", t, new Vector3(0, 0.73f, 0), new Vector3(1.26f, 0.08f, 0.76f), olive, 0.8f, 0f, false);
        Box("Stripe", t, new Vector3(0, 0.45f, 0.356f), new Vector3(1.0f, 0.12f, 0.01f), stripe, 1f, 0f, false);
        Box("Stripe", t, new Vector3(0, 0.45f, -0.356f), new Vector3(1.0f, 0.12f, 0.01f), stripe, 1f, 0f, false);
        foreach (float x in new[] { -0.5f, 0.5f })
            Box("Handle", t, new Vector3(x, 0.55f, 0), new Vector3(0.06f, 0.06f, 0.8f), Steel, 1f, 0f, false);
        var lamp = MeshGen.TexCylinder("Lamp", t.TransformPoint(new Vector3(0.45f, 0.77f, 0.2f)), 0.05f, 0.08f, stripe, 1f, t, false);
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.3f);
        light.range = 3f;
        light.intensity = 1.2f;
        light.shadows = LightShadows.None;
        light.transform.localPosition += Vector3.up * 0.1f;
        t.gameObject.AddComponent<AmmoBox>();
    }

    /// <summary>One random obstacle of any kind.</summary>
    public static void Random(Transform parent, Vector3 pos, float yaw, System.Random rng)
    {
        switch (rng.Next(6))
        {
            case 0: SandbagBunker(parent, pos, yaw); break;
            case 1: CarWreck(parent, pos, yaw); break;
            case 2: Pipes(parent, pos, yaw); break;
            case 3: Hedgehog(parent, pos, yaw); Hedgehog(parent, pos + Quaternion.Euler(0, yaw, 0) * new Vector3(2.2f, 0, 0.4f), yaw + 30f); break;
            case 4: CrateStack(parent, pos, yaw, rng); break;
            default: Blocks(parent, pos, yaw, 3); break;
        }
    }
}
