using System;
using UnityEngine;
using UnityEngine.Events;
#if !UNITY_WEBGL
using Whisper;
#endif

namespace Conkist.GDK.SmartAgents
{
    /// <summary>
    /// Service responsible for recording microphone audio and transcribing it locally using Whisper.cpp (via WhisperUnity package).
    /// </summary>
    public class Record2TextService : MonoBehaviour
    {
#if !UNITY_WEBGL
        [Header("Local Whisper Settings")]
        [Tooltip("The WhisperManager instance to use. If empty, it will try to find one on this GameObject.")]
        [SerializeField] private WhisperManager _whisperManager;
#endif

        [Header("Recording Settings")]
        [SerializeField] private int _maxDurationSeconds = 60;

        [Tooltip("Whisper natively processes audio at 16000Hz. This ensures best quality.")]
        [SerializeField] private int _sampleRate = 16000;

        private string _activeMicrophone;
        private AudioClip _recordingClip;
        private bool _isRecording;

        public bool IsRecording => _isRecording;

        public UnityEvent<string> OnTranscriptionComplete;
        public UnityEvent<string> OnTranscriptionFailed;

#if !UNITY_WEBGL
        private void Start()
        {
            if (_whisperManager == null)
            {
                _whisperManager = GetComponent<WhisperManager>();
                if (_whisperManager == null)
                {
                    Debug.LogWarning("[Record2TextService] No WhisperManager assigned! Make sure to add the WhisperManager component to the scene.");
                }
            }

            if (Microphone.devices.Length > 0)
            {
                _activeMicrophone = Microphone.devices[0];
            }
            else
            {
                Debug.LogWarning("[Record2TextService] No microphone found on this device!");
            }
        }
#endif

        public void StartRecording()
        {
#if !UNITY_WEBGL
            if (_isRecording || string.IsNullOrEmpty(_activeMicrophone)) return;

            // Destroy the old recording clip to prevent memory leaks
            if (_recordingClip != null)
            {
                Destroy(_recordingClip);
                _recordingClip = null;
            }

            // Start Unity microphone
            _recordingClip = Microphone.Start(_activeMicrophone, false, _maxDurationSeconds, _sampleRate);
            _isRecording = true;
            Debug.Log($"[Record2TextService] Started recording on {_activeMicrophone} at {_sampleRate}Hz...");
#endif
        }

        public void StopRecordingAndTranscribeButton()
        {
#if !UNITY_WEBGL
            Debug.Log($"[Record2TextService] StopRecordingAndTranscribeButton called...");
            StopRecordingAndTranscribe();
#endif
        }

        public async void StopRecordingAndTranscribe(Action<string> onSuccess = null, Action<string> onError = null)
        {
#if !UNITY_WEBGL
            if (!_isRecording)
            {
                Debug.LogWarning("[Record2TextService] StopRecording called, but _isRecording was false. Did StartRecording run?");
                return;
            }

            int stopPosition = Microphone.GetPosition(_activeMicrophone);
            Microphone.End(_activeMicrophone);
            _isRecording = false;

            if (stopPosition <= 0)
            {
                Debug.LogError("[Record2TextService] Recording is too short or empty.");
                OnTranscriptionFailed?.Invoke("Recording is too short or empty.");
                onError?.Invoke("Recording is too short or empty.");
                return;
            }

            if (_whisperManager == null)
            {
                Debug.LogError("[Record2TextService] WhisperManager is not initialized.");
                OnTranscriptionFailed?.Invoke("WhisperManager is not initialized.");
                onError?.Invoke("WhisperManager is not initialized.");
                return;
            }

            // Fallback: If Whisper model isn't loaded and isn't loading, initialize it now
            if (!_whisperManager.IsLoaded && !_whisperManager.IsLoading)
            {
                Debug.LogWarning("[Record2TextService] Whisper model is not loaded yet. Initializing now...");
                try
                {
                    await _whisperManager.InitModel();
                }
                catch (Exception initEx)
                {
                    Debug.LogError($"[Record2TextService] Fallback Whisper model initialization failed: {initEx.Message}");

                    EventManager.TriggerEvent(new ShowPopupEvent(
                        "Speech Recognition Disabled",
                        "The Whisper Speech-to-Text model failed to initialize. The voice recording feature will be disabled.",
                        "OK",
                        "",
                        null,
                        null,
                        true
                    ));

                    OnTranscriptionFailed?.Invoke($"Model initialization failed: {initEx.Message}");
                    onError?.Invoke($"Model initialization failed: {initEx.Message}");
                    return;
                }
            }

            // Wait if it is currently loading
            while (_whisperManager.IsLoading)
            {
                await System.Threading.Tasks.Task.Yield();
            }

            if (!_whisperManager.IsLoaded)
            {
                Debug.LogError("[Record2TextService] Whisper model failed to load.");
                OnTranscriptionFailed?.Invoke("Whisper model failed to load.");
                onError?.Invoke("Whisper model failed to load.");
                return;
            }

            // Crop the AudioClip
            float[] samples = new float[stopPosition * _recordingClip.channels];
            _recordingClip.GetData(samples, 0);

            AudioClip croppedClip = AudioClip.Create("CroppedRecording", stopPosition, _recordingClip.channels, _recordingClip.frequency, false);
            croppedClip.SetData(samples, 0);

            Debug.Log($"[Record2TextService] Stopped recording. Starting local Whisper transcription...");

            try
            {
                // Await local transcription
                var res = await _whisperManager.GetTextAsync(croppedClip);
                string textResult = (res != null && !string.IsNullOrWhiteSpace(res.Result)) ? res.Result.Trim() : "";

                OnTranscriptionComplete?.Invoke(textResult);
                onSuccess?.Invoke(textResult);

                // Broadcast global GDK Event
                EventManager.TriggerEvent(new AgentTranscriptionEvent(textResult));
            }
            catch (Exception ex)
            {
                OnTranscriptionFailed?.Invoke($"Local transcription failed: {ex.Message}");
                onError?.Invoke($"Local transcription failed: {ex.Message}");
            }
            finally
            {
                if (croppedClip != null)
                {
                    Destroy(croppedClip);
                }
            }
#endif
        }
    }
}
