using UnityEngine;

namespace UA
{
    public class WeaponDef
    {
        public byte Id;
        public string Name;
        public bool Primary;
        public float Damage;
        public float FireRate;      // shots per second
        public float Range;
        public float Spread;        // degrees, hip fire
        public float AimSpread;     // degrees, aiming
        public float Recoil;        // degrees of camera kick
        public float ReloadTime;
        public float HeadMultiplier = 2f;
        public int MagSize;
        public int Reserve;
        public int Pellets = 1;
        public bool Auto;
        public bool Scope;
        public float AimFov = 45f;
        public string Sound;
        public float SoundPitch = 1f;
        public float MoveSpeed = 1f;
    }

    /// <summary>
    /// Damage rules (100 health): AK-47 kills with 5 body shots or 2 head shots, M4A1 6 / 3,
    /// L96 sniper 2 body shots or 1 head shot, Glock 7 / 3, Desert Eagle 3 / 2, SPAS-12 one close shot.
    /// </summary>
    public static class Weapons
    {
        public const int Count = 6;

        public static readonly WeaponDef[] All =
        {
            new WeaponDef { Id = 0, Name = "AK-47", Primary = true, Damage = 21, HeadMultiplier = 2.5f, FireRate = 10f, Range = 130, Spread = 2.6f, AimSpread = 0.7f, Recoil = 1.5f, ReloadTime = 2.4f, MagSize = 30, Reserve = 150, Auto = true, AimFov = 42, Sound = "shot_ak", MoveSpeed = 0.95f },
            new WeaponDef { Id = 1, Name = "M4A1", Primary = true, Damage = 18, HeadMultiplier = 2.5f, FireRate = 12.5f, Range = 130, Spread = 2.1f, AimSpread = 0.5f, Recoil = 1.05f, ReloadTime = 2.2f, MagSize = 30, Reserve = 180, Auto = true, AimFov = 42, Sound = "shot_m4", MoveSpeed = 0.97f },
            new WeaponDef { Id = 2, Name = "SPAS-12", Primary = true, Damage = 13, HeadMultiplier = 1.5f, Pellets = 9, FireRate = 1.4f, Range = 38, Spread = 6.5f, AimSpread = 4.5f, Recoil = 4f, ReloadTime = 3.2f, MagSize = 8, Reserve = 40, AimFov = 50, Sound = "shot_enemy", SoundPitch = 0.7f, MoveSpeed = 0.95f },
            new WeaponDef { Id = 3, Name = "L96 SNIPER", Primary = true, Damage = 75, HeadMultiplier = 3f, FireRate = 0.8f, Range = 300, Spread = 7f, AimSpread = 0f, Recoil = 5f, ReloadTime = 3f, MagSize = 5, Reserve = 25, Scope = true, AimFov = 15, Sound = "shot_ak", SoundPitch = 0.6f, MoveSpeed = 0.88f },
            new WeaponDef { Id = 4, Name = "GLOCK 17", Primary = false, Damage = 15, HeadMultiplier = 2.5f, FireRate = 6.5f, Range = 70, Spread = 2.2f, AimSpread = 0.8f, Recoil = 0.9f, ReloadTime = 1.5f, MagSize = 17, Reserve = 85, AimFov = 50, Sound = "shot_pistol", MoveSpeed = 1.05f },
            new WeaponDef { Id = 5, Name = "DESERT EAGLE", Primary = false, Damage = 35, HeadMultiplier = 2.5f, FireRate = 2.6f, Range = 80, Spread = 2.6f, AimSpread = 0.7f, Recoil = 3.2f, ReloadTime = 1.9f, MagSize = 7, Reserve = 35, AimFov = 48, Sound = "shot_pistol", SoundPitch = 0.75f, MoveSpeed = 1.03f },
        };

        public static WeaponDef Get(int id) => All[Mathf.Clamp(id, 0, All.Length - 1)];
    }

    /// <summary>Deterministic shot directions, so host and clients compute the same shotgun pellets.</summary>
    public static class Ballistics
    {
        public static Vector3 Spray(Vector3 dir, float degrees, System.Random rng)
        {
            if (degrees <= 0f) return dir;
            float angle = (float)rng.NextDouble() * degrees;
            float around = (float)rng.NextDouble() * 360f;
            var perpendicular = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            perpendicular = Quaternion.AngleAxis(around, dir) * perpendicular;
            return Quaternion.AngleAxis(angle, perpendicular) * dir;
        }

        public static Vector3[] Pellets(Vector3 dir, int seed, WeaponDef def)
        {
            if (def.Pellets <= 1) return new[] { dir };
            var rng = new System.Random(seed);
            var result = new Vector3[def.Pellets];
            result[0] = dir;
            for (int i = 1; i < result.Length; i++)
                result[i] = Spray(dir, def.Spread, rng);
            return result;
        }
    }
}
