using UnityEngine;
using VContainer.Unity;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Put one in each scene that uses AI. It builds the container from the enabled providers and injects
    /// every component of the scene that asks for <see cref="ISpeechToText"/> or <see cref="ITextToSpeech"/>.
    /// Components created later can call <c>Container.Inject(this)</c>. Without a scope, consumers fall back to the null providers.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AILifetimeScope : LifetimeScope
    {
        protected override void Configure(VContainer.IContainerBuilder builder)
        {
            new AIProvidersInstaller().Install(builder);
        }

        protected override void Awake()
        {
            base.Awake();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour == this) continue;
                Container.Inject(behaviour);
            }
        }
    }
}
