// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Events;
using Stride.Input;
using ArenaRush.Core;

namespace ArenaRush.Player
{
    public class PlayerInput : SyncScript
    {
        /// <summary>
        /// Raised every frame with the intended direction of movement from the player.
        /// </summary>
        // TODO Should not be static, but allow binding between player and controller
        public static readonly EventKey<Vector3> MoveDirectionEventKey = new EventKey<Vector3>();

        public static readonly EventKey<Vector2> CameraDirectionEventKey = new EventKey<Vector2>();

        public static readonly EventKey<bool> JumpEventKey = new EventKey<bool>();
        private bool jumpButtonDown = false;

        public float DeadZone { get; set; } = 0.25f;

        public CameraComponent Camera { get; set; }

        /// <summary>
        /// Multiplies move movement by this amount to apply aim rotations
        /// </summary>
        public float MouseSensitivity = 1f;

        public List<Keys> KeysLeft { get; } = new List<Keys>();

        public List<Keys> KeysRight { get; } = new List<Keys>();

        public List<Keys> KeysUp { get; } = new List<Keys>();

        public List<Keys> KeysDown { get; } = new List<Keys>();

        public List<Keys> KeysJump { get; } = new List<Keys>();

        /// <summary>
        /// Touch: left part of the screen is a floating move stick, dragging on the right part (or while holding FIRE)
        /// aims the camera, and the round buttons fire, jump, reload, switch weapon and pause.
        /// </summary>
        public float TouchJoystickRadius { get; set; } = 0.08f;

        public float TouchLookSensitivity { get; set; } = 2.6f;

        private int? movePointer;
        private int? lookPointer;
        private int? firePointer;
        private Vector2 lookDelta;

        private static bool Hit(Vector2 normalized, Vector2 center, float radius)
        {
            var a = UI.Overlay.ToVirtual(normalized);
            var b = UI.Overlay.ToVirtual(center);
            return (a - b).Length() < radius * 1.25f;
        }

        private void ReadTouch()
        {
            var bounds = Game.Window.ClientBounds;
            var aspect = UI.Overlay.Width / UI.Overlay.Height;
            lookDelta = Vector2.Zero;
            TouchState.ClearPresses();

            foreach (var ev in Input.PointerEvents)
            {
                if (ev.Device is IMouseDevice)
                    continue;
                switch (ev.EventType)
                {
                    case PointerEventType.Pressed:
                        if (Hit(ev.Position, TouchState.FireCenter, TouchState.FireRadius) && firePointer == null)
                            firePointer = ev.PointerId;
                        else if (Hit(ev.Position, TouchState.JumpCenter, TouchState.JumpRadius))
                        {
                            TouchState.JumpPressed = true;
                            TouchState.JumpFlash = 0.15f;
                        }
                        else if (Hit(ev.Position, TouchState.ReloadCenter, TouchState.ReloadRadius))
                            TouchState.ReloadPressed = true;
                        else if (Hit(ev.Position, TouchState.SwapCenter, TouchState.SwapRadius))
                            TouchState.SwapPressed = true;
                        else if (Hit(ev.Position, TouchState.PauseCenter, TouchState.PauseRadius))
                            TouchState.PausePressed = true;
                        else if (ev.Position.X < 0.42f && movePointer == null)
                        {
                            movePointer = ev.PointerId;
                            TouchState.StickOrigin = TouchState.StickCurrent = ev.Position;
                        }
                        else if (lookPointer == null)
                            lookPointer = ev.PointerId;
                        break;
                    case PointerEventType.Moved:
                        if (ev.PointerId == movePointer)
                            TouchState.StickCurrent = ev.Position;
                        else if (ev.PointerId == lookPointer || ev.PointerId == firePointer)
                            lookDelta += ev.DeltaPosition * new Vector2(aspect, 1);
                        break;
                    case PointerEventType.Released:
                    case PointerEventType.Canceled:
                        if (ev.PointerId == movePointer) movePointer = null;
                        if (ev.PointerId == lookPointer) lookPointer = null;
                        if (ev.PointerId == firePointer) firePointer = null;
                        break;
                }
            }

            TouchState.FireHeld = firePointer != null || (Input.HasMouse && Input.IsMousePositionLocked && Input.IsMouseButtonDown(MouseButton.Left));
            TouchState.StickActive = movePointer != null;
            TouchState.Stick = Vector2.Zero;
            if (movePointer != null)
            {
                var d = (TouchState.StickCurrent - TouchState.StickOrigin) * new Vector2(aspect, 1) / TouchJoystickRadius;
                if (d.Length() > 1) d.Normalize();
                TouchState.Stick = new Vector2(d.X, -d.Y);
            }

            // keyboard equivalents for desktop testing
            if (Input.IsKeyPressed(Keys.R)) TouchState.ReloadPressed = true;
            if (Input.IsKeyPressed(Keys.Q) || Input.IsKeyPressed(Keys.Tab)) TouchState.SwapPressed = true;
            if (Input.IsKeyPressed(Keys.Escape) || Input.IsKeyPressed(Keys.P)) TouchState.PausePressed = true;
        }

