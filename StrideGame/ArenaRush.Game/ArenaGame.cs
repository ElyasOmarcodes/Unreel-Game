using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace ArenaRush;

/// <summary>
/// Top-down arena shooter: drag anywhere to move, the hero fires automatically at the nearest enemy.
/// Desktop fallback: WASD / arrow keys.
/// </summary>
public class ArenaGame : SyncScript
{
    private const float ArenaHalfSize = 18f;
    private const float PlayerSpeed = 7f;
    private const float BulletSpeed = 22f;
    private const float BulletLifetime = 1.4f;
    private const float FireInterval = 0.22f;
    private const float JoystickRadius = 0.12f; // in normalized screen units
    private const int MaxHealth = 5;

    private readonly Random random = new();
    private readonly List<Entity> enemies = new();
    private readonly List<(Entity Entity, Vector3 Velocity, float Life)> bullets = new();

    private Entity player;
    private Entity camera;
    private Entity enemyTemplate;
    private Entity bulletTemplate;

    private int? joystickPointer;
    private Vector2 joystickOrigin;
    private Vector2 joystickCurrent;

    private float fireTimer;
    private float spawnTimer;
    private float invulnerableTimer;
    private float elapsed;
    private int score;
    private int best;
    private int health;
    private bool gameOver;

    public override void Start()
    {
        var scene = Entity.Scene;
        player = scene.Entities.First(e => e.Name == "Player");
        camera = scene.Entities.First(e => e.Name == "Camera");
        enemyTemplate = scene.Entities.First(e => e.Name == "EnemyTemplate");
        bulletTemplate = scene.Entities.First(e => e.Name == "BulletTemplate");
        scene.Entities.Remove(enemyTemplate);
        scene.Entities.Remove(bulletTemplate);

        DebugText.Visible = true;
        DebugText.TextColor = Color.White;
        ResetGame();
    }

    public override void Update()
    {
        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
        var move = ReadMoveInput(out var tapped);

        if (gameOver)
        {
            if (tapped || Input.IsKeyPressed(Keys.Space) || Input.IsKeyPressed(Keys.Enter))
                ResetGame();
            UpdateCamera(dt);
            DrawHud();
            return;
        }

        elapsed += dt;
        invulnerableTimer = Math.Max(0, invulnerableTimer - dt);

        // Player movement
        var pos = player.Transform.Position + new Vector3(move.X, 0, move.Y) * PlayerSpeed * dt;
        pos.X = MathUtil.Clamp(pos.X, -ArenaHalfSize, ArenaHalfSize);
        pos.Z = MathUtil.Clamp(pos.Z, -ArenaHalfSize, ArenaHalfSize);
        player.Transform.Position = pos;
        if (move.LengthSquared() > 0.001f)
            player.Transform.Rotation *= Quaternion.RotationYawPitchRoll(0, move.Y * dt * 10f, -move.X * dt * 10f);

        // Blink while invulnerable
        var model = player.Get<ModelComponent>();
        model.Enabled = invulnerableTimer <= 0 || ((int)(invulnerableTimer * 12) % 2 == 0);

        SpawnEnemies(dt);
        UpdateEnemies(dt);
        Fire(dt);
        UpdateBullets(dt);
        UpdateCamera(dt);
        DrawHud();
    }

    private Vector2 ReadMoveInput(out bool tapped)
    {
        tapped = false;
        foreach (var ev in Input.PointerEvents)
        {
            switch (ev.EventType)
            {
                case PointerEventType.Pressed when joystickPointer == null:
                    joystickPointer = ev.PointerId;
                    joystickOrigin = joystickCurrent = ev.Position;
                    tapped = true;
                    break;
                case PointerEventType.Moved when ev.PointerId == joystickPointer:
                    joystickCurrent = ev.Position;
                    break;
                case PointerEventType.Released when ev.PointerId == joystickPointer:
                case PointerEventType.Canceled when ev.PointerId == joystickPointer:
                    joystickPointer = null;
                    break;
            }
        }

        var move = Vector2.Zero;
        if (joystickPointer != null)
        {
            var delta = joystickCurrent - joystickOrigin;
            // Normalized positions are relative to width/height; compensate for aspect ratio.
            var aspect = Game.Window.ClientBounds.Width / (float)Math.Max(1, Game.Window.ClientBounds.Height);
            delta.X *= aspect;
            move = delta / JoystickRadius;
            if (move.Length() > 1) move.Normalize();
            if (move.Length() < 0.1f) move = Vector2.Zero;
        }

        if (Input.IsKeyDown(Keys.W) || Input.IsKeyDown(Keys.Up)) move.Y -= 1;
        if (Input.IsKeyDown(Keys.S) || Input.IsKeyDown(Keys.Down)) move.Y += 1;
        if (Input.IsKeyDown(Keys.A) || Input.IsKeyDown(Keys.Left)) move.X -= 1;
        if (Input.IsKeyDown(Keys.D) || Input.IsKeyDown(Keys.Right)) move.X += 1;
        if (move.Length() > 1) move.Normalize();
        return move;
    }

