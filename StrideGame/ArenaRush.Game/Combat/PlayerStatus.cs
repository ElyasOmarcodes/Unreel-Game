namespace ArenaRush.Combat
{
    /// <summary>Player numbers shown by the HUD.</summary>
    public static class PlayerStatus
    {
        public static float Health = 100;
        public static float MaxHealth = 100;
        public static string WeaponName = "";
        public static int Ammo;
        public static int Reserve;
        public static float ReloadProgress = -1;   // -1 when not reloading
        public static float HitMarker;             // seconds left
        public static bool LastHitHeadshot;
        public static float DamageFlash;           // seconds left
        public static float Spread;                // current crosshair spread (virtual px)
    }
}
