using UA;
using UnityEngine;

/// <summary>
/// Name boards for the parts of a map, as Al-Zahra has them (an Arabic line over an English one, on two
/// posts) but of old white-painted iron: each is set on free, level ground near the spot asked for, read from
/// the side that faces the given point.
/// </summary>
public static class Signboards
{
    /// <summary>Puts a board near 'near' (x, z) facing 'readers'; returns false when no room was found within 12 m.</summary>
    public static bool Stand(Transform parent, Vector3 near, Vector3 readers, string arabic, string english)
    {
        Physics.SyncTransforms();
        int mask = World.EnvMask | (1 << World.FenceLayer);
        for (int ring = 0; ring <= 8; ring++)
            for (int k = 0, n = Mathf.Max(1, ring * 8); k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                var probe = near + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ring * 1.5f);
                // the ground itself: the lowest level surface (not a roof, a deck or a tree)
                RaycastHit hit = default;
                bool any = false;
                foreach (var h in Physics.RaycastAll(new Vector3(probe.x, 80f, probe.z), Vector3.down, 160f, World.EnvMask, QueryTriggerInteraction.Ignore))
                    if (h.normal.y > 0.85f && (!any || h.point.y < hit.point.y)) { hit = h; any = true; }
                if (!any) continue;
                var face = readers - hit.point;
                face.y = 0;
                if (face.sqrMagnitude < 1f) face = Vector3.back;
                face.Normalize();
                var rot = Quaternion.LookRotation(face);
                if (Physics.CheckBox(hit.point + Vector3.up * 2.25f, new Vector3(1.8f, 2f, 0.45f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                // a slope under the posts: the board would float or sink at one end
                bool level = true;
                foreach (float s in new[] { -1.3f, 1.3f })
                {
                    var side = hit.point + rot * Vector3.right * s;
                    if (!Physics.Raycast(side + Vector3.up * 1.5f, Vector3.down, out var h2, 3f, mask, QueryTriggerInteraction.Ignore) || Mathf.Abs(h2.point.y - hit.point.y) > 0.3f) level = false;
                }
                if (!level) continue;
                // the board is read from its local -Z side
                float yaw = Mathf.Atan2(-face.x, -face.z) * Mathf.Rad2Deg;
                ZahraBuilder.BoardStand(parent, hit.point, yaw, arabic, english, true);
                Physics.SyncTransforms();
                return true;
            }
        Debug.LogWarning("[Map] no room for the board " + english);
        return false;
    }
}
