using VContainer;
using VContainer.Unity;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Registers the null providers first, then each provider found in <see cref="AIProviderRegistry"/>.
    /// VContainer resolves the last registration of a type, so a real provider replaces its null one.
    /// </summary>
    public sealed class AIProvidersInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance<ISpeechToText>(NullSpeechToText.Instance);
            builder.RegisterInstance<ITextToSpeech>(NullTextToSpeech.Instance);

            foreach (var install in AIProviderRegistry.All)
            {
                install(builder);
            }
        }
    }
}
