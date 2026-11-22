using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Shows what the session says lies on the map in an ADVANCED match: weapons lying on their side,
    /// vests, closed crates, C4 packs, the yellow fuel drums and the planted charges, each with a soft ring
    /// of light on the ground so it can be found. The same on the host and on the others (the drums'
    /// colliders stop the shots everywhere). Built from Resources/Loot (made by the editor) and the
    /// weapons' own models.
    /// </summary>
    public class LootView : MonoBehaviour
    {
        public static LootView I;

        private readonly Dictionary<ushort, GameObject> items = new Dictionary<ushort, GameObject>();
        private readonly Dictionary<ushort, GameObject> charges = new Dictionary<ushort, GameObject>();
        private readonly List<GameObject> barrels = new List<GameObject>();
        private int shownVersion = -1;
        private static Material ringMat, beamMat;
        private static Mesh ringMesh, beamMesh;
        private readonly List<Transform> beams = new List<Transform>();

        public static void Ensure()
        {
            if (I == null)
            {
                I = new GameObject("LootView").AddComponent<LootView>();
                I.gameObject.AddComponent<AirDropView>();
            }
        }

        private void Update()
        {
            var s = Session.I;
            if (s == null) return;
            if (s.LootVersion != shownVersion)
            {
                shownVersion = s.LootVersion;
                Sync(s);
            }
            // the things on the ground turn slowly and the rings breathe, so they catch the eye
            float t = Time.time;
            foreach (var go in items.Values)
            {
                if (go == null) continue;
                var spin = go.transform.Find("Spin");
                if (spin != null) spin.localRotation = Quaternion.Euler(0, t * 40f % 360f, 0);
                var ring = go.transform.Find("Ring");
                if (ring != null) ring.localScale = Vector3.one * (1f + Mathf.Sin(t * 3f) * 0.06f);
            }
            // the beams of light turn to the camera (a flat light seen from every side) and breathe
            var cam = CameraRig.I != null ? CameraRig.I.Cam.transform : Camera.main != null ? Camera.main.transform : null;
            beams.RemoveAll(b => b == null);
            if (cam != null)
                foreach (var b in beams)
                {
                    var d = cam.position - b.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) b.rotation = Quaternion.LookRotation(d);
                    var k = b.localScale;
                    k.x = 1f + Mathf.Sin(t * 2.2f + b.GetInstanceID()) * 0.12f;
                    b.localScale = k;
                }
            // the charges' red lamps blink
            bool lit = Mathf.Repeat(t, 0.8f) < 0.12f;
            foreach (var go in charges.Values)
            {
                var led = go != null ? go.transform.Find("LedOn") : null;
                if (led != null) led.gameObject.SetActive(lit);
            }
        }

        private void Sync(Session s)
        {
            // things
            var keep = new HashSet<ushort>();
            foreach (var l in s.Loot)
            {
                keep.Add(l.Id);
                if (!items.ContainsKey(l.Id)) items[l.Id] = Make(l);
            }
            foreach (var id in new List<ushort>(items.Keys))
                if (!keep.Contains(id))
                {
                    if (items[id] != null)
                    {
                        Effects.Puff(items[id].transform.position + Vector3.up * 0.3f, Vector3.up);
                        Destroy(items[id]);
                    }
                    items.Remove(id);
                }
            // drums
            while (barrels.Count < s.Barrels.Count) barrels.Add(MakeBarrel());
            while (barrels.Count > s.Barrels.Count) { Destroy(barrels[barrels.Count - 1]); barrels.RemoveAt(barrels.Count - 1); }
            for (int i = 0; i < s.Barrels.Count; i++)
            {
                var b = s.Barrels[i];
                barrels[i].transform.position = b.Pos;
                if (barrels[i].activeSelf != b.Alive) barrels[i].SetActive(b.Alive);
            }
            // charges
            keep.Clear();
            foreach (var c in s.Charges)
            {
                keep.Add(c.Id);
                if (!charges.ContainsKey(c.Id)) charges[c.Id] = MakeCharge(c);
            }
            foreach (var id in new List<ushort>(charges.Keys))
                if (!keep.Contains(id)) { if (charges[id] != null) Destroy(charges[id]); charges.Remove(id); }
        }

        private static GameObject Load(string name)
        {
            var prefab = Resources.Load<GameObject>("Loot/" + name);
            if (prefab != null) return Instantiate(prefab);
            // nothing built yet: a plain box, so the game still works
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(0.6f, 0.3f, 0.4f);
            return go;
        }

        private GameObject Make(LootItem l)
        {
            var root = new GameObject("Loot " + l.Kind + " " + l.Item);
            root.transform.SetParent(transform, false);
            root.transform.position = l.Pos;
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            GameObject model;
            Color ring;
            switch (l.Kind)
            {
                case LootKind.Weapon:
                {
                    var prefab = Resources.Load<GameObject>("Weapons/W" + l.Item);
                    model = prefab != null ? Instantiate(prefab) : Load("Crate");
                    model.transform.SetParent(spin, false);
                    // lying on its side, its middle over the spot, just off the ground
                    model.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                    var b = Bounds(model);
                    model.transform.position += root.transform.position - new Vector3(b.center.x, b.min.y - 0.03f, b.center.z);
                    break;
                }
                case LootKind.Armor:
                    model = Load(l.Item > 100 ? "VestMilitary" : l.Item > 50 ? "VestHeavy" : "VestLight");
                    model.transform.SetParent(spin, false);
                    break;
                case LootKind.C4:
                    model = Load("C4Pack");
                    model.transform.SetParent(spin, false);
                    break;
                case LootKind.Grenade:
                    model = Load("GrenadePack");
                    model.transform.SetParent(spin, false);
                    break;
                default:
                    // the containers do not turn: the supply crate, the military crate, the crate from the sky
                    model = Load(l.Kind == LootKind.Military ? "CrateMilitary" : l.Kind == LootKind.Airdrop ? "AirdropCrate" : "Crate");
                    model.transform.SetParent(root.transform, false);
                    if (l.Kind != LootKind.Crate) model.transform.localRotation = Quaternion.Euler(0, (l.Id * 47) % 360, 0);
                    break;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // the ring in the colour of its grade; purple and gold things also send a beam of light up (seen from afar)
            ring = Tiers.Tint(l);
            Ring(root.transform, ring, l.Kind == LootKind.Airdrop ? 1.7f : Tiers.Container(l.Kind) ? 1.25f : 0.8f);
            int tier = Tiers.Of(l);
            if (l.Kind == LootKind.Airdrop) Beam(root.transform, ring, 38f, 0.9f);
            else if (l.Kind == LootKind.Military) Beam(root.transform, ring, 10f, 0.55f);
            else if (tier >= Tiers.Epic) Beam(root.transform, ring, tier >= Tiers.Legendary ? 6f : 3.5f, 0.35f);
            return root;
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.2f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private GameObject MakeBarrel()
        {
            var go = Load("Barrel");
            go.name = "FuelDrum";
            go.transform.SetParent(transform, false);
            // solid for the players and the shots (the host counts a hit on it)
            if (go.GetComponent<Collider>() == null)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f; cap.height = 0.9f; cap.center = new Vector3(0, 0.45f, 0);
            }
            go.layer = 0;
            return go;
        }

        private GameObject MakeCharge(Charge c)
        {
            var go = Load("C4Planted");
            go.name = "C4";
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(c.Pos, Quaternion.FromToRotation(Vector3.up, c.Normal));
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            // the owner's team colour on the ring under it
            Color ring = new Color(1f, 0.2f, 0.15f);
            if (Session.I != null && Session.I.Players.TryGetValue(c.Owner, out var owner) && World.I != null) ring = World.TeamColor(owner, Session.I);
            Ring(go.transform, ring, 0.35f);
            return go;
        }

        private static void Ring(Transform parent, Color color, float radius)
        {
            if (ringMat == null)
            {
                ringMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32f;
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.2f) * 0.9f + Mathf.Clamp01(1f - d) * 0.25f;
                        tex.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                tex.Apply();
                ringMat.mainTexture = tex;
                ringMesh = new Mesh { name = "LootRing" };
                ringMesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) };
                ringMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                ringMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                ringMesh.RecalculateBounds();
            }
            var go = new GameObject("Ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * 0.03f;
            var holder = new GameObject("Disc");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localScale = Vector3.one * radius;
            holder.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = holder.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ringMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_TintColor", new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.5f));
            mr.SetPropertyBlock(block);
        }

        /// <summary>A beam of light standing up from a thing, bright at the foot and fading upwards (additive, seen from far).</summary>
        private void Beam(Transform parent, Color color, float height, float width)
        {
            if (beamMat == null)
            {
                beamMat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                const int w = 32, h = 128;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float across = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                        float up = (y + 0.5f) / h;
                        float a = Mathf.Exp(-across * across * 6f) * Mathf.Pow(1f - up, 1.7f) * (0.35f + 0.65f * Mathf.Exp(-up * 9f));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                beamMat.mainTexture = tex;
                beamMesh = new Mesh { name = "LootBeam" };
                beamMesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
                beamMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                beamMesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };   // both faces
                beamMesh.RecalculateBounds();
            }
            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * 0.05f;
            var quad = new GameObject("Light");
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(width, height, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = beamMesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = beamMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_TintColor", new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.55f));
            mr.SetPropertyBlock(block);
            beams.Add(go.transform);
        }

        /// <summary>The thing within reach of a player standing at 'pos' (closest first), or null.</summary>
        public static LootItem Nearest(Vector3 pos)
        {
            var s = Session.I;
            if (s == null) return null;
            LootItem best = null;
            float bestD = Session.PickReach * Session.PickReach;
            foreach (var l in s.Loot)
            {
                var d = l.Pos - pos;
                if (Mathf.Abs(d.y) > 1.6f) continue;
                d.y = 0;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = l; }
            }
            return best;
        }

        /// <summary>What the PICK UP button says for a thing.</summary>
        public static string Label(LootItem l)
        {
            switch (l.Kind)
            {
                case LootKind.Weapon: return Weapons.Get(l.Item).Name;
                case LootKind.Armor: return l.Item == 150 ? "MILITARY VEST" : l.Item == 100 ? "HEAVY VEST" : l.Item == 50 ? "LIGHT VEST" : Loc.Pair("VEST", l.Item.ToString());
                case LootKind.C4: return "C4 x" + l.Item;
                case LootKind.Grenade: return "GRENADES x" + l.Item;
                case LootKind.Military: return "OPEN MILITARY CRATE";
                case LootKind.Airdrop: return "OPEN SUPPLY DROP";
                default: return "OPEN CRATE";
            }
        }

        public static void Clear()
        {
            if (I == null) return;
            foreach (var go in I.items.Values) if (go != null) Destroy(go);
            foreach (var go in I.charges.Values) if (go != null) Destroy(go);
            foreach (var go in I.barrels) if (go != null) Destroy(go);
            I.items.Clear();
            I.beams.Clear();
            I.charges.Clear();
            I.barrels.Clear();
            I.shownVersion = -1;
        }
    }
}
