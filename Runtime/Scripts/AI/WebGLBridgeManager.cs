using UnityEngine;
using UnityEngine.Events;
using Conkist.GDK;

namespace Conkist.GDK.SmartAgents
{
    [System.Serializable]
    public class ScreenSizeData
    {
        public int width;
        public int height;
    }

    public class WebGLBridgeManager : MonoBehaviour
    {
        [Header("Volume Events")]
        public UnityEvent<float> OnMusicVolumeChanged;
        public UnityEvent<float> OnSFXVolumeChanged;
        public UnityEvent<float> OnVoiceVolumeChanged;
        public UnityEvent<bool> OnMuteStatusChanged;

        [Header("Theme & Color Mode Events")]
        public UnityEvent<string> OnColorModeChanged; // Emits "light" or "dark"
        public UnityEvent<bool> OnIsDarkModeChanged;  // Emits true for dark, false for light

        [Header("Layout & Sizing Events")]
        public UnityEvent<Vector2Int> OnScreenSizeChanged;
        public UnityEvent<int> OnScreenWidthChanged;
        public UnityEvent<int> OnScreenHeightChanged;

        private bool _isMuted = false;

        private static WebGLBridgeManager _instance;
        public static WebGLBridgeManager Instance => _instance;

        private void Awake()
        {
            if (gameObject.name == "AppManager")
            {
                Debug.Log("[WebGLBridgeManager] Detected on AppManager. Spawning dedicated, persistent WebGLBridgeManager GameObject...");
                
                // Create a unique, persistent GameObject for WebGLBridgeManager to prevent destruction by singleton managers
                var bridgeGO = new GameObject("WebGLBridgeManager");
                var newBridge = bridgeGO.AddComponent<WebGLBridgeManager>();
                
                // Copy all event handlers/references configured in the inspector
                newBridge.OnMusicVolumeChanged = OnMusicVolumeChanged;
                newBridge.OnSFXVolumeChanged = OnSFXVolumeChanged;
                newBridge.OnVoiceVolumeChanged = OnVoiceVolumeChanged;
                newBridge.OnMuteStatusChanged = OnMuteStatusChanged;
                newBridge.OnColorModeChanged = OnColorModeChanged;
                newBridge.OnIsDarkModeChanged = OnIsDarkModeChanged;
                newBridge.OnScreenSizeChanged = OnScreenSizeChanged;
                newBridge.OnScreenWidthChanged = OnScreenWidthChanged;
                newBridge.OnScreenHeightChanged = OnScreenHeightChanged;
                
                DontDestroyOnLoad(bridgeGO);
                return;
            }

            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            gameObject.name = "WebGLBridgeManager";
            Debug.Log("[WebGLBridgeManager] Persistent instance initialized successfully on GameObject: " + gameObject.name);
        }

        /// <summary>
        /// Receives music volume changes from the host page.
        /// </summary>
        /// <param name="volume">Volume between 0.0 and 1.0</param>
        public void SetMusicVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            Debug.Log($"[WebGLBridgeManager] SetMusicVolume: {clamped}");
            if (AudioManager.HasInstance)
            {
                AudioManager.Instance.MusicVolume = clamped;
            }
            OnMusicVolumeChanged?.Invoke(clamped);
        }

        /// <summary>
        /// Receives SFX volume changes from the host page.
        /// </summary>
        /// <param name="volume">Volume between 0.0 and 1.0</param>
        public void SetSFXVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            Debug.Log($"[WebGLBridgeManager] SetSFXVolume: {clamped}");
            if (AudioManager.HasInstance)
            {
                AudioManager.Instance.SFXVolume = clamped;
            }
            OnSFXVolumeChanged?.Invoke(clamped);
        }

        /// <summary>
        /// Receives voice volume changes from the host page.
        /// </summary>
        /// <param name="volume">Volume between 0.0 and 1.0</param>
        public void SetVoiceVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            Debug.Log($"[WebGLBridgeManager] SetVoiceVolume: {clamped}");
            if (AudioManager.HasInstance)
            {
                AudioManager.Instance.VoiceVolume = clamped;
            }
            OnVoiceVolumeChanged?.Invoke(clamped);
        }

        /// <summary>
        /// Mutes or unmutes all audio.
        /// </summary>
        /// <param name="mute">1 to mute, 0 to unmute</param>
        public void ToggleMute(int mute)
        {
            _isMuted = (mute != 0);
            Debug.Log($"[WebGLBridgeManager] ToggleMute: {_isMuted}");
            OnMuteStatusChanged?.Invoke(_isMuted);

            // Default fallback: alter global audio listener volume
            AudioListener.volume = _isMuted ? 0f : 1f;
        }

        /// <summary>
        /// Receives color mode / theme changes from the host page.
        /// </summary>
        /// <param name="mode">"light" or "dark"</param>
        public void SetColorMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            string cleanMode = mode.Trim().ToLowerInvariant();
            
            Debug.Log($"[WebGLBridgeManager] SetColorMode: {cleanMode}");
            OnColorModeChanged?.Invoke(cleanMode);
            OnIsDarkModeChanged?.Invoke(cleanMode == "dark");
        }

        /// <summary>
        /// Receives screen size modifications from the host page.
        /// Can accept formatted string "1920x1080", "1920,1080", or JSON payload.
        /// </summary>
        /// <param name="sizeStr">Width and height configuration</param>
        public void SetScreenSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return;
            Debug.Log($"[WebGLBridgeManager] SetScreenSize: {sizeStr}");

            int width = 0;
            int height = 0;

            // Try JSON parsing
            if (sizeStr.Trim().StartsWith("{"))
            {
                try
                {
                    var sizeData = JsonUtility.FromJson<ScreenSizeData>(sizeStr);
                    width = sizeData.width;
                    height = sizeData.height;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[WebGLBridgeManager] Failed to parse screen size JSON: {ex.Message}");
                }
            }
            else
            {
                // Fallback to splitting by separator characters
                string[] parts = sizeStr.Split(new char[] { ',', 'x', 'X', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[0].Trim(), out width);
                    int.TryParse(parts[1].Trim(), out height);
                }
            }

            if (width > 0 && height > 0)
            {
                OnScreenSizeChanged?.Invoke(new Vector2Int(width, height));
                OnScreenWidthChanged?.Invoke(width);
                OnScreenHeightChanged?.Invoke(height);
            }
        }
    }
}
