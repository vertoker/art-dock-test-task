using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using Steamworks;
using Steamworks.Data;

namespace ArtDock.Networking.Platforms
{
    /// <summary> Bounds Steam request duration and leaves lobbies returned after cancellation </summary>
    internal class SteamLobbyOperation
    {
        private Task cleanup = Task.CompletedTask;
        private CancellationTokenSource cancellation;
        private int generation;
        private bool running;

        public bool Busy => running || !cleanup.IsCompleted;
        public string Error { get; private set; } = string.Empty;

        public async Task<Lobby?> Run(Func<Task<Lobby?>> begin, float timeout, CancellationToken token)
        {
            Error = string.Empty;
            if (Busy)
            {
                Error = GameErrors.SteamLobbyPending;
                return null;
            }

            token.ThrowIfCancellationRequested();
            running = true;
            var version = ++generation;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellation = deadline;
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));

            try
            {
                var pending = begin();
                var canceled = Task.Delay(Timeout.Infinite, deadline.Token);
                var completed = await Task.WhenAny(pending, canceled);

                if (completed != pending || deadline.IsCancellationRequested || version != generation)
                {
                    cleanup = CleanupLate(pending);
                    if (!token.IsCancellationRequested && version == generation) Error = GameErrors.SteamLobbyTimedOut;
                    return null;
                }

                return await pending;
            }
            finally
            {
                cancellation = null;
                deadline.Cancel();
                running = false;
            }
        }

        public void Cancel()
        {
            generation++;
            cancellation?.Cancel();
        }

        /// <summary> Leaves a lobby returned after cancellation without surfacing cleanup failures </summary>
        private static async Task CleanupLate(Task<Lobby?> pending)
        {
            try
            {
                var lobby = await pending;
                if (lobby.HasValue && SteamClient.IsValid) lobby.Value.Leave();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
