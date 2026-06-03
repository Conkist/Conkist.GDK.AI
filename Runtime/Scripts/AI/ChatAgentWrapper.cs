using UnityEngine;
using UnityEngine.Events;
using LLMUnity;
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

    public class ChatAgentWrapper : MonoBehaviour
    {
        public CharacterActor characterActor;
        
        [Header("LLM Provider Settings")]
        [SerializeField] private LLMProvider _provider = LLMProvider.LLMUnity;

        [Header("OpenRouter Settings")]
        [SerializeField] private OpenRouterClient _openRouterClient;
        
        [Header("Events")]
        public UnityEvent<string> OnReceiveMessage;
        public UnityEvent<bool> OnTypingIndicatorChanged;

        [Header("RAG (Knowledge Base) Settings")]
        [SerializeField] private RAG _rag;
        [SerializeField] private bool _useRAG = true;
        [SerializeField] private bool _forceRebuildRAG = false;
        [SerializeField] private int _numRAGResults = 3;

        private LLMAgent _llmAgent;
        private string _systemPrompt;

        private void Awake()
        {
            TryGetComponent<LLMAgent>(out _llmAgent);
            if (_provider == LLMProvider.LLMUnity && _llmAgent == null)
            {
                Debug.LogError("[ChatAgentWrapper] LLMUnity provider is selected, but LLMAgent component is missing on this GameObject!");
            }
        }

        private async void Start()
        {
            InitializeSystemPrompt();

            if (_useRAG && _rag != null)
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

            string originalPrompt = "";
            if (_provider == LLMProvider.LLMUnity && _llmAgent != null)
            {
                originalPrompt = _llmAgent.systemPrompt;
            }
            else
            {
                originalPrompt = "You are a companion character in a game/experience. Respond in a JSON format matching the schema requested below.";
            }

            string characterPrompt = characterActor.characterSO.defaultPrompt;
            
            if (!string.IsNullOrEmpty(originalPrompt))
            {
                // Do not prepend if the character prompt is already part of the system prompt
                if (!originalPrompt.Contains(characterPrompt))
                {
                    _systemPrompt = characterPrompt + "\n\n" + originalPrompt;
                }
                else
                {
                    _systemPrompt = originalPrompt;
                }
            }
            else
            {
                _systemPrompt = characterPrompt;
            }

            if (_provider == LLMProvider.OpenRouter)
            {
                // Inject JSON schema instructions specifically for OpenRouter system prompt to guarantee structured output
                string jsonInstruction = "\n\nCRITICAL: You must return your response as a valid JSON array of AgentFeedback objects and nothing else. Do not wrap the JSON output in markdown formatting blocks (e.g. do NOT use ```json). The output must be pure JSON conforming to this schema:\n" +
                                         "[\n" +
                                         "  {\n" +
                                         "    \"thought\": \"your internal thoughts before speaking\",\n" +
                                         "    \"message\": \"the spoken reply back to the user\",\n" +
                                         "    \"affinity_mod\": integer (affinity modification, typically -2 to +2),\n" +
                                         "    \"excitement_mod\": integer (excitement modification, typically -2 to +2),\n" +
                                         "    \"task\": \"optional task identifier\",\n" +
                                         "    \"target\": \"optional target identifier\",\n" +
                                         "    \"duration\": float (duration of speech in seconds)\n" +
                                         "  }\n" +
                                         "]";

                _systemPrompt += jsonInstruction;
                if (_openRouterClient != null)
                {
                    _openRouterClient.Initialize(_systemPrompt);
                }
            }
            else if (_llmAgent != null)
            {
                _llmAgent.systemPrompt = _systemPrompt;
            }

            Debug.Log($"[ChatAgentWrapper] Initialized static system prompt for: {characterActor.characterSO.defaultName} using provider: {_provider}");
        }

        public void ClearHistory()
        {
            if (_provider == LLMProvider.LLMUnity && _llmAgent != null)
            {
                _ = _llmAgent.ClearHistory();
            }
            else if (_provider == LLMProvider.OpenRouter && _openRouterClient != null)
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

            if (string.IsNullOrEmpty(savePath) || knowledgeFiles == null || knowledgeFiles.Count == 0)
            {
                Debug.LogWarning("[ChatAgentWrapper] RAG save path is empty or no knowledge files are configured in CharacterSO.");
                return;
            }

            bool loaded = false;
            if (!_forceRebuildRAG)
            {
                loaded = await _rag.Load(savePath);
            }

            if (!loaded)
            {
#if UNITY_EDITOR
                Debug.Log($"[ChatAgentWrapper] RAG save file '{savePath}' not found or force rebuild enabled. Creating embeddings...");
                
                // Clear any existing items in the RAG to avoid duplication/mismatches during rebuild
                _rag.Clear();

                System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();

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

                stopwatch.Stop();
                Debug.Log($"[ChatAgentWrapper] Embedded {_rag.Count()} phrases in {stopwatch.Elapsed.TotalMilliseconds / 1000f} seconds.");
                _rag.Save(savePath);
#else
                Debug.LogError($"[ChatAgentWrapper] RAG embeddings could not be loaded from '{savePath}' and we are not in the Editor!");
#endif
            }
            else
            {
                Debug.Log($"[ChatAgentWrapper] Loaded existing RAG database '{savePath}' with {_rag.Count()} phrases.");
            }
        }

        public void ForceMessageToAgent(string text)
        {
            OnMessageSubmitted(text);
        }

        private async void OnMessageSubmitted(string text)
        {
            OnTypingIndicatorChanged?.Invoke(true);

            StringBuilder promptBuilder = new StringBuilder();
            
            // Dynamic State Injection (Aura/Mood states updated dynamically per turn)
            if (characterActor != null)
            {
                promptBuilder.AppendLine($"[State: Affinity={characterActor.currentAffinity}, Excitement={characterActor.currentExcitement}]");
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
                if (_provider == LLMProvider.LLMUnity)
                {
                    if (_llmAgent == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because LLMAgent is missing.");
                        OnTypingIndicatorChanged?.Invoke(false);
                        return;
                    }
                    response = await _llmAgent.Chat(promptToSend);
                }
                else
                {
                    if (_openRouterClient == null)
                    {
                        Debug.LogError("[ChatAgentWrapper] Cannot send message because OpenRouterClient is unassigned.");
                        OnTypingIndicatorChanged?.Invoke(false);
                        return;
                    }
                    response = await _openRouterClient.ChatAsync(promptToSend);
                }

                response = CleanJsonResponse(response);

                if (!string.IsNullOrEmpty(response))
                {
                    Debug.Log($"[ChatAgentWrapper] raw response: {response}");
                    AgentFeedback[] feedbacks = response.FromJson<AgentFeedback[]>();
                    if (feedbacks != null && feedbacks.Length > 0)
                    {
                        StartCoroutine(RunFeedbacks(feedbacks));
                    }
                    else
                    {
                        OnTypingIndicatorChanged?.Invoke(false);
                    }
                }
                else
                {
                    OnTypingIndicatorChanged?.Invoke(false);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatAgentWrapper] Error during chat: {ex.Message}");
                OnTypingIndicatorChanged?.Invoke(false);
            }
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

        private IEnumerator RunFeedbacks(AgentFeedback[] feedbacks)
        {
            for (int i = 0; i < feedbacks.Length; i++)
            {
                var feedback = feedbacks[i];
                
                OnTypingIndicatorChanged?.Invoke(false);

                if (characterActor != null)
                {
                    characterActor.UpdateMood(feedback.affinity_mod, feedback.excitement_mod);
                }

                // Fire event for local subscribers (UI / local speech)
                OnReceiveMessage?.Invoke(feedback.message);

                // Broadcast global GDK event
                string actorName = (characterActor != null && characterActor.characterSO != null) 
                    ? characterActor.characterSO.defaultName 
                    : gameObject.name;
                EventManager.TriggerEvent(new AgentMessageEvent(actorName, feedback.message));

                yield return new WaitForSeconds(feedback.duration);

                if (i < feedbacks.Length - 1)
                {
                    if (string.IsNullOrEmpty(feedbacks[i + 1].message))
                    {
                        // skip typing indicator if next message is empty
                        continue;
                    }
                    OnTypingIndicatorChanged?.Invoke(true);
                    yield return new WaitForSeconds(0.5f);
                }
            }
        }
    }
}
