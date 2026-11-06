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
    public class ProjectScope : LifetimeScope
    {
        [SerializeField] private GameSettings settings;
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
            if (!settings) throw new NullReferenceException($"\"{nameof(settings)}\" is null");
            if (!manager) throw new NullReferenceException($"\"{nameof(manager)}\" is null");
            if (!context) throw new NullReferenceException($"\"{nameof(context)}\" is null");
            if (!view) throw new NullReferenceException($"\"{nameof(view)}\" is null");
            
            builder.RegisterInstance(settings);
            builder.RegisterInstance(manager);
            builder.RegisterInstance(context);
            
            builder.Register<LaunchOptions>(Lifetime.Singleton);
            builder.Register<LanPlatform>(Lifetime.Singleton).As<IConnectionPlatform>();
            builder.Register<PlatformService>(Lifetime.Singleton);
            builder.Register<SessionService>(Lifetime.Singleton);
            
            builder.RegisterComponent(view);
            builder.RegisterEntryPoint<AppLoop>();
        }
    }
}
