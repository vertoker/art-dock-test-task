using System;
using ArtDock.Networking;
using ArtDock.Networking.Platforms;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ArtDock.Runtime
{
    /// <summary> Registers application dependencies and keeps the root object across scene changes </summary>
    public class ProjectScope : LifetimeScope
    {
        [SerializeField] private GameSettings settings;
        [SerializeField] private GameTextSettings textSettings;
        [SerializeField] private NetworkManager manager;
        [SerializeField] private NetworkContext context;
        [SerializeField] private SessionView view;

        protected override void Awake()
        {
            DontDestroyOnLoad(gameObject);
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (!settings) throw new NullReferenceException(string.Format(GameErrors.MissingReference, nameof(settings)));
            if (!textSettings) throw new NullReferenceException(string.Format(GameErrors.MissingReference, nameof(textSettings)));
            if (!manager) throw new NullReferenceException(string.Format(GameErrors.MissingReference, nameof(manager)));
            if (!context) throw new NullReferenceException(string.Format(GameErrors.MissingReference, nameof(context)));
            if (!view) throw new NullReferenceException(string.Format(GameErrors.MissingReference, nameof(view)));

            context.Text = textSettings;
            
            builder.RegisterInstance(settings);
            builder.RegisterInstance<IGameText>(textSettings);
            builder.RegisterInstance(manager);
            builder.RegisterInstance(context);
            
            builder.Register<LaunchOptions>(Lifetime.Singleton);
            builder.Register<LanPlatform>(Lifetime.Singleton).As<IConnectionPlatform>();
            builder.Register<SteamPlatform>(Lifetime.Singleton).As<IConnectionPlatform>();
            builder.Register<PlatformService>(Lifetime.Singleton);
            builder.Register<SessionService>(Lifetime.Singleton);
            builder.Register<LanDiscovery>(Lifetime.Singleton);
            builder.Register<LoggingStatistics>(Lifetime.Singleton).As<IPlayerStatistics>();
            
            builder.RegisterComponent(view);
            builder.RegisterEntryPoint<AppLoop>();
        }
    }
}
