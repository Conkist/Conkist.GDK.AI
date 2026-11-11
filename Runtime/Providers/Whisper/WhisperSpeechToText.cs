using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Whisper;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Local speech to text with whisper.unity. Exists only while symbol CONKIST_WHISPER is on
    /// (this assembly has that define constraint). Consumers use <see cref="ISpeechToText"/>.
    /// </summary>
    public class WhisperSpeechToText : MonoBehaviour, ISpeechToText
    {
        private const string FallbackModel = "Whisper/ggml-tiny.bin";

        [Header("Local Whisper Settings")]
        [Tooltip("The WhisperManager to use. If empty, it is taken from this GameObject.")]
        [SerializeField] private WhisperManager _whisperManager;

        [Header("Recording Settings")]
        [SerializeField] private int _maxDurationSeconds = 60;

        [Tooltip("Whisper natively processes audio at 16000Hz. This ensures best quality.")]
        [SerializeField] private int _sampleRate = 16000;

        private string _activeMicrophone;
        private AudioClip _recordingClip;
        private bool _isRecording;

        public bool IsAvailable => true;
        public bool IsReady => _whisperManager != null && _whisperManager.IsLoaded;
        public bool IsRecording => _isRecording;
        public event Action<string> TranscriptionFailed;

        private void Awake()
        {
            if (_whisperManager == null) _whisperManager = GetComponent<WhisperManager>();
            if (_whisperManager == null)
            {
                Debug.LogWarning("[WhisperSpeechToText] No WhisperManager assigned. Add a WhisperManager to this GameObject.");
            }
        }

        private void Start()
        {
            if (Microphone.devices.Length > 0)
            {
                _activeMicrophone = Microphone.devices[0];
            }
            else
            {
                Debug.LogWarning("[WhisperSpeechToText] No microphone found on this device.");
            }
        }

        /// <summary>
        /// Copies the model from StreamingAssets to persistentDataPath when needed (required on Android),
        /// then loads it. Falls back to the tiny model if the configured one is missing.
        /// </summary>
        public async Task<bool> InitializeAsync(IProgress<float> progress = null)
        {
            if (IsReady) return true;
            if (_whisperManager == null)
            {
                return Fail("Whisper Speech-to-Text component was not found. The voice recording feature will be disabled.");
            }

            string model = _whisperManager.ModelPath;
            string source = Path.Combine(Application.streamingAssetsPath, model);

            if (Application.platform != RuntimePlatform.Android && !File.Exists(source))
            {
                Debug.LogWarning($"[WhisperSpeechToText] Model '{model}' not found. Falling back to '{FallbackModel}'.");
                model = FallbackModel;
                source = Path.Combine(Application.streamingAssetsPath, model);
                if (!File.Exists(source)) return Fail("The Whisper Speech-to-Text model file was not found. The voice recording feature will be disabled.");
            }

            string dest = Path.Combine(Application.persistentDataPath, model);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));

            if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
            {
                bool copied = await CopyModelAsync(source, dest, progress);
                if (!copied && Application.platform == RuntimePlatform.Android && model != FallbackModel)
                {
                    model = FallbackModel;
                    source = Path.Combine(Application.streamingAssetsPath, model);
                    dest = Path.Combine(Application.persistentDataPath, model);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    copied = File.Exists(dest) && new FileInfo(dest).Length > 0 || await CopyModelAsync(source, dest, progress);
                }
                if (!copied) return Fail("The Whisper Speech-to-Text model could not be copied on this device. The voice recording feature will be disabled.");
            }
            progress?.Report(0.8f);

            _whisperManager.IsModelPathInStreamingAssets = false;
            _whisperManager.ModelPath = dest;

            try
            {
                var init = _whisperManager.InitModel();
                float elapsed = 0f;
                while (!init.IsCompleted)
                {
                    elapsed += Time.deltaTime;
                    progress?.Report(Mathf.Lerp(0.8f, 0.98f, Mathf.Clamp01(elapsed / 1.5f)));
                    await Task.Yield();
                }
                await init; // rethrows a faulted init
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WhisperSpeechToText] Failed to initialize model: {ex}");
                return Fail("The Whisper Speech-to-Text model failed to load. The voice recording feature will be disabled.");
            }

            progress?.Report(1f);
            return true;
        }

        public void StartRecording()
        {
            if (_isRecording || string.IsNullOrEmpty(_activeMicrophone)) return;

            if (_recordingClip != null)
            {
                Destroy(_recordingClip);
                _recordingClip = null;
            }

            _recordingClip = Microphone.Start(_activeMicrophone, false, _maxDurationSeconds, _sampleRate);
            _isRecording = true;
        }

        public async Task<string> StopAndTranscribeAsync()
        {
            if (!_isRecording)
            {
                Debug.LogWarning("[WhisperSpeechToText] Stop called, but no recording was running.");
                return null;
            }

            int stopPosition = Microphone.GetPosition(_activeMicrophone);
            Microphone.End(_activeMicrophone);
            _isRecording = false;

            if (stopPosition <= 0) return Failed("Recording is too short or empty.");
            if (_whisperManager == null) return Failed("WhisperManager is not initialized.");

            if (!_whisperManager.IsLoaded && !_whisperManager.IsLoading && !await InitializeAsync())
            {
                return Failed("Model initialization failed.");
            }
            while (_whisperManager.IsLoading) await Task.Yield();
            if (!_whisperManager.IsLoaded) return Failed("Whisper model failed to load.");

            var samples = new float[stopPosition * _recordingClip.channels];
            _recordingClip.GetData(samples, 0);
            var cropped = AudioClip.Create("CroppedRecording", stopPosition, _recordingClip.channels, _recordingClip.frequency, false);
            cropped.SetData(samples, 0);

            try
            {
                var res = await _whisperManager.GetTextAsync(cropped);
                string text = res != null && !string.IsNullOrWhiteSpace(res.Result) ? res.Result.Trim() : "";
                EventManager.TriggerEvent(new AgentTranscriptionEvent(text));
                return text;
            }
            catch (Exception ex)
            {
                return Failed($"Local transcription failed: {ex.Message}");
            }
            finally
            {
                if (cropped != null) Destroy(cropped);
            }
        }

        private static async Task<bool> CopyModelAsync(string source, string dest, IProgress<float> progress)
        {
            try
            {
                if (File.Exists(dest)) File.Delete(dest);

                if (Application.platform == RuntimePlatform.Android)
                {
                    using (var request = UnityWebRequest.Get(source))
                    {
                        request.downloadHandler = new DownloadHandlerFile(dest);
                        var op = request.SendWebRequest();
                        while (!op.isDone)
                        {
                            progress?.Report(request.downloadProgress * 0.8f);
                            await Task.Yield();
                        }
                        if (request.result == UnityWebRequest.Result.Success) return true;
                        Debug.LogWarning($"[WhisperSpeechToText] Copy of '{source}' failed: {request.error}");
                    }
                }
                else
                {
                    progress?.Report(0f);
                    File.Copy(source, dest, true);
                    progress?.Report(0.8f);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WhisperSpeechToText] Failed to copy model: {ex.Message}");
            }

            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            return false;
        }

        private bool Fail(string userMessage)
        {
            EventManager.TriggerEvent(new ShowPopupEvent("Speech Recognition Disabled", userMessage, "OK", "", null, null, true));
            TranscriptionFailed?.Invoke(userMessage);
            return false;
        }

        private string Failed(string reason)
        {
            Debug.LogError($"[WhisperSpeechToText] {reason}");
            TranscriptionFailed?.Invoke(reason);
            return null;
        }
    }
}
