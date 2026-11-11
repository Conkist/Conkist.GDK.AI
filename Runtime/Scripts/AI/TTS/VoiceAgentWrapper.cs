using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using VContainer;
using Conkist.GDK.AI.Providers;

namespace Conkist.GDK.AI.TTS
{
    public class VoiceAgentWrapper : MonoBehaviour
    {
        [Inject] private ITextToSpeech _textToSpeech;
        [SerializeField] private AudioSource _audioSource;

        private AudioClip _audioClip;

        public UnityEvent<AudioClip> OnVoiceGenerationCompleted;
        public UnityEvent<string> OnVoiceGenerationFailed;

        private ITextToSpeech Tts => _textToSpeech ?? NullTextToSpeech.Instance;

        public bool HasValidProvider => Tts.IsAvailable;

        public void GenerateVoice(string text)
        {
            if (!TryGenerateVoice(text))
            {
                Debug.LogError("[VoiceAgentWrapper] No text-to-speech provider is enabled. Enable one in Conkist > GDK > AI > Setup ElevenLabs and add an AILifetimeScope to the scene.");
            }
        }

        public bool TryGenerateVoice(string text, string voiceId = null, System.Action<AudioClip> onCompleted = null, System.Action<string> onFailed = null)
        {
            if (!Tts.IsAvailable)
            {
                return false;
            }

            StartCoroutine(StartTTSCoroutine(Tts.TextToSpeech(text, voiceId), text, onCompleted, onFailed));
            return true;
        }

        private IEnumerator StartTTSCoroutine(Task<AudioClip> task, string originalText, System.Action<AudioClip> onCompleted = null, System.Action<string> onFailed = null)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            
            if (task.IsFaulted)
            {
                Debug.LogError($"[VoiceAgentWrapper] TTS Generation failed: {task.Exception}");
                OnVoiceGenerationFailed?.Invoke(originalText);
                onFailed?.Invoke(originalText);
                yield break;
            }

            _audioClip = task.Result;
            if (_audioClip == null)
            {
                Debug.LogWarning("[VoiceAgentWrapper] TTS generation completed with a null AudioClip.");
                OnVoiceGenerationFailed?.Invoke(originalText);
                onFailed?.Invoke(originalText);
                yield break;
            }

            OnVoiceGenerationCompleted?.Invoke(_audioClip);
            onCompleted?.Invoke(_audioClip);
            
            if (_audioSource != null)
            {
                _audioSource.clip = _audioClip;
                _audioSource.Play();
            }
        }
    }
}
