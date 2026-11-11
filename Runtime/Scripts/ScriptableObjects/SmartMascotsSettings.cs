using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Conkist.GDK.AI
{
    /// <summary>
    /// Global base configuration for Smart Mascots. Assign it to the ApplicationManager.
    /// Resolution order: scene components (defaults) -> this asset (base) -> ChatAgentWrapper "Agent Overrides".
    /// Keep assets that hold API keys out of git and out of player builds.
    /// </summary>
    [CreateAssetMenu(fileName = "SmartMascotsSettings", menuName = "Conkist/GDK/AI/SmartMascots Settings")]
    public class SmartMascotsSettings : ScriptableObject
    {
        [Tooltip("LLMUnity = offline (on device). OpenRouter = online.")]
        [FormerlySerializedAs("defaultProvider")]
        public LLMProvider provider = LLMProvider.LLMUnity;

        public OfflineLLMSettings offline = new OfflineLLMSettings();
        public OnlineLLMSettings online = new OnlineLLMSettings();
        public RagSettings rag = new RagSettings();
        public SpeechToTextSettings speechToText = new SpeechToTextSettings();
        public TextToSpeechSettings textToSpeech = new TextToSpeechSettings();
        public LipSyncSettings lipSync = new LipSyncSettings();

        /// <summary>The settings of the ApplicationManager in the scene, or null. Works before the manager's Awake.</summary>
        public static SmartMascotsSettings Current
        {
            get
            {
                if (ApplicationManager.HasInstance) return ApplicationManager.Instance.Settings;
                var manager = FindFirstObjectByType<ApplicationManager>(FindObjectsInactive.Include);
                return manager != null ? manager.Settings : null;
            }
        }
    }

    [Serializable]
    public class OfflineLLMSettings
    {
        [Tooltip("Base model: LLMUnity model file name, for example Qwen3.5-2B-Q4_K_M.gguf. Empty keeps the model set on the scene's LLM component.")]
        public string model;
        [Tooltip("Forces the answer to be complete, valid JSON ({thought, actions[]}) with a grammar, so small models cannot stop halfway. Ignored if the LLMAgent already has a grammar.")]
        public bool jsonGrammar = true;
        [Tooltip("Every action must pick one of the character's body animations (\"none\" is not allowed). Needs the JSON grammar.")]
        public bool requireAnimation = true;
        [Tooltip("Off = leave the scene's LLM and LLMAgent parameters below untouched.")]
        public bool applyToSceneLLM = true;
        public int contextSize = 4096;
        public int numGPULayers = 99;
        [Tooltip("-1 = automatic.")]
        public int numThreads = -1;
        [Tooltip("Thinking mode. Keep it off for small models and JSON output.")]
        public bool reasoning;
        [Range(0f, 2f)] public float temperature = 0.2f;
        [Tooltip("Maximum tokens per answer. -1 = no limit.")]
        public int numPredict = -1;
    }

    [Serializable]
    public class OnlineLLMSettings
    {
        public string apiKey;
        [Tooltip("OpenRouter model id or preset slug.")]
        public string model = "meta-llama/llama-3-8b-instruct:free";
    }

    /// <summary>Knowledge base retrieval. Always local: LLMUnity embeddings when available, otherwise the pure C# keyword index.</summary>
    [Serializable]
    public class RagSettings
    {
        public bool enabled = true;
        [Min(1)] public int topResults = 3;
        public bool forceRebuild;
        [Tooltip("Use LLMUnity embeddings when a RAG component exists (not on WebGL). Off = keyword index only.")]
        public bool preferEmbeddings = true;
    }

    [Serializable]
    public class SpeechToTextSettings
    {
        [Tooltip("Shows or hides the record button. The Whisper provider itself needs the CONKIST_WHISPER symbol.")]
        public bool showRecordButton = true;
        [Tooltip("On = the transcribed text is sent as a message. Off = it is written in the input so the user can review it and send it.")]
        public bool sendAutomatically = true;
    }

    [Serializable]
    public class TextToSpeechSettings
    {
        public bool enableElevenLabs;
        public string apiKey;
        public string voiceId = "21m00Tcm4TlvDq8ikWAM";
        public string modelId = "eleven_multilingual_v2";
    }

    [Serializable]
    public class LipSyncSettings
    {
        public bool enabled = true;
    }
}
