using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Tracks cancellation and deadlines for one connection attempt </summary>
    internal class SessionAttempt : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly float timeout;
        private Task<bool> preparation;
        private float deadline;

        public CancellationToken Token => cancellation.Token;
        public bool PreparationTimedOut => cancellation.IsCancellationRequested;
        public bool ConnectionTimedOut => Time.realtimeSinceStartup > deadline;
        public bool PreparationCompleted => preparation == null || preparation.IsCompleted;

        public SessionAttempt(float timeout)
        {
            this.timeout = timeout;
            cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
        }

        public Task<bool> Prepare(IConnectionPlatform platform, bool host, string title, int capacity, bool isPrivate, string address)
        {
            preparation = host
                ? platform.PrepareHost(title, capacity, isPrivate, Token)
                : platform.PrepareJoin(address, Token);
            return preparation;
        }

        public void BeginConnection() => deadline = Time.realtimeSinceStartup + timeout;
        public void Cancel() => cancellation.Cancel();
        public void Dispose() => cancellation.Dispose();
    }
}
