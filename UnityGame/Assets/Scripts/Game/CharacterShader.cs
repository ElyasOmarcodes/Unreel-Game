using System.Collections.Generic;
using UnityEngine;

namespace UA
{
    /// <summary>
    /// Puts the characters' opaque Standard materials on UA/Character (the same lighting and textures, plus
    /// the rim and the own light that keep a far player readable, and no distance fog). Cutout and
    /// see-through materials (hair, lashes) keep Standard. Each material is switched once.
    /// </summary>
    public static class CharacterShader
    {
        private static Shader shader;
        private static bool looked;
        private static readonly HashSet<Material> done = new HashSet<Material>();

        public static void Apply(GameObject character)
        {
            if (!looked) { looked = true; shader = Shader.Find("UA/Character"); }
            if (shader == null || !shader.isSupported) return;
            foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || done.Contains(m)) continue;
                    done.Add(m);
                    if (m.shader == null || m.shader.name != "Standard" || m.renderQueue >= 2450) continue;
                    if (m.IsKeywordEnabled("_ALPHATEST_ON") || m.IsKeywordEnabled("_ALPHABLEND_ON") || m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) continue;
                    int queue = m.renderQueue;
                    m.shader = shader;
                    m.renderQueue = queue;
                }
        }
    }
}
