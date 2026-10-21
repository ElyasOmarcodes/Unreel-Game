using UnityEngine;

namespace UA
{
    /// <summary>
    /// Small photo studio high above the map: renders the player's character and a weapon into two
    /// textures for the profile screen (real 3D, rotating).
    /// </summary>
    public class PreviewStage : MonoBehaviour
    {
        public RenderTexture CharacterTexture, GunTexture;
        public float CharacterYaw = 20f;

        private Camera characterCam, gunCam;
        private GameObject character, gun;
        private Transform gunPivot;
        private int shownLook = -1, shownGun = -1;
        private static readonly Vector3 Origin = new Vector3(0, 900, 0);

        public static PreviewStage Create()
        {
            var go = new GameObject("PreviewStage");
            go.transform.position = Origin;
            var stage = go.AddComponent<PreviewStage>();
            stage.Build();
            stage.SetActive(false);
            return stage;
        }

        private Camera MakeCamera(string name, RenderTexture rt, Vector3 pos, Vector3 lookAt, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos);
            var cam = go.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            return cam;
        }

        private void Light(Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private void Build()
        {
            CharacterTexture = new RenderTexture(768, 1024, 24) { antiAliasing = 2 };
            GunTexture = new RenderTexture(1024, 400, 24) { antiAliasing = 2 };
            characterCam = MakeCamera("CharacterCam", CharacterTexture, Origin + new Vector3(0, 1.2f, 3.6f), Origin + new Vector3(0, 0.95f, 0), 38f);
            gunCam = MakeCamera("GunCam", GunTexture, Origin + new Vector3(20f, 0.05f, 1.6f), Origin + new Vector3(20f, 0f, 0), 30f);
            Light(Origin + new Vector3(1.5f, 2.5f, 2.5f), new Color(1f, 0.95f, 0.88f), 3f, 8f);
            Light(Origin + new Vector3(-2f, 1.5f, 1.5f), new Color(0.5f, 0.65f, 1f), 1.6f, 8f);
            Light(Origin + new Vector3(0f, 2f, -2.5f), new Color(1f, 0.6f, 0.25f), 2.2f, 8f);
            Light(Origin + new Vector3(20.5f, 1f, 1.2f), Color.white, 2.5f, 6f);
            Light(Origin + new Vector3(19.2f, 0.6f, -1f), new Color(1f, 0.6f, 0.25f), 1.5f, 6f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.position = Origin + new Vector3(0, -0.05f, 0);
            floor.transform.localScale = new Vector3(1.8f, 0.05f, 1.8f);
            var floorMat = new Material(Shader.Find("Standard")) { color = new Color(0.12f, 0.14f, 0.17f) };
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            gunPivot = new GameObject("GunPivot").transform;
            gunPivot.SetParent(transform, false);
            gunPivot.position = Origin + new Vector3(20f, 0, 0);
        }

        public void SetActive(bool active)
        {
            characterCam.enabled = active;
            gunCam.enabled = active;
            if (character != null) character.SetActive(active);
            if (gun != null) gun.SetActive(active);
        }

        public void ShowCharacter(int look, int weapon)
        {
            if (look != shownLook)
            {
                if (character != null) Destroy(character);
                var prefab = Resources.Load<GameObject>((look / 16) % 2 == 1 ? "Agent_F" : "Agent") ?? Resources.Load<GameObject>("Agent");
                character = Instantiate(prefab, Origin, Quaternion.Euler(0, CharacterYaw, 0), transform);
                character.GetComponent<CharacterController>().enabled = false;
                var agent = character.GetComponent<Agent>();
                agent.SetTeamColor(new Color(0.18f, 0.42f, 0.95f));
                agent.SetLook(look);
                shownLook = look;
            }
            character.GetComponent<Agent>().SetWeapon(weapon);
        }

        public void ShowGun(int weapon)
        {
            if (weapon == shownGun) return;
            if (gun != null) Destroy(gun);
            var prefab = Resources.Load<GameObject>("Weapons/W" + weapon);
            if (prefab != null)
            {
                gun = Instantiate(prefab, gunPivot);
                gun.transform.localPosition = Vector3.zero;
                gun.transform.localRotation = Quaternion.identity;
                // scale the gun to fill the picture, centred on the pivot
                var renderers = gun.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds Measure()
                    {
                        var b = renderers[0].bounds;
                        foreach (var r in renderers) b.Encapsulate(r.bounds);
                        return b;
                    }
                    var before = Measure();
                    float longest = Mathf.Max(before.size.x, Mathf.Max(before.size.y, before.size.z));
                    if (longest > 0.001f) gun.transform.localScale *= 0.9f / longest;
                    gun.transform.position += gunPivot.position - Measure().center;
                }
                gunCam.transform.position = gunPivot.position + new Vector3(1.15f, 0.08f, 0);
                gunCam.transform.rotation = Quaternion.LookRotation(gunPivot.position - gunCam.transform.position);
            }
            shownGun = weapon;
        }

        private void Update()
        {
            if (character != null) character.transform.rotation = Quaternion.Euler(0, CharacterYaw, 0);
            // swing around the side view so the whole gun stays readable
            gunPivot.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.unscaledTime * 0.7f) * 40f, 0);
        }
    }
}
