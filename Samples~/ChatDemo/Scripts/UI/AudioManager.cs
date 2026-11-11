using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Mixer Routing")]
    [SerializeField] private AudioMixer _audioMixer;
    [SerializeField] private AudioMixerGroup _musicGroup;
    [SerializeField] private AudioMixerGroup _sfxGroup;

    [Header("Background Music")]
    [SerializeField] private AudioClip _bgMusicClip;

    [Header("Sound Effects (SFX)")]
    [SerializeField] private AudioClip _buttonClickClip;
    [SerializeField] private AudioClip _keyboardTypeClip;
    [SerializeField] private AudioClip _optionInClip;
    [SerializeField] private AudioClip _optionOutClip;

    [Header("Typing Sound Settings")]
    [SerializeField] private float _minPitch = 0.90f;
    [SerializeField] private float _maxPitch = 1.10f;

    private AudioSource _bgMusicSource;
    private AudioSource _sfxSource;
    private AudioSource _typingSource; // Dedicated source to allow rapid typing clicks with varied pitches

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize AudioSource channels dynamically
        InitializeSources();
    }

    private void Start()
    {
        // Start playing the background music on loop
        PlayBackgroundMusic();
    }

    private void InitializeSources()
    {
        // 1. Background Music Source
        _bgMusicSource = gameObject.AddComponent<AudioSource>();
        _bgMusicSource.loop = true;
        _bgMusicSource.playOnAwake = false;
        if (_musicGroup != null)
        {
            _bgMusicSource.outputAudioMixerGroup = _musicGroup;
        }

        // 2. Standard SFX Source (clicks, panel transitions)
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;
        if (_sfxGroup != null)
        {
            _sfxSource.outputAudioMixerGroup = _sfxGroup;
        }

        // 3. Dedicated Typing Sound Source (prevents cutting off other SFX)
        _typingSource = gameObject.AddComponent<AudioSource>();
        _typingSource.loop = false;
        _typingSource.playOnAwake = false;
        if (_sfxGroup != null)
        {
            _typingSource.outputAudioMixerGroup = _sfxGroup;
        }
    }

    public void PlayBackgroundMusic()
    {
        if (_bgMusicSource != null && _bgMusicClip != null)
        {
            _bgMusicSource.clip = _bgMusicClip;
            _bgMusicSource.Play();
            Debug.Log("[AudioManager] Looping background music started.");
        }
    }

    public void PlayButtonClick()
    {
        if (_sfxSource != null && _buttonClickClip != null)
        {
            _sfxSource.pitch = 1.0f; // Reset pitch to normal
            _sfxSource.PlayOneShot(_buttonClickClip);
        }
    }

    public void PlayKeyboardType()
    {
        if (_typingSource != null && _keyboardTypeClip != null)
        {
            // Apply randomized pitch for mechanical typing sound variability
            _typingSource.pitch = Random.Range(_minPitch, _maxPitch);
            _typingSource.PlayOneShot(_keyboardTypeClip);
        }
    }

    public void PlayOptionIn()
    {
        if (_sfxSource != null && _optionInClip != null)
        {
            _sfxSource.pitch = 1.0f;
            _sfxSource.PlayOneShot(_optionInClip);
        }
    }

    public void PlayOptionOut()
    {
        if (_sfxSource != null && _optionOutClip != null)
        {
            _sfxSource.pitch = 1.0f;
            _sfxSource.PlayOneShot(_optionOutClip);
        }
    }

    public void SetMasterVolume(float volume)
    {
        // Enforce safe volume ranges
        volume = Mathf.Clamp(volume, 0.0001f, 1.0f);
        
        // Logarithmic dB scale: 20 * log10(volume)
        float db = Mathf.Log10(volume) * 20.0f;

        if (_audioMixer != null)
        {
            // Set parameter on the mixer
            bool success = _audioMixer.SetFloat("Master", db);
            if (!success)
            {
                // Fallback to setting AudioListener if parameter is not exposed
                AudioListener.volume = volume;
            }
            Debug.Log($"[AudioManager] Mixer Master volume updated: {db:F1} dB (Raw: {volume:P0})");
        }
        else
        {
            // Standard fallback
            AudioListener.volume = volume;
            Debug.Log($"[AudioManager] AudioListener volume updated: {volume:P0}");
        }
    }
}
