using System.Linq;
using UnityEngine;

/// <summary>
/// Where a real lamp post stands: at the edge of the street or path, against a wall, a fence or a
/// parapet, never out in the way. Used by every map for its street lamps and lanterns.
/// </summary>
public static class LampSpot
{
    private const int Rays = 24;

    /// <summary>
    /// The edge nearest to a foot point 'p' within 'reach': 'gap' out from a solid stretch of wall (no
    /// door or gap for a metre either side) on the same ground level, and the way out from that wall.
    /// </summary>
    public static bool Edge(Vector3 p, float reach, float gap, out Vector3 spot, out Vector3 outward)
    {
        int mask = UA.World.EnvMask | (1 << UA.World.FenceLayer);
        spot = p;
        outward = Vector3.zero;
        var eye = p + Vector3.up * 1.1f;
        var hits = Enumerable.Range(0, Rays)
            .Select(i => Quaternion.Euler(0, i * 360f / Rays, 0) * Vector3.forward)
            .Select(d => Physics.Raycast(eye, d, out var h, reach, mask, QueryTriggerInteraction.Ignore) ? h : (RaycastHit?)null)
            .Where(h => h.HasValue && Mathf.Abs(h.Value.normal.y) < 0.3f)
            .Select(h => h.Value)
            .OrderBy(h => h.distance);
        foreach (var h in hits)
        {
            var n = new Vector3(h.normal.x, 0, h.normal.z).normalized;
            var along = Vector3.Cross(Vector3.up, n);
            // a solid stretch of wall: no door, no end of the wall right beside the lamp
            bool solid = true;
            foreach (float s in new[] { -1f, 1f })
                if (!Physics.Raycast(h.point + n * 0.6f + along * s, -n, 1.2f, mask, QueryTriggerInteraction.Ignore)) solid = false;
            if (!solid) continue;
            var at = h.point + n * gap;
            // the same ground as the lamp had (not down a step, a canal or onto a ledge)
            if (!Physics.Raycast(new Vector3(at.x, p.y + 1.2f, at.z), Vector3.down, out var ground, 2f, mask, QueryTriggerInteraction.Ignore)
                || ground.normal.y < 0.8f || Mathf.Abs(ground.point.y - p.y) > 0.5f) continue;
            // room for the post itself
            if (Physics.CheckCapsule(ground.point + Vector3.up * 0.25f, ground.point + Vector3.up * 2.6f, 0.12f, mask, QueryTriggerInteraction.Ignore)) continue;
            spot = ground.point;
            outward = n;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Moves every lamp named 'name' under 'root' (its pivot at the foot of the post) to the edge of its
    /// path when there is one within 'reach', its arm (local +X, when it has one) out over the path.
    /// Returns how many were moved.
    /// </summary>
    public static int Settle(Transform root, string name, float reach, float gap, bool turnArm)
    {
        Physics.SyncTransforms();
        int moved = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == name).ToList())
        {
            // its own post must not count as the wall
            var own = t.GetComponentsInChildren<Collider>().Where(c => c.enabled).ToList();
            foreach (var c in own) c.enabled = false;
            bool found = Edge(t.position, reach, gap, out var spot, out var outward);
            foreach (var c in own) c.enabled = true;
            if (!found) continue;
            t.position = spot;
            if (turnArm) t.rotation = Quaternion.FromToRotation(Vector3.right, outward);
            moved++;
        }
        Physics.SyncTransforms();
        return moved;
    }
}
