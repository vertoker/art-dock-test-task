using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Creates the starting cube pyramid after the arena loads </summary>
    public class ArenaLayout : MonoBehaviour
    {
        private bool spawned;

        /// <summary> Creates pyramid rows once through the shared server spawner </summary>
        public void SpawnInitialItems(NetworkContext context)
        {
            // Only the server creates the pyramid, and repeated calls add nothing
            if (spawned || !context.GetComponent<NetworkManager>().IsServer) return;

            spawned = true;
            var settings = context.Settings.World;
            var size = context.Settings.Items.CubeSize;
            
            for (var row = 0; row < settings.PyramidRows; row++)
            {
                var columns = settings.PyramidRows - row;
                for (var column = 0; column < columns; column++)
                {
                    // Center each row relative to the pyramid base
                    var position = settings.PyramidOrigin + new Vector3(
                        (column - (columns - 1) * 0.5f) * settings.PyramidSpacing,
                        row * settings.PyramidSpacing + size * 0.5f + settings.PyramidGroundGap, 0);
                    
                    if (!ItemSpawner.TrySpawn(context, position)) return;
                }
            }
        }
    }
}
