using UnityEngine;

namespace ArtDock.Networking
{
    [CreateAssetMenu(menuName = "Game/Settings")]
    public class GameSettings : ScriptableObject
    {
        [SerializeField, Range(2, 4)] private int maxPlayers = 4;
        [SerializeField, Min(1)] private int tickRate = 30;

        public int MaxPlayers => maxPlayers;
        public int TickRate => tickRate;
    }
}
