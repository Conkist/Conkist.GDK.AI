using UnityEngine;
using UnityEngine.Events;
using LLMUnity;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Conkist.GDK.Serialization;
using Conkist.GDK;
using Conkist.GDK.SmartAgents.TTS;

namespace Conkist.GDK.SmartAgents
{
    public enum LLMProvider
    {
        LLMUnity,
        OpenRouter
    }

    public class ChatAgentWrapper : MonoBehaviour
    {
        public CharacterActor characterActor;

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

        [Header("Speech Settings")]
        [SerializeField] private VoiceAgentWrapper _voiceAgentWrapper;
        [SerializeField] private AvatarAnimatorWrapper _avatarAnimatorWrapper;
        [SerializeField] private bool _allowTextDrivenLipsyncFallback = true;

        [SerializeField] private OpenRouterClient _openRouterClient;
        
        [Header("Events")]
        public UnityEvent<string> OnReceiveMessage;
        public UnityEvent<bool> OnTypingIndicatorChanged;
        public UnityEvent<AgentActions> OnFeedbackReceived;

        public bool IsTyping { get; private set; }

        private void SetTyping(bool typing)
        {
            IsTyping = typing;
            OnTypingIndicatorChanged?.Invoke(typing);
        }

        [Header("RAG (Knowledge Base) Settings")]
        [SerializeField] private RAG _rag;
        [SerializeField] private bool _useRAG = true;
        [SerializeField] private bool _forceRebuildRAG = false;
        [SerializeField] private int _numRAGResults = 3;

        private const string DefaultSystemPromptPrefix =
            "You are a companion character in a game/experience. Respond in a JSON format matching the schema requested below. " +
            "If you choose an animate task, use only exact animation keys from the character's animation list. " +
            "Do not paraphrase, shorten, or invent animation names. If no exact match exists, use task = \"none\".";

        private LLMAgent _llmAgent;
        private string _systemPrompt;
        private Coroutine _feedbackCoroutine;

        private void Awake()
        {
            TryGetComponent<LLMAgent>(out _llmAgent);
            if (ActiveProvider == LLMProvider.LLMUnity && _llmAgent == null)
            {
                Debug.LogError("[ChatAgentWrapper] LLMUnity provider is selected, but LLMAgent component is missing on this GameObject!");
            }

            ResolveVoiceAndAnimatorReferences();
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
            SafelyCancelLLMRequests();
        }

        /// <summary>
        /// Safely cancels any in-flight LLMUnity requests.
        /// Wrapped in try/catch because the LLMUnity native service may already
        /// be shut down during Play Mode exit, causing a Mono SIGSEGV if called
        /// on a dangling native handle.
        /// </summary>
        private void SafelyCancelLLMRequests()
        {
            if (_llmAgent == null) return;
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
        }

        private async void Start()
        {
            InitializeSystemPrompt();

            if (_useRAG && _rag != null)
            {
                await InitializeRAG();
            }

            if (_voiceAgentWrapper != null)
            {
                _voiceAgentWrapper.OnVoiceGenerationFailed.AddListener(HandleVoiceGenerationFailed);
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
            else if (_llmAgent != null)
            {
                _llmAgent.systemPrompt = _systemPrompt;
            }

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

        private void ResolveVoiceAndAnimatorReferences()
        {
            if (_voiceAgentWrapper == null)
            {
                TryGetComponent<VoiceAgentWrapper>(out _voiceAgentWrapper);
                if (_voiceAgentWrapper == null)
                {
                    _voiceAgentWrapper = GetComponentInChildren<VoiceAgentWrapper>();
                }
                if (_voiceAgentWrapper == null)
                {
                    _voiceAgentWrapper = GetComponentInParent<VoiceAgentWrapper>();
                }
            }

            if (_avatarAnimatorWrapper == null)
            {
                TryGetComponent<AvatarAnimatorWrapper>(out _avatarAnimatorWrapper);
                if (_avatarAnimatorWrapper == null)
                {
                    _avatarAnimatorWrapper = GetComponentInChildren<AvatarAnimatorWrapper>();
                }
                if (_avatarAnimatorWrapper == null)
                {
                    _avatarAnimatorWrapper = GetComponentInParent<AvatarAnimatorWrapper>();
                }
            }
        }

        private void OnEnable()
        {

        }

        private void OnDisable()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
            }
            SafelyCancelLLMRequests();
        }

        private void OnDestroy()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
            }

            SafelyCancelLLMRequests();

