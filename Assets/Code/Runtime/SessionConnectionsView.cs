using System;
using System.Collections.Generic;
using ArtDock.Core;
using ArtDock.Networking;
using ArtDock.Networking.Platforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtDock.Runtime
{
    /// <summary> Lists discovered LAN sessions and Steam friends with join and invitation actions </summary>
    internal class SessionConnectionsView : IDisposable
    {
        private readonly VisualElement root;
        private readonly SessionService session;
        private readonly LanDiscovery discovery;
        private readonly IGameText text;
        private readonly List<(Button Button, Action Callback)> hostCallbacks = new();
        private readonly List<(Button Button, Action Callback)> friendCallbacks = new();
        private bool showFriends;
        private float friendsRefreshAt;

        public SessionConnectionsView(VisualElement root, SessionService session, LanDiscovery discovery, IGameText text,
            SessionConnectionsView previous)
        {
            this.root = root;
            this.session = session;
            this.discovery = discovery;
            this.text = text;

            showFriends = previous?.showFriends ?? false;
            friendsRefreshAt = previous?.friendsRefreshAt ?? 0;
        }

        /// <summary> Starts or stops LAN session discovery </summary>
        public void ToggleSearch()
        {
            if (discovery.Searching) discovery.StopSearch();
            else discovery.StartSearch();
        }

        /// <summary> Opens the invitation list and the Steam overlay when available </summary>
        public void ToggleFriends(bool joining)
        {
            showFriends = !showFriends;
            ResetFriends();
            if (showFriends) session.InviteFriends();
            RefreshFriends(joining);
        }

        public void ResetFriends() => friendsRefreshAt = 0;

        /// <summary> Rebuilds LAN session rows and sets whether each host can be joined </summary>
        public void RefreshHosts()
        {
            var list = root.Q("hosts");
            ClearCallbacks(hostCallbacks);
            list.Clear();
            root.Q<Label>("lan-status").text = discovery.Status;
            
            foreach (var host in discovery.Hosts)
            {
                var address = host.Address;
                var button = new Button
                {
                    text = text.Format(TextKeys.HostRow, host.Title, host.Players, host.Capacity, address),
                    enableRichText = false,
                    focusable = false,
                };
                
                BindDynamicButton(button, () => _ = session.Join(address), hostCallbacks);
                button.SetEnabled(!session.Busy && host.Players < host.Capacity);
                list.Add(button);
            }
        }

        /// <summary> Selects the friend list for joining or inviting and limits its refresh rate </summary>
        public void RefreshFriends(bool joining)
        {
            var inviting = session.CanInvite && showFriends;
            Show("steam-friends", joining);
            Show("invite-friends", inviting);
            if (!joining && !inviting || Time.unscaledTime < friendsRefreshAt) return;

            friendsRefreshAt = Time.unscaledTime + 2;
            RefreshFriendList(joining);
        }

        /// <summary> Fills the selected friend list and shows a message when no matching friends exist </summary>
        private void RefreshFriendList(bool joining)
        {
            var list = root.Q(joining ? "steam-friends-list" : "invite-friends-list");
            ClearCallbacks(friendCallbacks);
            list.Clear();
            SetText("steam-friends-heading", TextKeys.FriendsHeading);
            SetText("invite-friends-heading", session.SteamOverlayAvailable ? TextKeys.FriendsHeading : TextKeys.SteamOverlayHelp);

            foreach (var friend in session.SteamFriends)
            {
                if (joining && (string.IsNullOrEmpty(friend.Lobby) || friend.Lobby == "0")) continue;

                list.Add(CreateFriendButton(friend, joining));
            }

            if (list.childCount == 0) list.Add(new Label(text[joining ? TextKeys.NoFriendLobbies : TextKeys.NoOnlineFriends]));
        }

        /// <summary> Creates a friend row for joining their lobby or sending an invitation </summary>
        private Button CreateFriendButton(SessionFriend friend, bool joining)
        {
            var button = new Button
            {
                text = text.Format(joining ? TextKeys.JoinFriend : TextKeys.InviteFriend, friend.Name),
                enableRichText = false,
                focusable = false,
            };

            if (joining) BindDynamicButton(button, () => _ = session.Join(friend.Lobby), friendCallbacks);
            else BindDynamicButton(button, () => InviteFriend(friend, button), friendCallbacks);
            button.SetEnabled(!session.Busy);
            return button;
        }

        /// <summary> Sends an invitation and displays its result on the selected friend row </summary>
        private void InviteFriend(SessionFriend friend, Button button)
        {
            var sent = session.InviteFriend(friend.Id);
            button.text = text.Format(sent ? TextKeys.InvitationSent : TextKeys.InvitationFailed, friend.Name);
            button.SetEnabled(!sent);
        }

        /// <summary> Stores the callback so it can be removed when the list is rebuilt </summary>
        private static void BindDynamicButton(Button button, Action callback, List<(Button Button, Action Callback)> bindings)
        {
            button.clicked += callback;
            bindings.Add((button, callback));
        }

        /// <summary> Removes button callbacks before clearing the list </summary>
        private static void ClearCallbacks(List<(Button Button, Action Callback)> bindings)
        {
            foreach (var binding in bindings) binding.Button.clicked -= binding.Callback;
            bindings.Clear();
        }

        private void SetText(string element, string key) => root.Q<TextElement>(element).text = text[key];
        private void Show(string name, bool visible) => root.Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Dispose()
        {
            ClearCallbacks(hostCallbacks);
            ClearCallbacks(friendCallbacks);
        }
    }
}
