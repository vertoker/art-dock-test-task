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
        private readonly SessionService session;
        private readonly LanDiscovery discovery;
        private readonly IGameText text;

        private readonly VisualElement hostsList;
        private readonly Label lanStatus;
        private readonly VisualElement steamFriendsPanel;
        private readonly VisualElement inviteFriendsPanel;
        private readonly VisualElement steamFriendsList;
        private readonly VisualElement inviteFriendsList;
        private readonly Label steamFriendsHeading;
        private readonly Label inviteFriendsHeading;

        private readonly List<(Button Button, Action Callback)> hostCallbacks = new();
        private readonly List<(Button Button, Action Callback)> friendCallbacks = new();
        private bool showFriends;
        private float friendsRefreshAt;

        public SessionConnectionsView(VisualElement root, SessionService session, LanDiscovery discovery, IGameText text,
            SessionConnectionsView previous)
        {
            this.session = session;
            this.discovery = discovery;
            this.text = text;

            hostsList = root.Q("hosts");
            lanStatus = root.Q<Label>("lan-status");
            steamFriendsPanel = root.Q("steam-friends");
            inviteFriendsPanel = root.Q("invite-friends");
            steamFriendsList = root.Q("steam-friends-list");
            inviteFriendsList = root.Q("invite-friends-list");
            steamFriendsHeading = root.Q<Label>("steam-friends-heading");
            inviteFriendsHeading = root.Q<Label>("invite-friends-heading");

            showFriends = previous?.showFriends ?? false;
            friendsRefreshAt = previous?.friendsRefreshAt ?? 0;
        }

        public void ToggleSearch()
        {
            if (discovery.Searching) discovery.StopSearch();
            else discovery.StartSearch();
        }

        public void ToggleFriends(bool joining)
        {
            showFriends = !showFriends;
            ResetFriends();
            if (showFriends) session.InviteFriends();
            RefreshFriends(joining);
        }

        public void ResetFriends() => friendsRefreshAt = 0;

        public void RefreshHosts()
        {
            ClearCallbacks(hostCallbacks);
            hostsList.Clear();
            lanStatus.text = discovery.Status;
            
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
                hostsList.Add(button);
            }
        }

        public void RefreshFriends(bool joining)
        {
            var inviting = session.CanInvite && showFriends;
            Show(steamFriendsPanel, joining);
            Show(inviteFriendsPanel, inviting);
            if (!joining && !inviting || Time.unscaledTime < friendsRefreshAt) return;

            friendsRefreshAt = Time.unscaledTime + 2;
            RefreshFriendList(joining);
        }

        private void RefreshFriendList(bool joining)
        {
            var list = joining ? steamFriendsList : inviteFriendsList;
            ClearCallbacks(friendCallbacks);
            list.Clear();
            steamFriendsHeading.text = text[TextKeys.FriendsHeading];
            inviteFriendsHeading.text = text[session.SteamOverlayAvailable ? TextKeys.FriendsHeading : TextKeys.SteamOverlayHelp];

            foreach (var friend in session.SteamFriends)
            {
                if (joining && (string.IsNullOrEmpty(friend.Lobby) || friend.Lobby == "0")) continue;

                list.Add(CreateFriendButton(friend, joining));
            }

            if (list.childCount == 0) list.Add(new Label(text[joining ? TextKeys.NoFriendLobbies : TextKeys.NoOnlineFriends]));
        }

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

        private void InviteFriend(SessionFriend friend, Button button)
        {
            var sent = session.InviteFriend(friend.Id);
            button.text = text.Format(sent ? TextKeys.InvitationSent : TextKeys.InvitationFailed, friend.Name);
            button.SetEnabled(!sent);
        }

        private static void BindDynamicButton(Button button, Action callback, List<(Button Button, Action Callback)> bindings)
        {
            button.clicked += callback;
            bindings.Add((button, callback));
        }

        private static void ClearCallbacks(List<(Button Button, Action Callback)> bindings)
        {
            foreach (var binding in bindings) binding.Button.clicked -= binding.Callback;
            bindings.Clear();
        }

        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Dispose()
        {
            ClearCallbacks(hostCallbacks);
            ClearCallbacks(friendCallbacks);
        }
    }
}