    private void SpawnEnemies(float dt)
    {
        spawnTimer -= dt;
        if (spawnTimer > 0) return;

        // Spawn faster as time goes on.
        spawnTimer = Math.Max(0.35f, 1.6f - elapsed * 0.02f);
        var count = 1 + (int)(elapsed / 30f);
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(random.NextDouble() * Math.PI * 2);
            var spawn = player.Transform.Position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 16f;
            spawn.X = MathUtil.Clamp(spawn.X, -ArenaHalfSize, ArenaHalfSize);
            spawn.Z = MathUtil.Clamp(spawn.Z, -ArenaHalfSize, ArenaHalfSize);
            spawn.Y = 0.45f;

            var enemy = enemyTemplate.Clone();
            enemy.Transform.Position = spawn;
            Entity.Scene.Entities.Add(enemy);
            enemies.Add(enemy);
        }
    }

    private void UpdateEnemies(float dt)
    {
        var speed = 2.5f + elapsed * 0.04f;
        var playerPos = player.Transform.Position;
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            var toPlayer = playerPos - enemy.Transform.Position;
            toPlayer.Y = 0;
            var distance = toPlayer.Length();
            if (distance < 0.9f)
            {
                RemoveEnemy(i);
                if (invulnerableTimer <= 0)
                {
                    health--;
                    invulnerableTimer = 1.2f;
                    if (health <= 0) EndGame();
                }
                continue;
            }
            toPlayer.Normalize();
            enemy.Transform.Position += toPlayer * speed * dt;
            enemy.Transform.Rotation *= Quaternion.RotationY(dt * 3f);
        }
    }

    private void Fire(float dt)
    {
        fireTimer -= dt;
        if (fireTimer > 0 || enemies.Count == 0) return;

        var origin = player.Transform.Position;
        var target = enemies.OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, origin)).First();
        var dir = target.Transform.Position - origin;
        dir.Y = 0;
        if (dir.LengthSquared() > 15f * 15f) return;
        dir.Normalize();

        fireTimer = FireInterval;
        var bullet = bulletTemplate.Clone();
        bullet.Transform.Position = origin + dir * 0.6f;
        Entity.Scene.Entities.Add(bullet);
        bullets.Add((bullet, dir * BulletSpeed, BulletLifetime));
    }

    private void UpdateBullets(float dt)
    {
        for (var i = bullets.Count - 1; i >= 0; i--)
        {
            var (bullet, velocity, life) = bullets[i];
            life -= dt;
            bullet.Transform.Position += velocity * dt;

            var hit = enemies.FindIndex(e => Vector3.DistanceSquared(e.Transform.Position, bullet.Transform.Position) < 0.55f * 0.55f);
            if (hit >= 0)
            {
                RemoveEnemy(hit);
                score += 10;
                life = 0;
            }

            if (life <= 0)
            {
                Entity.Scene.Entities.Remove(bullet);
                bullets.RemoveAt(i);
            }
            else
            {
                bullets[i] = (bullet, velocity, life);
            }
        }
    }

    private void RemoveEnemy(int index)
    {
        Entity.Scene.Entities.Remove(enemies[index]);
        enemies.RemoveAt(index);
    }

    private void UpdateCamera(float dt)
    {
        var target = player.Transform.Position + new Vector3(0, 14f, 8f);
        camera.Transform.Position = Vector3.Lerp(camera.Transform.Position, target, Math.Min(1f, dt * 5f));
    }

    private void EndGame()
    {
        gameOver = true;
        best = Math.Max(best, score);
        player.Get<ModelComponent>().Enabled = true;
    }

    private void ResetGame()
    {
        foreach (var e in enemies) Entity.Scene.Entities.Remove(e);
        foreach (var b in bullets) Entity.Scene.Entities.Remove(b.Entity);
        enemies.Clear();
        bullets.Clear();

        player.Transform.Position = new Vector3(0, 0.5f, 0);
        health = MaxHealth;
        score = 0;
        elapsed = 0;
        spawnTimer = 1f;
        fireTimer = 0;
        invulnerableTimer = 0;
        gameOver = false;
    }

    private void DrawHud()
    {
        var hearts = new string('#', Math.Max(0, health)) + new string('-', MaxHealth - Math.Max(0, health));
        DebugText.Print($"ARENA RUSH   Score: {score}   Best: {best}   HP [{hearts}]   Time: {elapsed:0}s", new Int2(20, 20));
        if (gameOver)
        {
            var w = Game.Window.ClientBounds.Width;
            var h = Game.Window.ClientBounds.Height;
            DebugText.Print("GAME OVER", new Int2(w / 2 - 40, h / 2 - 20), Color.OrangeRed);
            DebugText.Print("Tap to play again", new Int2(w / 2 - 70, h / 2 + 5));
        }
        else if (elapsed < 4)
        {
            DebugText.Print("Drag your finger to move - you shoot automatically!", new Int2(20, 45), Color.Yellow);
        }
    }
}
