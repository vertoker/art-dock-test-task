using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArtDock.Core
{
    /// <summary> A Steam friend and the address of their current lobby </summary>
    public readonly struct SessionFriend
    {
        public readonly ulong Id;
        public readonly string Name;
        public readonly string Lobby;

        public SessionFriend(ulong id, string name, string lobby)
        {
            Id = id;
            Name = name;
            Lobby = lobby;
        }
    }

    /// <summary> Friend lists and invitations provided by the connection platform </summary>
    public interface IFriendsPlatform
    {
        bool OverlayAvailable { get; }
        SessionFriend[] GetFriends();
        bool InviteFriend(ulong id);
    }

    /// <summary> Tracks confirmed throws and flushes pending statistics </summary>
    public interface IPlayerStatistics
    {
        void ConfirmThrow();
        void Flush();
    }

    /// <summary> Connection platform used by the session </summary>
    public enum ConnectionMode : byte
    {
        Lan = 0,
        Steam = 1,
    }

    /// <summary> Prepares connections and exposes platform state and join requests </summary>
    public interface IConnectionPlatform : IDisposable
    {
        public ConnectionMode Mode { get; }
        public string PlayerId { get; }
        public string PlayerName { get; }
        public string LobbyId { get; }
        public string Error { get; }
        
        public event Action<string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;
        
        /// <summary> Prepares the platform to host a session with the given capacity and visibility </summary>
        public Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken);
        /// <summary> Prepares the platform to join an IP address or lobby ID </summary>
        public Task<bool> PrepareJoin(string address, CancellationToken cancellationToken);
        
        public void Tick();
        public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers);
        public void Leave();
    }

    /// <summary> Sends invitations and closes lobby access before session shutdown </summary>
    public interface ILobbyPlatform
    {
        public bool CanInvite { get; }
        public void InviteFriends();
        public void CloseLobby();
    }

    /// <summary> Exposes the platform overlay state to block gameplay input </summary>
    public interface IOverlayPlatform
    {
        bool OverlayOpen { get; }
    }
}
