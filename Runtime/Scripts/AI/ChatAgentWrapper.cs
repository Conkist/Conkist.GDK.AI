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

namespace Conkist.GDK.SmartAgents
{
    public enum LLMProvider
    {
        LLMUnity,
        OpenRouter
    }

    public class ChatAgentWrapper : AgentWrapper
    {
        [Header("Custom Overrides")]
        [SerializeField] private bool _useCustomProvider = false;
        [SerializeField] private LLMProvider _customProvider = LLMProvider.LLMUnity;
        [SerializeField] private bool _useCustomModel = false;
        [SerializeField] private string _customModel;

        public LLMProvider ActiveProvider
        {
            get
            {
                if (_useCustomProvider)
                {
                    return _customProvider;
                }
                var settings = GetApplicationSettings();
                if (settings != null)
                {
                    return settings.defaultProvider;
                }
                return LLMProvider.LLMUnity;
            }
            set
            {
                if (_useCustomProvider)
                {
                    _customProvider = value;
                }
                else
                {
                    var settings = GetApplicationSettings();
                    if (settings != null)
                    {
                        settings.defaultProvider = value;
                    }
                }
                UpdateActiveProviderCache();
            }
        }

        private ApplicationSettingsSO GetApplicationSettings()
        {
            if (ApplicationManager.HasInstance)
            {
                return ApplicationManager.Instance.Settings;
            }
            return null;
        }

        [Header("Prompt Settings")]
        [TextArea(5, 10)]
        [SerializeField] private string _systemPromptPrefix;

        [SerializeField] private OpenRouterClient _openRouterClient;

        [Header("RAG (Knowledge Base) Settings")]
#if !UNITY_WEBGL
        [SerializeField] private RAG _rag;
#endif
        [SerializeField] private bool _useRAG = true;
        [SerializeField] private bool _forceRebuildRAG = false;
        [SerializeField] private int _numRAGResults = 3;

        private const string DefaultSystemPromptPrefix =
            "You are a companion character in a game/experience. Respond in a JSON format matching the schema requested below. " +
            "If you choose an animate task, use only exact animation keys from the character's animation list. " +
            "Do not paraphrase, shorten, or invent animation names. If no exact match exists, use task = \"none\".";

#if !UNITY_WEBGL
        private LLMAgent _llmAgent;
#endif
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
            if (_activeProviderRuntime == LLMProvider.LLMUnity && _llmAgent == null)
            {
                Debug.LogError("[ChatAgentWrapper] LLMUnity provider is selected, but LLMAgent component is missing on this GameObject!");
            }
#endif
        }

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

            int count = characterSO.animations.Length;
            return "[Animation Rules]\nThis character has " + count + " available animation keys. " +
                "If you choose an animate task, use one of the exact keys from this character's current animation list. " +
                "Do not invent, paraphrase, shorten, or normalize animation names. " +
                "If no exact key applies, set task = \"none\".";
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

            if (_useRAG)
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

            // Resolve API key
            string apiKey = "";
            if (settings != null)
            {
                apiKey = settings.defaultOpenRouterApiKey;
            }
            else
            {
                // Fallback to whatever is serialized directly in OpenRouterClient
                apiKey = _openRouterClient.ApiKey;
            }
            _openRouterClient.ApiKey = apiKey;

            // Resolve Model (which can be a model ID or preset slug)
            string modelOrPreset = "";
            if (_useCustomModel && !string.IsNullOrEmpty(_customModel))
            {
                modelOrPreset = _customModel;
            }
            else if (settings != null)
            {
                modelOrPreset = settings.defaultOpenRouterModel;
            }
            else
            {
                // Fallback to whatever is serialized directly in OpenRouterClient
                modelOrPreset = _openRouterClient.Model;
            }
            _openRouterClient.Model = modelOrPreset;
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

        private async System.Threading.Tasks.Task InitializeRAG()
        {
#if !UNITY_WEBGL
            if (characterActor == null || characterActor.characterSO == null)
            {
                Debug.LogWarning("[ChatAgentWrapper] Cannot initialize RAG: CharacterActor or CharacterSO is unassigned.");
                return;
            }

            var characterSO = characterActor.characterSO;
            string savePath = characterSO.ragSavePath;
            List<TextAsset> knowledgeFiles = characterSO.knowledgeFiles;
            bool hasKnowledgeFiles = knowledgeFiles != null && knowledgeFiles.Count > 0;
            bool hasAnimations = characterSO.animations != null && characterSO.animations.Length > 0;

            if (string.IsNullOrEmpty(savePath) || (!hasKnowledgeFiles && !hasAnimations))
            {
                Debug.LogWarning("[ChatAgentWrapper] RAG save path is empty or no knowledge files/animation list are configured in CharacterSO.");
                return;
            }

            if (_rag == null)
            {
                return;
            }

            bool loaded = false;
            if (!_forceRebuildRAG)
            {
                loaded = await _rag.Load(savePath);
            }

            if (!loaded)
            {
                Debug.Log($"[ChatAgentWrapper] RAG save file '{savePath}' not found or force rebuild enabled. Creating embeddings...");
                
                _rag.Clear();

                System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();

                if (hasKnowledgeFiles)
                {
                    foreach (var file in knowledgeFiles)
                    {
                        if (file == null) continue;
                        
                        string[] paragraphs = file.text.Split(new string[] { "\n\n", "\r\n\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
                        foreach (string paragraph in paragraphs)
                        {
                            string cleaned = paragraph.Trim();
                            if (!string.IsNullOrEmpty(cleaned))
                            {
                                await _rag.Add(cleaned);
                            }
                        }
                    }
                }

                // Also embed the character's animation list (from CharacterSO animations) as a separate document
                try
                {
                    if (hasAnimations)
                    {
                        StringBuilder animBuilder = new StringBuilder();
                        animBuilder.AppendLine($"Animation list for character: {characterSO.defaultName}");
                        animBuilder.AppendLine("Use these animation keys to trigger runtime animations using the 'animate' action or task.");
                        animBuilder.AppendLine();
                        foreach (var expr in characterSO.animations)
                        {
                            if (expr == null) continue;
                            string line = $"- \"{expr.key}\"";
                            if (!string.IsNullOrEmpty(expr.description))
                            {
                                line += $" - {expr.description}";
                            }
                            animBuilder.AppendLine(line);
                        }

                        string animDoc = animBuilder.ToString();
                        if (!string.IsNullOrEmpty(animDoc))
                        {
                            await _rag.Add(animDoc);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ChatAgentWrapper] Failed to add animation list to RAG: {ex.Message}");
                }

                stopwatch.Stop();
                Debug.Log($"[ChatAgentWrapper] Embedded {_rag.Count()} phrases in {stopwatch.Elapsed.TotalMilliseconds / 1000f} seconds.");
                _rag.Save(savePath);
            }
            else
            {
                Debug.Log($"[ChatAgentWrapper] Loaded existing RAG database '{savePath}' with {_rag.Count()} phrases.");
            }
#else
            await System.Threading.Tasks.Task.Yield();
            Debug.LogWarning("[ChatAgentWrapper] RAG is not supported on WebGL builds.");
#endif
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
#if !UNITY_WEBGL
            if (_useRAG && _rag != null && _rag.Count() > 0)
            {
                try
                {
                    (string[] similarPhrases, _) = await _rag.Search(text, _numRAGResults);
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
#endif

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
