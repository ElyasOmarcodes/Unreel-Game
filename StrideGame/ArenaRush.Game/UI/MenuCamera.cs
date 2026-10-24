using System;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace ArenaRush.UI
{
    /// <summary>
    /// Slow cinematic orbit around the factory, used behind the splash and the menus.
    /// Switches between this camera and the player's camera depending on the game state.
    /// </summary>
    public class MenuCamera : SyncScript
    {
        public Vector3 Center { get; set; } = new Vector3(0, 2, -16);
        public float Radius { get; set; } = 26f;
        public float Height { get; set; } = 9f;

        private CameraComponent menuCamera;
        private CameraComponent playerCamera;
        private float angle;

        public override void Start()
        {
            menuCamera = Entity.Get<CameraComponent>();
            playerCamera = Entity.Scene.Entities.SelectMany(Combat.PlayerWeapon.Descendants)
                .Where(e => e.Name == "Camera").Select(e => e.Get<CameraComponent>()).FirstOrDefault(c => c != null);
            angle = 0.6f;
        }

        public override void Update()
        {
            var useMenu = !GameFlow.InMatch;
            if (menuCamera != null) menuCamera.Enabled = useMenu;
            if (playerCamera != null) playerCamera.Enabled = !useMenu;
            if (!useMenu) return;

            angle += (float)Game.UpdateTime.Elapsed.TotalSeconds * 0.06f;
            var pos = Center + new Vector3(MathF.Cos(angle) * Radius, Height, MathF.Sin(angle) * Radius);
            Entity.Transform.Position = pos;
            var view = Matrix.LookAtRH(pos, Center, Vector3.UnitY);
            view.Invert();
            view.Decompose(out _, out Quaternion rotation, out _);
            Entity.Transform.Rotation = rotation;
        }
    }
}
