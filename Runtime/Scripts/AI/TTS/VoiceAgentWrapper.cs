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

        public void GenerateVoice(string text)
        {
            _voiceGenerator = VoiceGeneratorBase.Instance;
            
            if (_voiceGenerator == null)
            {
                Debug.LogError("[VoiceAgentWrapper] No Voice Generator assigned and no singleton instance found in the scene.");
                return;
            }

            StartCoroutine(StartTTSCoroutine(_voiceGenerator.TextToSpeech(text)));
        }

        private IEnumerator StartTTSCoroutine(Task<AudioClip> task)
        {
            yield return new WaitUntil(() => task.IsCompleted);
            
            if (task.IsFaulted)
            {
                Debug.LogError($"[VoiceAgentWrapper] TTS Generation failed: {task.Exception}");
                yield break;
            }

            _audioClip = task.Result;
            OnVoiceGenerationCompleted?.Invoke(_audioClip);
            
            if (_audioSource != null)
            {
                _audioSource.clip = _audioClip;
                _audioSource.Play();
            }
        }
    }
}