        public override void Update()
        {
            ReadTouch();
            if (!GameFlow.IsPlaying)
            {
                MoveDirectionEventKey.Broadcast(Vector3.Zero);
                CameraDirectionEventKey.Broadcast(Vector2.Zero);
                JumpEventKey.Broadcast(false);
                return;
            }

            // Character movement: should be camera-aware
            {
                // Left stick: movement
                var moveDirection = Input.GetLeftThumbAny(DeadZone) + TouchState.Stick;

                // Keyboard: movement
                if (KeysLeft.Any(key => Input.IsKeyDown(key)))
                    moveDirection += -Vector2.UnitX;
                if (KeysRight.Any(key => Input.IsKeyDown(key)))
                    moveDirection += +Vector2.UnitX;
                if (KeysUp.Any(key => Input.IsKeyDown(key)))
                    moveDirection += +Vector2.UnitY;
                if (KeysDown.Any(key => Input.IsKeyDown(key)))
                    moveDirection += -Vector2.UnitY;

                // Broadcast the movement vector as a world-space Vector3 to allow characters to be controlled
                var worldSpeed = (Camera != null)
                    ? Utils.LogicDirectionToWorldDirection(moveDirection, Camera, Vector3.UnitY)
                    : new Vector3(moveDirection.X, 0, moveDirection.Y);

                // Adjust vector's magnitute - worldSpeed has been normalized
                var moveLength = moveDirection.Length();
                var isDeadZoneLeft = moveLength < DeadZone;
                if (isDeadZoneLeft)
                {
                    worldSpeed = Vector3.Zero;
                }
                else
                {
                    if (moveLength > 1)
                    {
                        moveLength = 1;
                    }
                    else
                    {
                        moveLength = (moveLength - DeadZone) / (1f - DeadZone);
                    }

                    worldSpeed *= moveLength;
                }

                MoveDirectionEventKey.Broadcast(worldSpeed);
            }

            // Camera rotation: left-right rotates the camera horizontally while up-down controls its altitude
            {
                // Right stick: camera rotation
                var cameraDirection = Input.GetRightThumbAny(DeadZone);
                var isDeadZoneRight = cameraDirection.Length() < DeadZone;
                if (isDeadZoneRight)
                    cameraDirection = Vector2.Zero;
                else
                    cameraDirection.Normalize();
                
                // Contrary to a mouse, driving camera rotation from a stick must be scaled by delta time.
                // The amount of camera rotation with a stick is constant over time based on the tilt of the stick,
                // Whereas mouse driven rotation is already constrained by time, it is driven by the difference in position from last *time* to this *time*.
                cameraDirection *= (float)this.Game.UpdateTime.Elapsed.TotalSeconds;

                // Mouse-based camera rotation. Only enabled after you click the screen to lock your cursor, pressing escape cancels this
                if (Input.HasMouse && Input.IsMouseButtonDown(MouseButton.Left))
                {
                    Input.LockMousePosition(true);
                    Game.IsMouseVisible = false;
                }
                if (Input.IsKeyPressed(Keys.F1))
                {
                    Input.UnlockMousePosition();
                    Game.IsMouseVisible = true;
                }
                if (Input.IsMousePositionLocked)
                {
                    cameraDirection += new Vector2(Input.MouseDelta.X, -Input.MouseDelta.Y)*MouseSensitivity;
                }

                cameraDirection += new Vector2(lookDelta.X, -lookDelta.Y) * TouchLookSensitivity;
                cameraDirection *= GameOptions.LookSensitivity;
                if (GameOptions.InvertY) cameraDirection.Y = -cameraDirection.Y;

                // Broadcast the camera direction directly, as a screen-space Vector2
                CameraDirectionEventKey.Broadcast(cameraDirection);
            }

            // Jumping: don't bother with jump restrictions here, just pass the button states
            {
                // Controller: jumping
                var isJumpDown = Input.IsGamePadButtonDownAny(GamePadButton.A);
                var didJump = (!jumpButtonDown && isJumpDown);
                jumpButtonDown = isJumpDown;

                // Keyboard: jumping
                didJump |= (KeysJump.Any(key => Input.IsKeyPressed(key)));
                didJump |= TouchState.JumpPressed;

                JumpEventKey.Broadcast(didJump);
            }
        }
    }
}
