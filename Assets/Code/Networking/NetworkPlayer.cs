using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArtDock.Networking
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerNetworkTransform))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private CapsuleCollider remoteCollider;
        public readonly NetworkVariable<Vector3> ReplicatedAim = new(Vector3.forward);
        
        private NetworkContext context;
        private CharacterController controller;
        private PlayerNetworkTransform networkTransform;
        private LocalPlayerController localControl;
        private PlayerValidation validation;
        private float nextCorrection;
        private float nextRespawn;
        private bool localActive;
        private PlayerInteraction interaction;
        private float nextItemAction;
        private float nextSpawn;
        internal NetworkContext Context => context;
        public NetworkItem HeldItem => context ? context.HeldItemFor(OwnerClientId) : null;

        public Camera ViewCamera => viewCamera;
        public Vector3 TrustedPosition => validation.TrustedPosition;
        public Vector3 AimDirection => validation.AimDirection;
        public bool PositionValid => validation.PositionValid;
        public bool CanAct => IsSpawned && IsServer && Playing && PositionValid && !networkTransform.HasInvalidState &&
                              context.Session.State == SessionState.Connected && !context.Session.Loading && context.World.IsReady(OwnerClientId);
        private bool Playing => context.World && context.World.Phase.Value == SessionPhase.Playing;

        public override void OnNetworkSpawn()
        {
            context = NetworkManager.GetComponent<NetworkContext>();
            controller = GetComponent<CharacterController>();
            networkTransform = GetComponent<PlayerNetworkTransform>();
            
            validation = new PlayerValidation(context.Settings, transform.position);
            localControl = new LocalPlayerController(this, controller, context.Settings);
            interaction = new PlayerInteraction(this);

            context.RegisterPlayer(this);
            viewCamera.transform.localPosition = Vector3.up * context.Settings.Player.EyeHeight;
            
            SetPresentation(false);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            var active = Playing && context.Session.State == SessionState.Connected && !context.Session.Loading;
            SetPresentation(active);
            
            if (!active) return;
            if (IsOwner) UpdateLocalPlayer();
            if (IsServer) UpdateServerPosition();
        }

        private void SetPresentation(bool active)
        {
            var enableLocal = active && IsOwner;
            controller.enabled = enableLocal;
            remoteCollider.enabled = active && !IsOwner;
            bodyRenderer.enabled = active && !IsOwner;
            
            viewCamera.gameObject.SetActive(enableLocal);
            if (enableLocal == localActive) return;
            localActive = enableLocal;
            
            if (IsOwner) SetCursor(enableLocal && Application.isFocused);
        }

        private void UpdateLocalPlayer()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var wasLocked = Cursor.lockState == CursorLockMode.Locked;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursor(false);
            else if (Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursor(true);
            
            var acceptInput = Application.isFocused && Cursor.lockState == CursorLockMode.Locked;
            localControl.Tick(acceptInput);
            if (acceptInput && wasLocked && keyboard != null && mouse != null) interaction.Tick(keyboard, mouse);
        }

        private void UpdateServerPosition()
        {
            if (networkTransform.HasInvalidState)
            {
                validation.RejectPosition();
                CorrectPosition();
                return;
            }

            if (PlayerValidation.OutsideBounds(transform.position, context.Settings))
            {
                Respawn();
                return;
            }

            if (!validation.TryMove(transform.position, Time.unscaledDeltaTime)) CorrectPosition();
        }

        private void CorrectPosition()
        {
            if (Time.unscaledTime < nextCorrection) return;

            nextCorrection = Time.unscaledTime + context.Settings.Player.CorrectionInterval;
            MoveOwner(validation.TrustedPosition);
        }

        private void Respawn()
        {
            if (Time.unscaledTime < nextRespawn) return;
            
            nextRespawn = Time.unscaledTime + context.Settings.Player.RespawnCooldown;
            HeldItem?.ReleaseDisconnected(OwnerClientId);
            var position = context.SpawnPositionFor(OwnerClientId);

            validation.Reset(position);
            MoveOwner(position);
        }

        private void MoveOwner(Vector3 position)
        {
            var wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            controller.enabled = wasEnabled;
            
            TeleportOwnerRpc(position);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void TeleportOwnerRpc(Vector3 position)
        {
            var wasEnabled = controller.enabled;
            controller.enabled = false;
            localControl.Reset();
            networkTransform.Teleport(position, Quaternion.identity, transform.localScale);
            controller.enabled = wasEnabled;
        }

        internal void UpdateAim(Vector3 direction) => AimRpc(direction);

        public bool TryConsumeItemAction()
        {
            if (!CanAct || Time.unscaledTime < nextItemAction) return false;

            nextItemAction = Time.unscaledTime + context.Settings.Items.ActionCooldown;
            return true;
        }

        public void RequestSpawn()
        {
            if (IsOwner) SpawnItemRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SpawnItemRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || !CanAct || Time.unscaledTime < nextSpawn) return;

            nextSpawn = Time.unscaledTime + context.Settings.Items.SpawnCooldown;
            ItemSpawner.TrySpawnForPlayer(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void AimRpc(Vector3 direction)
        {
            if (!CanAct || !validation.TryAim(direction, Time.unscaledTime)) return;

            ReplicatedAim.Value = validation.AimDirection;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && IsOwner)
                SetCursor(false);
        }

        private static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public override void OnNetworkDespawn()
        {
            HeldItem?.ReleaseDisconnected(OwnerClientId);
            context.UnregisterPlayer(this);

            SetPresentation(false);
            if (IsOwner) SetCursor(false);

            context = null;
        }
    }
}
