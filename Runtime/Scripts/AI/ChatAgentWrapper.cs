using UnityEngine;
using UnityEngine.Events;
#if !UNITY_WEBGL
using LLMUnity;
#endif
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Conkist.GDK.Serialization;
using Conkist.GDK;

namespace Conkist.GDK.AI
{
    public enum LLMProvider
    {
        LLMUnity,
        OpenRouter
    }

    [DefaultExecutionOrder(-200)] // before LLMUnity's LLM.Awake, so model overrides land before the server starts
    public class ChatAgentWrapper : AgentWrapper
    {
        [Header("Agent Overrides (applied after SmartMascotsSettings)")]
        [SerializeField] private bool _overrideProvider;
        [SerializeField] private LLMProvider _provider = LLMProvider.LLMUnity;
        [SerializeField] private bool _overrideModel;
        [Tooltip("LLMUnity model file name when the provider is LLMUnity; OpenRouter model id or preset slug when online.")]
        [SerializeField] private string _model;

        /// <summary>Provider after the agent override. Setting it writes the agent override only; the shared settings asset is never changed.</summary>
        public LLMProvider ActiveProvider
        {
            get => ResolveProvider();
            set
            {
                _overrideProvider = true;
                _provider = value;
                UpdateActiveProviderCache();
            }
        }

        private static SmartMascotsSettings GetApplicationSettings() => SmartMascotsSettings.Current;

        // Scene defaults -> SmartMascotsSettings (base) -> this agent's override.
        private LLMProvider ResolveProvider()
        {
            if (_overrideProvider) return _provider;
            var settings = GetApplicationSettings();
            return settings != null ? settings.provider : LLMProvider.LLMUnity;
        }

        private string ResolveModel(LLMProvider provider, SmartMascotsSettings settings)
        {
            if (_overrideModel && !string.IsNullOrEmpty(_model)) return _model;
            if (settings == null) return null;
            return provider == LLMProvider.LLMUnity ? settings.offline.model : settings.online.model;
        }

        // Needs the grammar: without it nothing stops the model from answering "none".
        private bool RequireAnimation
        {
            get
            {
                var offline = GetApplicationSettings()?.offline;
                return offline == null || (offline.jsonGrammar && offline.requireAnimation);
            }
        }

        private bool RagEnabled => GetApplicationSettings()?.rag.enabled ?? true;
        private int RagTopResults => Mathf.Max(1, GetApplicationSettings()?.rag.topResults ?? 3);
        private bool RagForceRebuild => GetApplicationSettings()?.rag.forceRebuild ?? false;
        private bool RagPreferEmbeddings => GetApplicationSettings()?.rag.preferEmbeddings ?? true;

        [Header("Prompt Settings")]
        [Tooltip("Guardrails, modifier logic, output format and JSON schema. Saved in the prefab. Empty = built-in default.")]
        [TextArea(5, 10)]
        [SerializeField] private string _systemPromptPrefix;

        [SerializeField] private OpenRouterClient _openRouterClient;

#if !UNITY_WEBGL
        [Header("RAG (settings are in SmartMascotsSettings > RAG)")]
        [SerializeField] private RAG _rag;
#endif

