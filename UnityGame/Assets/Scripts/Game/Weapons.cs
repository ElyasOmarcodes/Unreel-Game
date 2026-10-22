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
        public bool BoltAction;     // re-aim after every shot
        public float AimFov = 45f;
        public string Sound;
        public float SoundPitch = 1f;
        public float MoveSpeed = 1f;
        // real-world data, shown on the profile page
        public string Caliber = "";
        public int MuzzleVelocity;      // m/s
        public int EffectiveRange;      // m
        public string Action = "";

    }

    /// <summary>
    /// Damage rules (100 health): AK-47 kills with 5 body shots or 2 head shots, M4A1 6 / 2,
    /// L96 sniper 2 body shots or 1 head shot, SVD 3 / 1, Glock 7 / 3, Desert Eagle 3 / 2,
    /// MP5 8 / 3, PKM 5 / 2 (a belt of 100, slow to reload, heavy to carry), SPAS-12 one close shot. Scoped guns: M4A1 (ACOG), SVD (PSO-1: its real field of view of about 6 degrees, reticle in true thousandths), L96.
    /// Any weapon can go in either slot.
    /// </summary>
    public static class Weapons
    {
        public const int Count = 9;

        public static readonly WeaponDef[] All =
        {
            new WeaponDef { Id = 0, Name = "AK-47", Primary = true, Damage = 21, HeadMultiplier = 2.5f, FireRate = 10f, Range = 400, Spread = 2.6f, AimSpread = 0.7f, Recoil = 1.5f, ReloadTime = 2.4f, MagSize = 30, Reserve = 150, Auto = true, AimFov = 42, Sound = "shot_ak", MoveSpeed = 0.95f, Caliber = "7.62×39 mm", MuzzleVelocity = 715, EffectiveRange = 350, Action = "AUTO 600 RPM" },
            new WeaponDef { Id = 1, Name = "M4A1 ACOG", Primary = true, Damage = 18, HeadMultiplier = 2.8f, FireRate = 12.5f, Range = 450, Spread = 2.1f, AimSpread = 0.35f, Recoil = 1.05f, ReloadTime = 2.2f, MagSize = 30, Reserve = 180, Auto = true, Scope = true, AimFov = 27, Sound = "shot_m4", MoveSpeed = 0.97f, Caliber = "5.56×45 mm NATO", MuzzleVelocity = 910, EffectiveRange = 500, Action = "AUTO 750 RPM" },
            new WeaponDef { Id = 2, Name = "SPAS-12", Primary = true, Damage = 13, HeadMultiplier = 1.5f, Pellets = 9, FireRate = 1.4f, Range = 38, Spread = 6.5f, AimSpread = 4.5f, Recoil = 4f, ReloadTime = 3.2f, MagSize = 8, Reserve = 40, AimFov = 50, Sound = "shot_shotgun", MoveSpeed = 0.95f, Caliber = "12 GAUGE", MuzzleVelocity = 400, EffectiveRange = 40, Action = "SEMI-AUTO" },
            new WeaponDef { Id = 3, Name = "L96 SNIPER", Primary = true, Damage = 75, HeadMultiplier = 3f, FireRate = 0.8f, Range = 800, Spread = 7f, AimSpread = 0f, Recoil = 5f, ReloadTime = 3f, MagSize = 5, Reserve = 25, Scope = true, BoltAction = true, AimFov = 4.5f, Sound = "shot_sniper", MoveSpeed = 0.88f, Caliber = "7.62×51 mm NATO", MuzzleVelocity = 850, EffectiveRange = 800, Action = "BOLT ACTION" },
            new WeaponDef { Id = 4, Name = "GLOCK 17", Primary = false, Damage = 15, HeadMultiplier = 2.5f, FireRate = 6.5f, Range = 150, Spread = 2.2f, AimSpread = 0.8f, Recoil = 0.9f, ReloadTime = 1.5f, MagSize = 17, Reserve = 85, AimFov = 50, Sound = "shot_pistol", MoveSpeed = 1.05f, Caliber = "9×19 mm", MuzzleVelocity = 375, EffectiveRange = 50, Action = "SEMI-AUTO" },
            new WeaponDef { Id = 5, Name = "DESERT EAGLE", Primary = false, Damage = 35, HeadMultiplier = 2.5f, FireRate = 2.6f, Range = 150, Spread = 2.6f, AimSpread = 0.7f, Recoil = 3.2f, ReloadTime = 1.9f, MagSize = 7, Reserve = 35, AimFov = 48, Sound = "shot_deagle", MoveSpeed = 1.03f, Caliber = ".50 AE", MuzzleVelocity = 470, EffectiveRange = 50, Action = "SEMI-AUTO" },
            new WeaponDef { Id = 6, Name = "MP5", Primary = false, Damage = 14, HeadMultiplier = 2.2f, FireRate = 13.33f, Range = 220, Spread = 2.8f, AimSpread = 1f, Recoil = 0.8f, ReloadTime = 2f, MagSize = 30, Reserve = 180, Auto = true, AimFov = 46, Sound = "shot_smg", MoveSpeed = 1.02f, Caliber = "9×19 mm", MuzzleVelocity = 400, EffectiveRange = 150, Action = "AUTO 800 RPM" },
            new WeaponDef { Id = 7, Name = "SVD DRAGUNOV", Primary = true, Damage = 46, HeadMultiplier = 2.4f, FireRate = 3f, Range = 700, Spread = 4f, AimSpread = 0.1f, Recoil = 3f, ReloadTime = 2.6f, MagSize = 10, Reserve = 40, Scope = true, AimFov = 6.5f, Sound = "shot_marksman", MoveSpeed = 0.92f, Caliber = "7.62×54 mm R", MuzzleVelocity = 830, EffectiveRange = 800, Action = "SEMI-AUTO" },
            new WeaponDef { Id = 8, Name = "PKM", Primary = true, Damage = 24, HeadMultiplier = 2.4f, FireRate = 10.8f, Range = 500, Spread = 3.4f, AimSpread = 1.2f, Recoil = 1.9f, ReloadTime = 5.5f, MagSize = 100, Reserve = 200, Auto = true, AimFov = 45, Sound = "shot_ak", SoundPitch = 0.86f, MoveSpeed = 0.85f, Caliber = "7.62×54 mm R", MuzzleVelocity = 825, EffectiveRange = 1000, Action = "AUTO 650 RPM, BELT 100" },
        };

        /// <summary>Weapons the bots pick from.</summary>
        public static readonly byte[] BotPrimaries = { 0, 1, 2, 3, 6, 7, 8 };

        /// <summary>Not a gun: used for kill feed and damage rules of frag grenades.</summary>
        public static readonly WeaponDef Grenade = new WeaponDef { Id = 20, Name = "GRENADE", Primary = true, Damage = 100, HeadMultiplier = 1f };

        public static WeaponDef Get(int id) => id == Grenade.Id ? Grenade : All[Mathf.Clamp(id, 0, All.Length - 1)];
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
