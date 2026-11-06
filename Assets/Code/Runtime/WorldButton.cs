using ArtDock.Core;
using ArtDock.Networking;
using UnityEngine;

namespace ArtDock.Runtime
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorldButton : MonoBehaviour, IWorldButton
    {
        [SerializeField] private int id;
        [SerializeField] private Transform output;
        private NetworkContext context;
        private float nextUse;
        public int Id => id;
        public bool OutputClear => output && context && !Physics.CheckBox(output.position, context.Settings.Items.HalfExtents,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        public string Prompt => context.Text[OutputClear ? TextKeys.CreateCube : TextKeys.ClearPad];

        private void OnEnable()
        {
            context = FindFirstObjectByType<NetworkContext>();
            if (context) context.RegisterButton(this);
        }

        private void OnDisable()
        {
            if (context) context.UnregisterButton(this);
        }

        public void Use(NetworkPlayer player)
        {
            if (!output || !player.CanAct || Time.unscaledTime < nextUse) return;

            var eye = player.TrustedPosition + Vector3.up * context.Settings.Player.EyeHeight;
            if (!Physics.Raycast(eye, player.AimDirection, out var hit, context.Settings.Items.InteractionRange,
                ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<WorldButton>() != this) return;

            nextUse = Time.unscaledTime + context.Settings.Items.DispenserCooldown;
            ItemSpawner.TrySpawn(context, output.position);
        }
    }
}