        // JSON schema for {thought, actions[]}; same shape as AgentFeedback / AgentActions.
        // When the character has animations, "task" is limited to "none" or animate(<one of its keys>), so the model cannot invent names.
        private static string BuildActionsJsonSchema(CharacterSO characterSO, bool allowNone)
        {
            string task = "{\"type\":\"string\"}";
            if (characterSO != null && characterSO.animations != null && characterSO.animations.Length > 0)
            {
                // Animation keys first; "none" only when allowed, and then last.
                var values = new StringBuilder();
                foreach (var anim in characterSO.animations)
                {
                    if (anim == null || string.IsNullOrEmpty(anim.key)) continue;
                    values.Append("\"animate(").Append(anim.key.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append(")\",");
                }
                if (allowNone) values.Append("\"none\"");
                task = "{\"enum\":[" + values.ToString().TrimEnd(',') + "]}";
            }

            return "{\"type\":\"object\",\"properties\":{" +
                "\"thought\":{\"type\":\"string\"}," +
                "\"actions\":{\"type\":\"array\",\"maxItems\":3,\"items\":{\"type\":\"object\",\"properties\":{" +
                "\"message\":{\"type\":\"string\"}," +
                "\"pleasure_mod\":{\"type\":\"number\"}," +
                "\"arousal_mod\":{\"type\":\"number\"}," +
                "\"dominance_mod\":{\"type\":\"number\"}," +
                "\"task\":" + task + "," +
                "\"target\":{\"type\":\"string\"}," +
                "\"duration\":{\"type\":\"number\"}}," +
                "\"required\":[\"message\",\"pleasure_mod\",\"arousal_mod\",\"dominance_mod\",\"task\",\"target\",\"duration\"]," +
                "\"additionalProperties\":false}}}," +
                "\"required\":[\"thought\",\"actions\"],\"additionalProperties\":false}";
        }

        private const string DefaultSystemPromptPrefix =
            "You are a companion character in a game/experience. Respond in a JSON format matching the schema requested below. " +
            "If you choose an animate task, use only exact animation keys from the character's animation list. " +
            "Do not paraphrase, shorten, or invent animation names. If no exact match exists, use task = \"none\".";

#if !UNITY_WEBGL
        private LLMAgent _llmAgent;
#endif
        private readonly KeywordKnowledgeIndex _keywordIndex = new KeywordKnowledgeIndex();
        private bool _embeddingRagReady;
        private string _systemPrompt;
        private bool _isQuitting = false;
        private LLMProvider _activeProviderRuntime = LLMProvider.LLMUnity;

        protected override void Awake()
        {
            base.Awake();
#if !UNITY_WEBGL
            TryGetComponent<LLMAgent>(out _llmAgent);
#endif
            UpdateActiveProviderCache();
#if !UNITY_WEBGL
            ApplyLLMUnityOverrides();
#endif
#if !UNITY_WEBGL
            if (_activeProviderRuntime == LLMProvider.LLMUnity && _llmAgent == null)
            {
                Debug.LogError("[ChatAgentWrapper] LLMUnity provider is selected, but LLMAgent component is missing on this GameObject!");
            }
#endif
        }

#if !UNITY_WEBGL
        /// <summary>
        /// Base: SmartMascotsSettings.offline. Then the agent override model. Runs before the LLM starts.
        /// </summary>
        private void ApplyLLMUnityOverrides()
        {
            if (_llmAgent == null) return;
            if (_activeProviderRuntime != LLMProvider.LLMUnity) return;

            var settings = GetApplicationSettings();

            // Grammar-constrained output: the model cannot stop in the middle of the JSON.
            if ((settings?.offline.jsonGrammar ?? true) && string.IsNullOrEmpty(_llmAgent.grammar))
            {
                _llmAgent.grammar = BuildActionsJsonSchema(characterActor != null ? characterActor.characterSO : null, !RequireAnimation);
            }

            if (_llmAgent.remote) return;
            // LLMAgent links its LLM in its own Awake, which runs after this one; look it up here.
            var llm = _llmAgent.llm != null ? _llmAgent.llm : FindFirstObjectByType<LLM>(FindObjectsInactive.Include);
            if (llm == null) return;
            if (llm.started)
            {
                Debug.LogWarning("[ChatAgentWrapper] LLM already started; model and settings overrides skipped.");
                return;
            }

            string model = ResolveModel(LLMProvider.LLMUnity, settings);
            if (!string.IsNullOrEmpty(model)) llm.SetModel(model);

            if (settings == null || !settings.offline.applyToSceneLLM) return;
            var o = settings.offline;
            llm.contextSize = o.contextSize;
            // -1 splits the context across every client (agent + embedder), leaving too little per slot for the prompt: answers got cut mid-JSON.
            llm.parallelPrompts = 1;
            llm.numGPULayers = o.numGPULayers;
            llm.numThreads = o.numThreads;
            // The reasoning setter talks to the native service, which does not exist before the LLM starts.
            if (llm.reasoning != o.reasoning) _ = ApplyReasoningWhenReady(llm, o.reasoning);
            _llmAgent.temperature = o.temperature;
            _llmAgent.numPredict = o.numPredict;
        }

        private static async System.Threading.Tasks.Task ApplyReasoningWhenReady(LLM llm, bool reasoning)
        {
            try
            {
                await llm.WaitUntilReady();
                if (llm != null && llm.started) llm.SetReasoning(reasoning);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ChatAgentWrapper] Could not set thinking mode: {ex.Message}");
            }
        }
#endif

