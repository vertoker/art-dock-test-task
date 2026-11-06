using System;
using ArtDock.Networking;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ArtDock.Runtime
{
    public class ProjectScope : LifetimeScope
    {
        [SerializeField] private GameSettings settings;

        protected override void Awake()
        {
            DontDestroyOnLoad(gameObject);
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (!settings) throw new NullReferenceException($"\"{nameof(settings)}\" is null");

            builder.RegisterInstance(settings);
            builder.RegisterEntryPoint<AppLoop>();
        }
    }
}
