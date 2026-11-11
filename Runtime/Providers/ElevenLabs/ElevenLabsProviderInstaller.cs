using UnityEngine;
using VContainer;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>Adds the ElevenLabs provider to the registry. This assembly exists only while CONKIST_ELEVENLABS is on.</summary>
    internal static class ElevenLabsProviderInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            AIProviderRegistry.Register("ElevenLabs", builder =>
            {
                var settings = SmartMascotsSettings.Current;
                if (settings != null && !settings.textToSpeech.enableElevenLabs) return; // off: null TTS stays, chat answers in text

                var tts = Object.FindFirstObjectByType<ElevenLabsTextToSpeech>(FindObjectsInactive.Include);
                if (tts == null) return;

                if (settings != null)
                {
                    if (!string.IsNullOrEmpty(settings.textToSpeech.apiKey)) tts.apiKey = settings.textToSpeech.apiKey;
                    if (!string.IsNullOrEmpty(settings.textToSpeech.voiceId)) tts.voiceId = settings.textToSpeech.voiceId;
                    if (!string.IsNullOrEmpty(settings.textToSpeech.modelId)) tts.modelId = settings.textToSpeech.modelId;
                }
                builder.RegisterInstance<ITextToSpeech>(tts);
            });
        }
    }
}
