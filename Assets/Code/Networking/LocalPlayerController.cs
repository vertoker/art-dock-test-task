using UnityEngine;
using UnityEngine.InputSystem;

namespace ArtDock.Networking
{
    /// <summary> Handles local movement and camera look, sending aim updates to the server </summary>
    public class LocalPlayerController
    {
        private readonly NetworkPlayer player;
        private readonly CharacterController controller;
        private readonly GameSettings.PlayerConfiguration settings;
        
        private float pitch;
        private float verticalSpeed;
        private float nextAim;
        private float refreshAim;
        private Vector3 lastAim;

        public LocalPlayerController(NetworkPlayer player, CharacterController controller, GameSettings settings)
        {
            this.player = player;
            this.controller = controller;
            this.settings = settings.Player;
        }

        public void Tick(bool acceptInput)
        {
            var mouse = Mouse.current;
            if (acceptInput && mouse != null) UpdateLook(mouse);
            UpdateMovement(acceptInput ? Keyboard.current : null);
        }

        /// <summary> Rotates the player and camera from mouse input and updates server aim </summary>
        private void UpdateLook(Mouse mouse)
        {
            var delta = mouse.delta.ReadValue() * settings.MouseSensitivity;
            player.transform.Rotate(0, delta.x, 0);
            
            // Clamp camera pitch to keep the view from flipping
            pitch = Mathf.Clamp(pitch - delta.y, -settings.PitchLimit, settings.PitchLimit);
            player.ViewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            if (Time.unscaledTime < nextAim) return;
            
            // Send aim after a noticeable turn or when its refresh interval expires
            nextAim = Time.unscaledTime + settings.AimSendInterval;
            var direction = player.ViewCamera.transform.forward;
            if (Time.unscaledTime < refreshAim && Vector3.Angle(direction, lastAim) < settings.AimAngleThreshold) return;
            
            lastAim = direction;
            refreshAim = Time.unscaledTime + settings.AimRefreshInterval;
            player.UpdateAim(direction);
        }

        /// <summary> Applies WASD movement, jumping and gravity through CharacterController </summary>
        private void UpdateMovement(Keyboard keyboard)
        {
            var input = Vector3.zero;
            if (keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                input.z = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            }
            
            // Limit diagonal speed and calculate vertical movement separately
            var movement = player.transform.TransformDirection(Vector3.ClampMagnitude(input, 1)) * settings.MoveSpeed;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            if (controller.isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                verticalSpeed = settings.JumpSpeed;
            
            verticalSpeed = Mathf.Max(verticalSpeed - settings.Gravity * Time.deltaTime, -settings.FallSpeed);
            controller.Move((movement + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        public void Reset()
        {
            pitch = 0;
            verticalSpeed = 0;
            nextAim = 0;
            refreshAim = 0;
            
            player.ViewCamera.transform.localRotation = Quaternion.identity;
        }
    }
}
