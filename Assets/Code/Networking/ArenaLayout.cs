using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    public sealed class ArenaLayout : MonoBehaviour
    {
        private bool spawned;

        public void SpawnInitialItems(NetworkContext context)
        {
            if (spawned || !context.GetComponent<NetworkManager>().IsServer) return;

            spawned = true;
            var settings = context.Settings.World;
            var size = context.Settings.Items.CubeSize;
            for (var row = 0; row < settings.PyramidRows; row++)
            {
                var columns = settings.PyramidRows - row;
                for (var column = 0; column < columns; column++)
                {
                    var position = settings.PyramidOrigin + new Vector3(
                        (column - (columns - 1) * 0.5f) * settings.PyramidSpacing,
                        row * settings.PyramidSpacing + size * 0.5f + settings.PyramidGroundGap, 0);
                    if (!ItemSpawner.TrySpawn(context, position)) return;
                }
            }
        }
    }
}
