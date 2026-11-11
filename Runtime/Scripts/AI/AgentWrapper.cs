using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using Conkist.GDK.Serialization;
using Conkist.GDK.AI.TTS;

namespace Conkist.GDK.AI
{
    public abstract class AgentWrapper : MonoBehaviour
    {
        public CharacterActor characterActor;

        [Header("Speech Settings")]
        [SerializeField] protected VoiceAgentWrapper _voiceAgentWrapper;
        [SerializeField] protected AvatarAnimatorWrapper _avatarAnimatorWrapper;
        [SerializeField] protected bool _allowTextDrivenLipsyncFallback = true;

        [Header("Events")]
        public UnityEvent<string> OnReceiveMessage;
        public UnityEvent<bool> OnTypingIndicatorChanged;
        public UnityEvent<AgentActions> OnFeedbackReceived;
        public UnityEvent OnAllActionsCompleted;

        public bool IsTyping { get; protected set; }

        protected Coroutine _feedbackCoroutine;

        protected virtual void Awake()
        {
            ResolveVoiceAndAnimatorReferences();
        }

        protected virtual void Start()
        {
            if (_voiceAgentWrapper != null)
            {
                _voiceAgentWrapper.OnVoiceGenerationFailed.AddListener(HandleVoiceGenerationFailed);
            }

            // uLipSync follows audio only when lip sync is on in the settings AND a TTS provider (ElevenLabs) is active.
            // Otherwise it is switched off and the text-simulated lip sync drives the mouth.
            if (_avatarAnimatorWrapper != null)
            {
                _avatarAnimatorWrapper.ConfigureLipSyncMode(_voiceAgentWrapper != null && _voiceAgentWrapper.HasValidProvider);
            }
        }

        protected virtual void OnDisable()
        {
            StopActiveFeedback();
        }

        protected virtual void OnDestroy()
        {
            StopActiveFeedback();
            if (_voiceAgentWrapper != null)
            {
                _voiceAgentWrapper.OnVoiceGenerationFailed.RemoveListener(HandleVoiceGenerationFailed);
            }
        }

        public virtual void ClearHistory()
        {
            StopActiveFeedback();
        }

        public void ToggleMute(int mute)
        {
            bool isMuted = mute != 0;
            AudioListener.volume = isMuted ? 0f : 1f;
            Debug.Log($"[AgentWrapper] ToggleMute -> isMuted: {isMuted}");
        }

        public abstract void ForceMessageToAgent(string text);

        protected void SetTyping(bool typing)
        {
            IsTyping = typing;
            OnTypingIndicatorChanged?.Invoke(typing);
        }

        protected void StopActiveFeedback()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
                SetTyping(false);
                OnAllActionsCompleted?.Invoke();
            }
        }

        protected void ResolveVoiceAndAnimatorReferences()
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

        protected void HandleVoiceGenerationFailed(string originalText)
        {
            if (_allowTextDrivenLipsyncFallback && _avatarAnimatorWrapper != null)
            {
                _avatarAnimatorWrapper.PlayTextLipSync(originalText);
            }
        }

        protected string CleanJsonResponse(string response)
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

        // Closes strings, arrays and objects left open when the LLM stops early, so a cut answer still plays.
        private static string CloseTruncatedJson(string json)
        {
            var stack = new Stack<char>();
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (inString) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') inString = false; continue; }
                if (c == '"') inString = true;
                else if (c == '{') stack.Push('}');
                else if (c == '[') stack.Push(']');
                else if ((c == '}' || c == ']') && stack.Count > 0) stack.Pop();
            }
            if (stack.Count == 0 && !inString) return json;
            var sb = new System.Text.StringBuilder(json);
            if (inString) sb.Append('"');
            string t = sb.ToString().TrimEnd();
            if (t.EndsWith(",")) t = t.Substring(0, t.Length - 1);
            sb = new System.Text.StringBuilder(t);
            if (t.EndsWith(":")) sb.Append("null");
            while (stack.Count > 0) sb.Append(stack.Pop());
            Debug.LogWarning("[AgentWrapper] LLM answer was cut off; closed the JSON automatically.");
            return sb.ToString();
        }

        protected AgentActions[] DeserializeAgentFeedbacks(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            string trimmed = json.Trim();
            if (trimmed.StartsWith("{")) trimmed = json = CloseTruncatedJson(trimmed);
            if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            {
                var action = new AgentActions
                {
                    message = json,
                    duration = Mathf.Max(3f, json.Length * 0.08f)
                };
                return new[] { action };
            }

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
                    Debug.LogWarning("[AgentWrapper] LLM returned a single action object instead of an array. Wrapping automatically.");
                    return new[] { single };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AgentWrapper] Failed to deserialize agent response: {ex.Message}\nRaw JSON: {json}");
            }

            return null;
        }

        protected void ProcessAndInvokeActionFeedback(AgentActions feedback, bool voiceAttempted = false, bool voiceSucceeded = false)
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

        protected bool TrySpeakOrAnimateMessage(string message, bool voiceAttempted = false, bool voiceSucceeded = false)
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

            if (voiceStarted && _avatarAnimatorWrapper != null && _avatarAnimatorWrapper.AudioLipSyncEnabled)
            {
                // Voice plays and lip sync is on: uLipSync follows the audio.
                _avatarAnimatorWrapper.OnAudioSpeechStarted();
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

        protected IEnumerator ExecuteFeedbacksSequentially(AgentActions[] feedbacks)
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
            OnAllActionsCompleted?.Invoke();
        }
    }
}
