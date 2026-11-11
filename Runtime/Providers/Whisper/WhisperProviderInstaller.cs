using UnityEngine;
using VContainer;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>Adds the Whisper provider to the registry. This assembly exists only while CONKIST_WHISPER is on.</summary>
    internal static class WhisperProviderInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AIProviderRegistry.Register("Whisper", builder =>
            {
                var stt = Object.FindFirstObjectByType<WhisperSpeechToText>(FindObjectsInactive.Include);
                if (stt != null) builder.RegisterInstance<ISpeechToText>(stt);
            });
        }
    }
}