            if (_voiceAgentWrapper != null)
            {
                _voiceAgentWrapper.OnVoiceGenerationFailed.RemoveListener(HandleVoiceGenerationFailed);
            }
        }

        private void HandleVoiceGenerationFailed(string originalText)
        {
            if (_allowTextDrivenLipsyncFallback && _avatarAnimatorWrapper != null)
            {
                _avatarAnimatorWrapper.PlayTextLipSync(originalText);
            }
        }

        public void ClearHistory()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
                SetTyping(false);
            }

            if (ActiveProvider == LLMProvider.LLMUnity && _llmAgent != null)
            {
                _ = _llmAgent.ClearHistory();
            }
            else if (ActiveProvider == LLMProvider.OpenRouter && _openRouterClient != null)
            {
                _openRouterClient.ClearHistory();
            }
        }

        private async System.Threading.Tasks.Task InitializeRAG()
        {
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
        }

        public void ForceMessageToAgent(string text)
        {
            Debug.Log("Forcing message!");
            OnMessageSubmitted(text);
        }

        private async void OnMessageSubmitted(string text)
        {
            await System.Threading.Tasks.Task.Yield();

            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
            }

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

            promptBuilder.AppendLine("User Message:");
            promptBuilder.Append(text);

            string promptToSend = promptBuilder.ToString();
            Debug.Log($"[ChatAgentWrapper] Augmented Prompt:\n{promptToSend}");

            try
            {
                string response = "";
                if (ActiveProvider == LLMProvider.LLMUnity)
                {
                    if (_llmAgent == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because LLMAgent is missing.");
                        SetTyping(false);
                        return;
                    }
                    response = await _llmAgent.Chat(promptToSend);
                }
                else
                {
                    if (_openRouterClient == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because OpenRouterClient is unassigned.");
                        SetTyping(false);
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
                    }
                }
                else
                {
                    SetTyping(false);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatAgentWrapper] Error during chat: {ex.Message}");
                SetTyping(false);
            }
        }

        private bool TrySpeakOrAnimateMessage(string message, bool voiceAttempted = false, bool voiceSucceeded = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // Attempt to play audio if voice provider is available and valid
            bool voiceStarted = false;
            if (voiceAttempted)
            {
                voiceStarted = voiceSucceeded;
            }
            else if (_voiceAgentWrapper != null && _voiceAgentWrapper.HasValidProvider)
            {
                string voiceId = (characterActor != null && characterActor.characterSO != null) 
                    ? characterActor.characterSO.elevenLabsVoiceID 
                    : null;
                voiceStarted = _voiceAgentWrapper.TryGenerateVoice(message, voiceId);
            }

            if (voiceStarted)
            {
                if (_avatarAnimatorWrapper != null)
                {
                    _avatarAnimatorWrapper.OnAudioSpeechStarted();
                }
            }
            else
            {
                // Fallback to text lipsync immediately if no voice provider is configured/available
                if (_allowTextDrivenLipsyncFallback && _avatarAnimatorWrapper != null)
                {
                    _avatarAnimatorWrapper.PlayTextLipSync(message);
                }
            }

            return voiceStarted;
        }



        private string CleanJsonResponse(string response)
        {
            if (string.IsNullOrEmpty(response)) return "";
            string cleaned = response.Trim();
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            else if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            return cleaned.Trim();
        }

        /// <summary>
        /// Deserializes the LLM response into an AgentActions array.
        /// Supports either a raw JSON array [{...}] or a wrapper object { thought: ..., actions: [...] }.
        /// </summary>
        private AgentActions[] DeserializeAgentFeedbacks(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            // First attempt: raw array of action objects.
            try
            {
                var arr = json.FromJson<AgentActions[]>();
                if (arr != null && arr.Length > 0) return arr;
            }
            catch { /* fall through to wrapper parsing */ }

            // Fallback: wrapper object with thought + actions.
            try
            {
                var wrapper = json.FromJson<AgentFeedback>();
                if (wrapper != null && wrapper.actions != null && wrapper.actions.Length > 0)
                {
                    return wrapper.actions;
                }
            }
            catch { /* fall through to plain-object fallback */ }

            // Fallback: single action object {}.
            try
            {
                var single = json.FromJson<AgentActions>();
                if (single != null)
                {
                    Debug.LogWarning("[ChatAgentWrapper] LLM returned a single action object instead of an array. Wrapping automatically.");
                    return new[] { single };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatAgentWrapper] Failed to deserialize agent response: {ex.Message}\nRaw JSON: {json}");
            }

            return null;
        }

        // Debug helper: inject raw JSON as if it were the LLM response (Editor testing)
        public void InjectRawFeedbackJson(string json)
        {
            string cleaned = CleanJsonResponse(json);
            var feedbacks = DeserializeAgentFeedbacks(cleaned);
            if (feedbacks != null && feedbacks.Length > 0)
            {
                if (_feedbackCoroutine != null)
                {
                    StopCoroutine(_feedbackCoroutine);
                }
                _feedbackCoroutine = StartCoroutine(ExecuteFeedbacksSequentially(feedbacks));
            }
            else
            {
                Debug.LogError("[ChatAgentWrapper] InjectRawFeedbackJson failed to parse JSON.");
            }
        }

        private void ProcessAndInvokeActionFeedback(AgentActions feedback, bool voiceAttempted = false, bool voiceSucceeded = false)
        {
            if (feedback == null) return;

            // Update mood state
            if (characterActor != null)
            {
                float pleasureDelta = feedback.pleasure_mod != 0 ? feedback.pleasure_mod : feedback.affinity_mod * 0.05f;
                float arousalDelta = feedback.arousal_mod != 0 ? feedback.arousal_mod : feedback.excitement_mod * 0.1f;
                float dominanceDelta = feedback.dominance_mod;

                characterActor.UpdateMood(pleasureDelta, arousalDelta, dominanceDelta);

                // Populate resulting states into the feedback so listeners can read final values
                feedback.resultingPleasure = characterActor.currentPleasure;
                feedback.resultingArousal = characterActor.currentArousal;
                feedback.resultingDominance = characterActor.currentDominance;
            }

            // Backwards-compatibility: parse legacy task format like "animate:Walk" into animate command
            if ((feedback.animate == null || string.IsNullOrEmpty(feedback.animate.name)) && !string.IsNullOrEmpty(feedback.task))
            {
                if (feedback.task.StartsWith("animate:"))
                {
                    string animName = feedback.task.Substring("animate:".Length).Trim();
                    if (!string.IsNullOrEmpty(animName))
                    {
                        feedback.animate = new AgentActions.AnimateCommand { name = animName };
                    }
                }
            }

            // Invoke event immediately — allows all subscribers (animate, movement, pickup, etc.) to react independently
            OnFeedbackReceived?.Invoke(feedback);
            
            // Also invoke message event for UI/speech ONLY if not empty/whitespace
            if (!string.IsNullOrWhiteSpace(feedback.message))
            {
                OnReceiveMessage?.Invoke(feedback.message);

                // Broadcast global GDK event
                string actorName = (characterActor != null && characterActor.characterSO != null) 
                    ? characterActor.characterSO.defaultName 
                    : gameObject.name;
                EventManager.TriggerEvent(new AgentMessageEvent(actorName, feedback.message));

                // Play speech or fallback lipsync for the message
                TrySpeakOrAnimateMessage(feedback.message, voiceAttempted, voiceSucceeded);
            }
        }

        private IEnumerator ExecuteFeedbacksSequentially(AgentActions[] feedbacks)
        {
            for (int i = 0; i < feedbacks.Length; i++)
            {
                var feedback = feedbacks[i];
                bool voiceAttempted = false;
                bool voiceSucceeded = false;

                // If voice generation is enabled and we have a message, wait for generation to complete
                if (!string.IsNullOrWhiteSpace(feedback.message) && _voiceAgentWrapper != null && _voiceAgentWrapper.HasValidProvider)
                {
                    SetTyping(true);

                    bool voiceReady = false;
                    bool voiceFailed = false;

                    string voiceId = (characterActor != null && characterActor.characterSO != null) 
                        ? characterActor.characterSO.elevenLabsVoiceID 
                        : null;

                    _voiceAgentWrapper.TryGenerateVoice(
                        feedback.message,
                        voiceId,
                        onCompleted: (clip) =>
                        {
                            voiceReady = true;
                            voiceSucceeded = true;
                        },
                        onFailed: (err) =>
                        {
                            voiceFailed = true;
                            voiceSucceeded = false;
                        }
                    );

                    yield return new WaitUntil(() => voiceReady || voiceFailed);
                    voiceAttempted = true;
                }

                // Hide typing indicator before presenting the message if the message is not empty.
                if (!string.IsNullOrWhiteSpace(feedback.message))
                {
                    SetTyping(false);
                }
                else
                {
                    // If the current action has no message, check if there are any subsequent non-empty messages.
                    // If there are, show the typing indicator. If not, hide it.
                    bool hasNextNonEmptyMessage = false;
                    for (int j = i + 1; j < feedbacks.Length; j++)
                    {
                        if (feedbacks[j] != null && !string.IsNullOrWhiteSpace(feedbacks[j].message))
                        {
                            hasNextNonEmptyMessage = true;
                            break;
                        }
                    }
                    if (hasNextNonEmptyMessage)
                    {
                        SetTyping(true);
                    }
                    else
                    {
                        SetTyping(false);
                    }
                }

                // Process the feedback (mood, animation, event invoke, speech/lipsync if not empty)
                ProcessAndInvokeActionFeedback(feedback, voiceAttempted, voiceSucceeded);

                // Wait for the duration of this action
                yield return new WaitForSeconds(feedback.duration);

                // If there's another feedback coming
                if (i < feedbacks.Length - 1)
                {
                    // If the next feedback has a non-empty message, show the typing indicator for a brief delay to simulate typing
                    if (feedbacks[i + 1] != null && !string.IsNullOrWhiteSpace(feedbacks[i + 1].message))
                    {
                        SetTyping(true);
                        yield return new WaitForSeconds(0.5f);
                    }
                }
            }

            SetTyping(false);
            _feedbackCoroutine = null;
        }
    }
}
