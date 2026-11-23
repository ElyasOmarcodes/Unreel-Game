using System;
using Stride.Engine;

namespace ArenaRush.Combat
{
    /// <summary>Hit points for the player and the enemies.</summary>
    public class Health : StartupScript
    {
        public float MaxHealth { get; set; } = 100f;

        private float? current;

        /// <summary>Current hit points (full until damaged, independent of script start order).</summary>
        public float Current
        {
            get => current ?? MaxHealth;
            private set => current = value;
        }

        public bool IsDead => Current <= 0;

        /// <summary>Raised with the damage amount.</summary>
        public event Action<float> Damaged;

        public event Action Died;

        public override void Start()
        {
        }

        public void Revive() => current = null;

        public void Damage(float amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Math.Max(0, Current - amount);
            Damaged?.Invoke(amount);
            if (Current <= 0)
                Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Math.Min(MaxHealth, Current + amount);
        }
    }
}
