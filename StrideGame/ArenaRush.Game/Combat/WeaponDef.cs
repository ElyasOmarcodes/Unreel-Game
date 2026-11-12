namespace ArenaRush.Combat
{
    public sealed class WeaponDef
    {
        public string Id;
        public string Name;
        public float Damage;
        public float RoundsPerMinute;
        public int Magazine;
        public int StartReserve;
        public float Spread;          // degrees
        public float Recoil;          // degrees of camera kick per shot
        public float ReloadTime;      // seconds
        public float Range = 120f;
        public bool Automatic;
        public string Sound;

        public float FireInterval => 60f / RoundsPerMinute;

        public static readonly WeaponDef[] All =
        {
            new WeaponDef { Id = "AK47", Name = "AK-47", Damage = 36, RoundsPerMinute = 600, Magazine = 30, StartReserve = 120,
                            Spread = 2.2f, Recoil = 1.6f, ReloadTime = 2.3f, Automatic = true, Sound = "ak" },
            new WeaponDef { Id = "M4", Name = "M4A1", Damage = 29, RoundsPerMinute = 780, Magazine = 30, StartReserve = 150,
                            Spread = 1.3f, Recoil = 1.0f, ReloadTime = 2.0f, Automatic = true, Sound = "m4" },
            new WeaponDef { Id = "PISTOL", Name = "PISTOL", Damage = 24, RoundsPerMinute = 360, Magazine = 15, StartReserve = 90,
                            Spread = 1.0f, Recoil = 1.2f, ReloadTime = 1.4f, Automatic = false, Sound = "pistol" },
        };
    }
}
