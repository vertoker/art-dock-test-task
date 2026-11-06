using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArtDock.Core
{
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

    public interface IFriendsPlatform
    {
        bool OverlayAvailable { get; }
        SessionFriend[] GetFriends();
        bool InviteFriend(ulong id);
    }

    public interface IPlayerStatistics
    {
        void ConfirmThrow();
        void Flush();
    }

    public enum ConnectionMode : byte
    {
        Lan = 0,
        Steam = 1,
    }

    public interface IConnectionPlatform : IDisposable
    {
        public ConnectionMode Mode { get; }
        public string PlayerId { get; }
        public string PlayerName { get; }
        public string LobbyId { get; }
        public string Error { get; }
        
        public event Action<string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;
        
        public Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken);
        public Task<bool> PrepareJoin(string address, CancellationToken cancellationToken);
        
        public void Tick();
        public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers);
        public void Leave();
    }

    public interface ILobbyPlatform
    {
        public bool CanInvite { get; }
        public void InviteFriends();
        public void CloseLobby();
    }

    public interface IOverlayPlatform
    {
        bool OverlayOpen { get; }
    }
}
