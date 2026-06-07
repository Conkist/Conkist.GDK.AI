using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Conkist.GDK.SmartAgents.TTS
{
    public class VoiceAgentWrapper : MonoBehaviour
    {
        private VoiceGeneratorBase _voiceGenerator;
        [SerializeField] private AudioSource _audioSource;

        private AudioClip _audioClip;

        public UnityEvent<AudioClip> OnVoiceGenerationCompleted;
        public UnityEvent<string> OnVoiceGenerationFailed;

        public bool HasValidProvider => VoiceGeneratorBase.Instance != null;

        public void GenerateVoice(string text)
        {
            if (!TryGenerateVoice(text))
            {
                Debug.LogError("[VoiceAgentWrapper] No Voice Generator assigned and no singleton instance found in the scene.");
            }
        }

        public bool TryGenerateVoice(string text)
        {
            _voiceGenerator = VoiceGeneratorBase.Instance;
            if (_voiceGenerator == null)
            {
                return false;
            }

            StartCoroutine(StartTTSCoroutine(_voiceGenerator.TextToSpeech(text), text));
            return true;
        }

        private IEnumerator StartTTSCoroutine(Task<AudioClip> task, string originalText)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            
            if (task.IsFaulted)
            {
                Debug.LogError($"[VoiceAgentWrapper] TTS Generation failed: {task.Exception}");
                OnVoiceGenerationFailed?.Invoke(originalText);
                yield break;
            }

            _audioClip = task.Result;
            if (_audioClip == null)
            {
                Debug.LogWarning("[VoiceAgentWrapper] TTS generation completed with a null AudioClip.");
                OnVoiceGenerationFailed?.Invoke(originalText);
                yield break;
            }

            OnVoiceGenerationCompleted?.Invoke(_audioClip);
            
            if (_audioSource != null)
            {
                _audioSource.clip = _audioClip;
                _audioSource.Play();
            }
        }
    }
}