        private void UpdateActiveProviderCache()
        {
            _activeProviderRuntime = ActiveProvider;
        }

        private string BuildSystemPrompt(CharacterSO characterSO)
        {
            string basePrompt = string.IsNullOrEmpty(_systemPromptPrefix)
                ? DefaultSystemPromptPrefix
                : _systemPromptPrefix;

            string characterPrompt = characterSO?.defaultPrompt ?? "";
            string animationSuffix = BuildAnimationSystemPromptSuffix(characterSO);

            if (!string.IsNullOrEmpty(characterPrompt))
            {
                if (!basePrompt.Contains(characterPrompt))
                {
                    return characterPrompt + "\n\n" + basePrompt + "\n\n" + animationSuffix;
                }
                return basePrompt + "\n\n" + animationSuffix;
            }

            return basePrompt + "\n\n" + animationSuffix;
        }

        private string BuildAnimationSystemPromptSuffix(CharacterSO characterSO)
        {
            if (characterSO == null || characterSO.animations == null || characterSO.animations.Length == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            sb.AppendLine("[Animation Rules]");
            sb.AppendLine(RequireAnimation
                ? "Every action must set task = \"animate(key)\" using exactly one of these keys: pick the closest one. Do not invent, paraphrase, shorten, or normalize names."
                : "To perform a body animation, set task = \"animate(key)\" using exactly one of these keys. Do not invent, paraphrase, shorten, or normalize names. If none fits, set task = \"none\".");
            foreach (var anim in characterSO.animations)
            {
                if (anim == null || string.IsNullOrEmpty(anim.key)) continue;
                sb.Append("- ").Append(anim.key);
                if (!string.IsNullOrEmpty(anim.description)) sb.Append(": ").Append(anim.description);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private string[] FindRelevantAnimationKeys(string userText, CharacterSO characterSO)
        {
            if (characterSO == null || characterSO.animations == null || characterSO.animations.Length == 0 || string.IsNullOrEmpty(userText))
            {
                return System.Array.Empty<string>();
            }

            string lowerText = userText.ToLowerInvariant();
            string[] tokens = lowerText.Split(new char[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '-', '_', '/', '\\', '"', '\'', '(', ')', '[', ']', '{', '}' }, System.StringSplitOptions.RemoveEmptyEntries);
            var scores = new Dictionary<string, int>();

            foreach (var anim in characterSO.animations)
            {
                if (anim == null || string.IsNullOrEmpty(anim.key))
                {
                    continue;
                }

                string keyLower = anim.key.ToLowerInvariant();
                string normalizedKey = keyLower.Replace('-', ' ');
                string descriptionLower = (anim.description ?? "").ToLowerInvariant();

                int score = 0;
                if (lowerText.Contains(keyLower))
                {
                    score += 100;
                }
                if (lowerText.Contains(normalizedKey))
                {
                    score += 60;
                }

                foreach (var token in tokens)
                {
                    if (token.Length < 3) continue;
                    if (normalizedKey.Contains(token)) score += 10;
                    if (descriptionLower.Contains(token)) score += 5;
                }

                if (score > 0)
                {
                    scores[anim.key] = score;
                }
            }

            if (scores.Count == 0)
            {
                return System.Array.Empty<string>();
            }

            var entries = new List<KeyValuePair<string, int>>(scores);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));

            var keys = new List<string>();
            foreach (var entry in entries)
            {
                if (!keys.Contains(entry.Key))
                {
                    keys.Add(entry.Key);
                    if (keys.Count >= 10) break;
                }
            }

            return keys.ToArray();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        /// <summary>
        /// Safely cancels any in-flight LLMUnity requests.
        /// Wrapped in try/catch because the LLMUnity native service may already
        /// be shut down during Play Mode exit, causing a Mono SIGSEGV if called
        /// on a dangling native handle.
        /// </summary>
        private void SafelyCancelLLMRequests()
        {
#if !UNITY_WEBGL
            if (_isQuitting) return;
            if (_activeProviderRuntime != LLMProvider.LLMUnity) return;
            if (_llmAgent == null) return;

            // If local client (not remote), verify the LLM component is valid and started
            if (!_llmAgent.remote)
            {
                if (_llmAgent.llm == null || !_llmAgent.llm.started) return;
            }

            try
            {
                _llmAgent.CancelRequests();
            }
            catch (System.Exception ex)
            {
                // Swallow — the native LLM service is already torn down during scene
                // teardown. Letting this propagate crashes Unity with a SIGSEGV.
                Debug.LogWarning($"[ChatAgentWrapper] Suppressed exception during LLM cancel (teardown race): {ex.Message}");
            }
#endif
        }

        protected override async void Start()
        {
            base.Start();
            UpdateActiveProviderCache();
            InitializeSystemPrompt();

            if (RagEnabled)
            {
                await InitializeRAG();
            }
        }

        private void InitializeSystemPrompt()
        {
            if (characterActor == null || characterActor.characterSO == null)
            {
                return;
            }

            _systemPrompt = BuildSystemPrompt(characterActor.characterSO);

            Debug.Log($"[ChatAgentWrapper] Final system prompt for {characterActor.characterSO.defaultName}:\n{_systemPrompt}");

            if (ActiveProvider == LLMProvider.OpenRouter)
            {
                if (_openRouterClient != null)
                {
                    ConfigureOpenRouterClient();
                    _openRouterClient.Initialize(_systemPrompt);
                }
            }
#if !UNITY_WEBGL
            else if (_llmAgent != null)
            {
                _llmAgent.systemPrompt = _systemPrompt;
            }
#endif

            Debug.Log($"[ChatAgentWrapper] Initialized static system prompt for: {characterActor.characterSO.defaultName} using provider: {ActiveProvider}");
        }

        private void ConfigureOpenRouterClient()
        {
            if (_openRouterClient == null) return;

            var settings = GetApplicationSettings();
            if (settings != null && !string.IsNullOrEmpty(settings.online.apiKey)) _openRouterClient.ApiKey = settings.online.apiKey;

            // Base from settings, then the agent override; otherwise whatever is serialized on the client.
            string model = ResolveModel(LLMProvider.OpenRouter, settings);
            if (!string.IsNullOrEmpty(model)) _openRouterClient.Model = model;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!_isQuitting)
            {
                SafelyCancelLLMRequests();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (!_isQuitting)
            {
                SafelyCancelLLMRequests();
            }
        }

        public override void ClearHistory()
        {
            base.ClearHistory();

#if !UNITY_WEBGL
            if (ActiveProvider == LLMProvider.LLMUnity && _llmAgent != null)
            {
                _ = _llmAgent.ClearHistory();
            }
#else
            if (ActiveProvider == LLMProvider.LLMUnity)
            {
                Debug.LogError("[ChatAgentWrapper] LLMUnity is not supported on WebGL builds. Please use OpenRouter or WebGLAgentWrapper.");
            }
#endif
            else if (ActiveProvider == LLMProvider.OpenRouter && _openRouterClient != null)
            {
                _openRouterClient.ClearHistory();
            }
        }

        /// <summary>Knowledge paragraphs as separate RAG documents. The animation list is in the system prompt, not here.</summary>
        private static List<string> CollectKnowledgeDocuments(CharacterSO characterSO)
        {
            var docs = new List<string>();

            if (characterSO.knowledgeFiles != null)
            {
                foreach (var file in characterSO.knowledgeFiles)
                {
                    if (file == null) continue;
                    foreach (string paragraph in file.text.Split(new[] { "\n\n", "\r\n\r\n" }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string cleaned = paragraph.Trim();
                        if (!string.IsNullOrEmpty(cleaned)) docs.Add(cleaned);
                    }
                }
            }

            return docs;
        }

        /// <summary>
        /// Embedding RAG (LLMUnity) when available and configured; otherwise the pure C# keyword index,
        /// which also runs on WebGL.
        /// </summary>
        private async System.Threading.Tasks.Task InitializeRAG()
        {
            if (characterActor == null || characterActor.characterSO == null)
            {
                Debug.LogWarning("[ChatAgentWrapper] Cannot initialize RAG: CharacterActor or CharacterSO is unassigned.");
                return;
            }

            var characterSO = characterActor.characterSO;
            List<string> docs = CollectKnowledgeDocuments(characterSO);
            if (docs.Count == 0)
            {
                Debug.LogWarning("[ChatAgentWrapper] No knowledge files or animation list are configured in CharacterSO.");
                return;
            }

#if !UNITY_WEBGL
            string savePath = characterSO.ragSavePath;
            if (_rag != null && RagPreferEmbeddings && !string.IsNullOrEmpty(savePath))
            {
                bool loaded = false;
                if (!RagForceRebuild)
                {
                    loaded = await _rag.Load(savePath);
                }

                if (!loaded)
                {
                    Debug.Log($"[ChatAgentWrapper] RAG save file '{savePath}' not found or force rebuild enabled. Creating embeddings...");
                    _rag.Clear();

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    foreach (string doc in docs)
                    {
                        await _rag.Add(doc);
                    }
                    stopwatch.Stop();

                    Debug.Log($"[ChatAgentWrapper] Embedded {_rag.Count()} phrases in {stopwatch.Elapsed.TotalSeconds}s.");
                    _rag.Save(savePath);
                }
                else
                {
                    Debug.Log($"[ChatAgentWrapper] Loaded existing RAG database '{savePath}' with {_rag.Count()} phrases.");
                }

                _embeddingRagReady = _rag.Count() > 0;
                return;
            }
#endif

            _keywordIndex.Clear();
            foreach (string doc in docs)
            {
                _keywordIndex.Add(doc);
            }
            Debug.Log($"[ChatAgentWrapper] Keyword RAG ready with {_keywordIndex.Count} documents.");
            await System.Threading.Tasks.Task.Yield();
        }

        public override void ForceMessageToAgent(string text)
        {
            Debug.Log("Forcing message!");
            OnMessageSubmitted(text);
        }

        private async void OnMessageSubmitted(string text)
        {
            await System.Threading.Tasks.Task.Yield();

            StopActiveFeedback();

            Debug.Log("Show Typing Indicator!");
            SetTyping(true);

            StringBuilder promptBuilder = new StringBuilder();
            
            // Dynamic State Injection (emotion state updated dynamically per turn)
            if (characterActor != null)
            {
                promptBuilder.AppendLine($"[State: Pleasure={characterActor.currentPleasure}, Arousal={characterActor.currentArousal}, Dominance={characterActor.currentDominance}]");

                if (characterActor.characterSO != null && characterActor.characterSO.animations != null && characterActor.characterSO.animations.Length > 0)
                {
                    string[] relevantKeys = FindRelevantAnimationKeys(text, characterActor.characterSO);
                    if (relevantKeys.Length == 0 && characterActor.characterSO.animations.Length <= 20)
                    {
                        var fallbackKeys = new List<string>();
                        foreach (var anim in characterActor.characterSO.animations)
                        {
                            if (anim != null && !string.IsNullOrEmpty(anim.key))
                            {
                                fallbackKeys.Add(anim.key);
                            }
                        }
                        relevantKeys = fallbackKeys.ToArray();
                    }

                    if (relevantKeys.Length > 0)
                    {
                        promptBuilder.AppendLine("[Relevant animation keys for this request:]");
                        foreach (var key in relevantKeys)
                        {
                            promptBuilder.AppendLine($"- \"{key}\"");
                        }
                        promptBuilder.AppendLine("Only use one of the exact keys above for task = \"animate(animation_name)\". Do not invent, shorten, or paraphrase names.");
                        promptBuilder.AppendLine();
                    }
                }
            }

            // Dynamic RAG Context Injection
            if (RagEnabled)
            {
                try
                {
                    string[] similarPhrases = null;
#if !UNITY_WEBGL
                    if (_embeddingRagReady)
                    {
                        (similarPhrases, _) = await _rag.Search(text, RagTopResults);
                    }
#endif
                    if (similarPhrases == null && _keywordIndex.Count > 0)
                    {
                        similarPhrases = _keywordIndex.Search(text, RagTopResults);
                    }

                    if (similarPhrases != null && similarPhrases.Length > 0)
                    {
                        promptBuilder.AppendLine("[Context: Use this knowledge from your memory if relevant:]");
                        foreach (var phrase in similarPhrases)
                        {
                            promptBuilder.AppendLine($"- {phrase}");
                        }
                        promptBuilder.AppendLine();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ChatAgentWrapper] RAG search failed: {ex.Message}.");
                }
            }

            promptBuilder.AppendLine("User Message:");
            promptBuilder.Append(text);

            string promptToSend = promptBuilder.ToString();
            Debug.Log($"[ChatAgentWrapper] Augmented Prompt:\n{promptToSend}");

            try
            {
                string response = "";
                if (ActiveProvider == LLMProvider.LLMUnity)
                {
#if !UNITY_WEBGL
                    if (_llmAgent == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because LLMAgent is missing.");
                        SetTyping(false);
                        OnAllActionsCompleted?.Invoke();
                        return;
                    }
                    response = await _llmAgent.Chat(promptToSend);
#else
                    Debug.LogError("[ChatAgentWrapper] LLMUnity is not supported on WebGL. Please use OpenRouter or WebGLAgentWrapper.");
                    SetTyping(false);
                    OnAllActionsCompleted?.Invoke();
                    return;
#endif
                }
                else
                {
                    if (_openRouterClient == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because OpenRouterClient is unassigned.");
                        SetTyping(false);
                        OnAllActionsCompleted?.Invoke();
                        return;
                    }
                    ConfigureOpenRouterClient();
                    response = await _openRouterClient.ChatAsync(promptToSend);
                }

                response = CleanJsonResponse(response);

                if (!string.IsNullOrEmpty(response))
                {
                    Debug.Log($"[ChatAgentWrapper] raw response: {response}");
                    AgentActions[] feedbacks = DeserializeAgentFeedbacks(response);
                    if (feedbacks != null && feedbacks.Length > 0)
                    {
                        _feedbackCoroutine = StartCoroutine(ExecuteFeedbacksSequentially(feedbacks));
                    }
                    else
                    {
                        SetTyping(false);
                        OnAllActionsCompleted?.Invoke();
                    }
                }
                else
                {
                    SetTyping(false);
                    OnAllActionsCompleted?.Invoke();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatAgentWrapper] Error during chat: {ex.Message}");
                SetTyping(false);
                OnAllActionsCompleted?.Invoke();
            }
        }

        // Debug helper: inject raw JSON as if it were the LLM response (Editor testing)
        public void InjectRawFeedbackJson(string json)
        {
            string cleaned = CleanJsonResponse(json);
            var feedbacks = DeserializeAgentFeedbacks(cleaned);
            if (feedbacks != null && feedbacks.Length > 0)
            {
                StopActiveFeedback();
                _feedbackCoroutine = StartCoroutine(ExecuteFeedbacksSequentially(feedbacks));
            }
            else
            {
                Debug.LogError("[ChatAgentWrapper] InjectRawFeedbackJson failed to parse JSON.");
            }
        }
    }
}
