using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Conkist.AI.Animation;

namespace Conkist.GDK.SmartAgents
{
    [RequireComponent(typeof(Animator))]
    public class AvatarAnimatorWrapper : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterActor _characterActor;
        [SerializeField] private EyeBlinkHandler _eyeBlinkHandler;
        [SerializeField] private AgentWrapper _chatAgentWrapper;

        [Header("uLipSync Settings")]
        [SerializeField] private uLipSync.uLipSync _uLipSync;
        [SerializeField] private uLipSync.uLipSyncBlendShape _uLipSyncBlendShape;

        [Header("Animation Rigging (LookAt)")]
        [SerializeField] private Rig _lookRig;
        [SerializeField] private MultiAimConstraint[] _aimConstraints;
        [SerializeField] private Transform _lookAtTarget;
        [SerializeField] private float _lookFollowSpeed = 5f;
        [SerializeField] private float _defaultLookDistance = 3f;
        [SerializeField] private float _defaultLookHeightOffset = 1.5f;

        [Header("Animation Smoothing")]
        [SerializeField] private float _lerpSpeed = 5f;

        [Header("Text Lip Sync Fallback")]
        [SerializeField] private bool _enableTextLipSyncFallback = true;
        [SerializeField] private float _textLipSyncDurationPerCharacter = 0.04f;
        [SerializeField] private float _textLipSyncMinDuration = 0.75f;
        [SerializeField] private float _textLipSyncMaxDuration = 4f;

        private Dictionary<string, AnimationClip> _animationDict = new Dictionary<string, AnimationClip>();
        private PlayableGraph _playableGraph;
        private Coroutine _animationCoroutine;
        private Coroutine _textLipSyncCoroutine;

        [Header("Debug Settings")]
        public bool showDebugGUI = true;
        public bool debugOverride = false;
        [Range(-1f, 1f)] public float debugPleasure = 0.5f;
        [Range(0f, 1f)] public float debugArousal = 0f;
        [Range(0f, 1f)] public float debugDominance = 0.5f;

        private Animator _animator;
        private Vector3 _defaultLocalLookPos;

        // Current runtime values sent to animator
        private float _currentPleasure = 0f;
        private float _currentArousal = 0f;
        private float _currentDominance = 0.5f;
        private float _currentLookWeight = 0f;

        // Target values to interpolate towards
        private float _targetPleasure = 0f;
        private float _targetArousal = 0f;
        private float _targetDominance = 0.5f;
        private float _targetLookWeight = 0f;

        // State change tracking to trigger EmotionChange
        private float _lastPleasure;
        private float _lastArousal;
        private float _lastDominance;

        private void Awake()
        {
            // Resolve Animator
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null) _animator = GetComponentInParent<Animator>();

            // Resolve CharacterActor
            if (_characterActor == null) _characterActor = GetComponent<CharacterActor>();
            if (_characterActor == null) _characterActor = GetComponentInChildren<CharacterActor>();
            if (_characterActor == null) _characterActor = GetComponentInParent<CharacterActor>();

            // Resolve EyeBlinkHandler
            if (_eyeBlinkHandler == null) _eyeBlinkHandler = GetComponent<EyeBlinkHandler>();
            if (_eyeBlinkHandler == null) _eyeBlinkHandler = GetComponentInChildren<EyeBlinkHandler>();
            if (_eyeBlinkHandler == null) _eyeBlinkHandler = GetComponentInParent<EyeBlinkHandler>();

            // Resolve AgentWrapper
            if (_chatAgentWrapper == null) _chatAgentWrapper = GetComponent<AgentWrapper>();
            if (_chatAgentWrapper == null) _chatAgentWrapper = GetComponentInChildren<AgentWrapper>();
            if (_chatAgentWrapper == null) _chatAgentWrapper = GetComponentInParent<AgentWrapper>();

            // Verification logging
            if (_animator == null)
            {
                Debug.LogError("[AvatarAnimatorWrapper] Animator component is missing on this GameObject and all parents/children!");
            }
            if (_characterActor == null)
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] CharacterActor component is missing on this GameObject and all parents/children.");
            }
            if (_eyeBlinkHandler == null)
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] EyeBlinkHandler component is missing on this GameObject and all parents/children.");
            }
            if (_chatAgentWrapper == null)
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] AgentWrapper component is missing on this GameObject and all parents/children.");
            }

            ResolveULipSyncReferences();
        }

        private void Start()
        {
            // Define the default local look target position (forward relative to character)
            _defaultLocalLookPos = new Vector3(0, _defaultLookHeightOffset, _defaultLookDistance);

            if (_lookAtTarget != null)
            {
                _lookAtTarget.position = transform.TransformPoint(_defaultLocalLookPos);
            }

            // Initialize emotion values to the actor's current state or the defaults.
            if (_characterActor != null)
            {
                _currentPleasure = _targetPleasure = _characterActor.currentPleasure;
                _currentArousal = _targetArousal = _characterActor.currentArousal;
                _currentDominance = _targetDominance = _characterActor.currentDominance;
            }
            else
            {
                _currentPleasure = _targetPleasure = 0f;
                _currentArousal = _targetArousal = 0f;
                _currentDominance = _targetDominance = 0.5f;
            }

            if (_animator != null)
            {
                _animator.SetFloat("Pleasure", _currentPleasure);
                _animator.SetFloat("Arousal", _currentArousal);
                _animator.SetFloat("Dominance", _currentDominance);
            }

            // Populate animation dictionary from CharacterSO
            if (_characterActor != null && _characterActor.characterSO != null && _characterActor.characterSO.animations != null && _characterActor.characterSO.animations.Length > 0)
            {
                foreach (var expr in _characterActor.characterSO.animations)
                {
                    if (!string.IsNullOrEmpty(expr.key) && expr.clip != null)
                    {
                        _animationDict[expr.key] = expr.clip;
                    }
                }
            }
            else
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] No animations found in CharacterSO. Animation playback will not work.");
            }

            // Subscribe to AgentWrapper events
            if (_chatAgentWrapper != null)
            {
                _chatAgentWrapper.OnFeedbackReceived.AddListener(HandleFeedbackReceived);
            }
        }

        private void OnDisable()
        {
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (_chatAgentWrapper != null)
            {
                _chatAgentWrapper.OnFeedbackReceived.RemoveListener(HandleFeedbackReceived);
            }
        }

        private void Update()
        {
            UpdateAnimationParameters();
            UpdateLookRig();
        }

        private void UpdateAnimationParameters()
        {
            // Skip updating animator controller parameters if a custom playable graph is active
            // to prevent the controller from overriding/resetting the playable clip.
            if (_playableGraph.IsValid() && _playableGraph.IsPlaying())
            {
                return;
            }

            if (debugOverride)
            {
                _targetPleasure = debugPleasure;
                _targetArousal = debugArousal;
                _targetDominance = debugDominance;
            }
            else if (_characterActor != null)
            {
                _targetPleasure = _characterActor.currentPleasure;
                _targetArousal = _characterActor.currentArousal;
                _targetDominance = _characterActor.currentDominance;

                // Detect discrete mood changes to fire trigger
                if (_characterActor.currentPleasure != _lastPleasure || _characterActor.currentArousal != _lastArousal || _characterActor.currentDominance != _lastDominance)
                {
                    _animator.SetTrigger("EmotionChange");
                    _lastPleasure = _characterActor.currentPleasure;
                    _lastArousal = _characterActor.currentArousal;
                    _lastDominance = _characterActor.currentDominance;
                }
            }

            // Smoothly interpolate parameters
            _currentPleasure = Mathf.MoveTowards(_currentPleasure, _targetPleasure, Time.deltaTime * _lerpSpeed);
            _currentArousal = Mathf.MoveTowards(_currentArousal, _targetArousal, Time.deltaTime * _lerpSpeed);
            _currentDominance = Mathf.MoveTowards(_currentDominance, _targetDominance, Time.deltaTime * _lerpSpeed);

            // Apply to Animator
            _animator.SetFloat("Pleasure", _currentPleasure);
            _animator.SetFloat("Arousal", _currentArousal);
            _animator.SetFloat("Dominance", _currentDominance);
        }

        private void UpdateLookRig()
        {
            Vector3 lookPosition = transform.TransformPoint(_defaultLocalLookPos);

            // Get target game object from character actor
            GameObject currentTarget = null;
            if (_characterActor != null)
            {
                currentTarget = _characterActor.CurrentTarget;
            }

            if (currentTarget != null)
            {
                lookPosition = currentTarget.transform.position;
                _targetLookWeight = 1.0f;
            }
            else
            {
                _targetLookWeight = 0.0f;
            }

            // Smoothly move the look target Transform
            if (_lookAtTarget != null)
            {
                _lookAtTarget.position = Vector3.Lerp(_lookAtTarget.position, lookPosition, Time.deltaTime * _lookFollowSpeed);
            }

            // Smoothly interpolate Rig weights
            _currentLookWeight = Mathf.MoveTowards(_currentLookWeight, _targetLookWeight, Time.deltaTime * _lerpSpeed);

            if (_lookRig != null)
            {
                _lookRig.weight = _currentLookWeight;
            }

            if (_aimConstraints != null)
            {
                foreach (var constraint in _aimConstraints)
                {
                    if (constraint != null)
                    {
                        constraint.weight = _currentLookWeight;
                    }
                }
            }
        }



        public void PlayAnimation(string animationName)
        {
            if (string.IsNullOrEmpty(animationName) || _animator == null) return;

            // Search list directly to support dynamic runtime additions/edits in the inspector
            AnimationClip clip = null;
            if (_animationDict != null && _animationDict.TryGetValue(animationName, out clip))
            {
                // found in dictionary
            }

            if (clip != null)
            {
                Debug.Log($"[AvatarAnimatorWrapper] Playing AnimationClip '{clip.name}' mapped to key '{animationName}'.");
                PlayAnimationClip(clip);
            }
            else
            {
                Debug.LogWarning($"[AvatarAnimatorWrapper] Animation mapping not found for key '{animationName}'.");
            }
        }

        public void PlayTextLipSync(string message)
        {
            if (!_enableTextLipSyncFallback || string.IsNullOrWhiteSpace(message) || _animator == null)
            {
                return;
            }

            if (_textLipSyncCoroutine != null)
            {
                StopCoroutine(_textLipSyncCoroutine);
                _textLipSyncCoroutine = null;
            }

            ResolveULipSyncReferences();

            // Animate simple lipsync using animator parameters based on message duration
            if (!TryAnimateSimpleLipSync(message))
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] Text lipsync fallback was requested but no lip sync parameter was found on animator.");
            }

            float duration = Mathf.Clamp(message.Length * _textLipSyncDurationPerCharacter, _textLipSyncMinDuration, _textLipSyncMaxDuration);
            if (_uLipSyncBlendShape != null)
            {
                _textLipSyncCoroutine = StartCoroutine(CoTextDecodedLipSync(message, duration));
            }
            else
            {
                Debug.LogWarning("[AvatarAnimatorWrapper] uLipSyncBlendShape component is missing. Can only use animator parameter fallback.");
            }
        }

        public void OnAudioSpeechStarted()
        {
            if (_textLipSyncCoroutine != null)
            {
                StopCoroutine(_textLipSyncCoroutine);
                _textLipSyncCoroutine = null;
            }

            ResolveULipSyncReferences();
            if (_uLipSync != null)
            {
                _uLipSync.enabled = true;
            }
        }

        private void ResolveULipSyncReferences()
        {
            if (_uLipSync == null)
            {
                _uLipSync = GetComponent<uLipSync.uLipSync>();
                if (_uLipSync == null) _uLipSync = GetComponentInChildren<uLipSync.uLipSync>();
                if (_uLipSync == null) _uLipSync = GetComponentInParent<uLipSync.uLipSync>();
            }

            if (_uLipSyncBlendShape == null)
            {
                _uLipSyncBlendShape = GetComponent<uLipSync.uLipSyncBlendShape>();
                if (_uLipSyncBlendShape == null) _uLipSyncBlendShape = GetComponentInChildren<uLipSync.uLipSyncBlendShape>();
                if (_uLipSyncBlendShape == null) _uLipSyncBlendShape = GetComponentInParent<uLipSync.uLipSyncBlendShape>();
            }
        }

        private struct SyllableInfo
        {
            public string text;
            public string phoneme;
            public float durationWeight;
        }

        private List<SyllableInfo> ParseSyllables(string text)
        {
            var list = new List<SyllableInfo>();
            if (string.IsNullOrEmpty(text)) return list;

            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                char c = text[i];
                
                // Handle spaces and punctuation as silence
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    int start = i;
                    while (i < n && (char.IsWhiteSpace(text[i]) || char.IsPunctuation(text[i]) || char.IsSymbol(text[i])))
                    {
                        i++;
                    }
                    string chunk = text.Substring(start, i - start);
                    list.Add(new SyllableInfo { text = chunk, phoneme = "-", durationWeight = chunk.Length * 0.75f });
                    continue;
                }

                // Process a syllable (consonants followed by vowels)
                int syllableStart = i;
                
                // 1. Consume leading consonants (if any)
                while (i < n && !IsVowel(text[i]) && !char.IsWhiteSpace(text[i]) && !char.IsPunctuation(text[i]))
                {
                    i++;
                }

                // 2. Consume vowel cluster (if any)
                int vowelStart = i;
                while (i < n && IsVowel(text[i]))
                {
                    i++;
                }
                int vowelLength = i - vowelStart;

                // If we consumed some characters
                if (i > syllableStart)
                {
                    string chunk = text.Substring(syllableStart, i - syllableStart);
                    string phoneme = "-";
                    
                    if (vowelLength > 0)
                    {
                        // Map the first vowel of the cluster to a phoneme
                        phoneme = GetPhonemeFromChar(text[vowelStart]);
                    }
                    else
                    {
                        // Trailing consonants with no vowel (e.g. "s", "r" at end of sentence)
                        char lastChar = char.ToLowerInvariant(chunk[chunk.Length - 1]);
                        if (lastChar == 'm' || lastChar == 'n') phoneme = "N";
                        else phoneme = "-";
                    }

                    list.Add(new SyllableInfo { text = chunk, phoneme = phoneme, durationWeight = chunk.Length });
                }
                else
                {
                    i++;
                }
            }
            return list;
        }

        private bool IsVowel(char c)
        {
            c = char.ToLowerInvariant(c);
            return c == 'a' || c == 'á' || c == 'à' || c == 'â' || c == 'ã' ||
                   c == 'e' || c == 'é' || c == 'è' || c == 'ê' ||
                   c == 'i' || c == 'í' || c == 'ì' || c == 'î' ||
                   c == 'o' || c == 'ó' || c == 'ò' || c == 'ô' || c == 'õ' ||
                   c == 'u' || c == 'ú' || c == 'ù' || c == 'û';
        }

        private string GetPhonemeFromChar(char c)
        {
            c = char.ToLowerInvariant(c);
            if (c == 'a' || c == 'á' || c == 'à' || c == 'â' || c == 'ã') return "A";
            if (c == 'e' || c == 'é' || c == 'è' || c == 'ê') return "E";
            if (c == 'i' || c == 'í' || c == 'ì' || c == 'î') return "I";
            if (c == 'o' || c == 'ó' || c == 'ò' || c == 'ô' || c == 'õ') return "O";
            if (c == 'u' || c == 'ú' || c == 'ù' || c == 'û') return "U";
            if (c == 'n' || c == 'm') return "N";
            return "-";
        }

        private IEnumerator CoTextDecodedLipSync(string message, float duration)
        {
            if (_uLipSyncBlendShape == null) yield break;

            if (_uLipSync != null)
            {
                _uLipSync.enabled = false;
            }

            var syllables = ParseSyllables(message);
            if (syllables.Count == 0) yield break;

            float totalWeight = 0f;
            foreach (var syl in syllables)
            {
                totalWeight += syl.durationWeight;
            }

            float[] syllableEndTimes = new float[syllables.Count];
            float accumulatedTime = 0f;
            for (int i = 0; i < syllables.Count; i++)
            {
                float pct = syllables[i].durationWeight / totalWeight;
                accumulatedTime += pct * duration;
                syllableEndTimes[i] = accumulatedTime;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_uLipSyncBlendShape == null) break;

                int currentSyllableIndex = 0;
                for (int i = 0; i < syllableEndTimes.Length; i++)
                {
                    if (elapsed <= syllableEndTimes[i])
                    {
                        currentSyllableIndex = i;
                        break;
                    }
                }
                currentSyllableIndex = Mathf.Clamp(currentSyllableIndex, 0, syllables.Count - 1);
                var currentSyllable = syllables[currentSyllableIndex];

                string phoneme = currentSyllable.phoneme;
                bool isSilence = (phoneme == "-");
                float targetRawVolume = isSilence ? 0f : Mathf.Pow(10f, _uLipSyncBlendShape.maxVolume);

                var ratios = new Dictionary<string, float>();
                if (!isSilence)
                {
                    ratios[phoneme] = 1f;
                }
                else
                {
                    ratios["-"] = 1f;
                }

                var info = new uLipSync.LipSyncInfo
                {
                    phoneme = phoneme,
                    volume = isSilence ? 0f : 1f,
                    rawVolume = targetRawVolume,
                    phonemeRatios = ratios
                };

                _uLipSyncBlendShape.OnLipSyncUpdate(info);

                yield return null;
                elapsed += Time.deltaTime;
            }

            if (_uLipSyncBlendShape != null)
            {
                var silentRatios = new Dictionary<string, float> { { "-", 1f } };
                var silentInfo = new uLipSync.LipSyncInfo
                {
                    phoneme = "-",
                    volume = 0f,
                    rawVolume = 0f,
                    phonemeRatios = silentRatios
                };
                _uLipSyncBlendShape.OnLipSyncUpdate(silentInfo);
            }

            if (_uLipSync != null)
            {
                _uLipSync.enabled = true;
            }

            _textLipSyncCoroutine = null;
        }

        private bool TryAnimateSimpleLipSync(string message)
        {
            if (_animator == null) return false;

            float duration = Mathf.Clamp(message.Length * _textLipSyncDurationPerCharacter, _textLipSyncMinDuration, _textLipSyncMaxDuration);

            if (HasAnimatorParameter("IsSpeaking", AnimatorControllerParameterType.Bool))
            {
                StartCoroutine(CoTemporaryAnimatorBool("IsSpeaking", true, duration));
                return true;
            }

            if (HasAnimatorParameter("Speaking", AnimatorControllerParameterType.Bool))
            {
                StartCoroutine(CoTemporaryAnimatorBool("Speaking", true, duration));
                return true;
            }

            if (HasAnimatorParameter("Speak", AnimatorControllerParameterType.Trigger))
            {
                _animator.SetTrigger("Speak");
                return true;
            }

            if (HasAnimatorParameter("Talk", AnimatorControllerParameterType.Trigger))
            {
                _animator.SetTrigger("Talk");
                return true;
            }

            return false;
        }

        private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType type)
        {
            if (_animator == null || string.IsNullOrEmpty(parameterName)) return false;

            foreach (var parameter in _animator.parameters)
            {
                if (parameter.name == parameterName && parameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator CoTemporaryAnimatorBool(string parameterName, bool value, float duration)
        {
            if (_animator == null) yield break;

            _animator.SetBool(parameterName, value);
            yield return new WaitForSeconds(duration);
            if (_animator != null)
            {
                _animator.SetBool(parameterName, !value);
            }
        }

        public void PlayAnimationClip(AnimationClip clip)
        {
            if (_animator == null || clip == null) return;

            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
            }

            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }

            if (_eyeBlinkHandler != null)
            {
                _eyeBlinkHandler.ForceOpenEyes();
            }

            _animationCoroutine = StartCoroutine(CoPlayAnimationClip(clip));
        }

        private IEnumerator CoPlayAnimationClip(AnimationClip clip)
        {
            // Play the clip using Playables API
            AnimationPlayableUtilities.PlayClip(_animator, clip, out _playableGraph);

            // Wait for clip duration
            yield return new WaitForSeconds(clip.length);

            // Clean up graph to return control back to Animator Controller
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }

            if (_eyeBlinkHandler != null)
            {
                _eyeBlinkHandler.ForceOpenEyes();
            }

            _animationCoroutine = null;
        }

        private void HandleFeedbackReceived(AgentActions feedback)
        {
            if (feedback == null) return;

            // Prefer explicit animate command if present
            string animName = null;
            if (feedback.animate != null && !string.IsNullOrEmpty(feedback.animate.name))
            {
                animName = feedback.animate.name;
            }
            else if (!string.IsNullOrEmpty(feedback.task))
            {
                // Backwards compatibility: allow task to be a direct animation key,
                // legacy format "animate:Walk", or the new "animate(animation_name)" syntax.
                if (feedback.task.StartsWith("animate:"))
                {
                    animName = feedback.task.Substring("animate:".Length).Trim();
                }
                else if (feedback.task.StartsWith("animate(") && feedback.task.EndsWith(")"))
                {
                    animName = feedback.task.Substring(8, feedback.task.Length - 9).Trim();
                }
                else
                {
                    animName = feedback.task;
                }
            }

            if (!string.IsNullOrEmpty(animName))
            {
                Debug.Log($"[AvatarAnimatorWrapper] Received animation command from AgentWrapper: '{animName}'");
                PlayAnimation(animName);
            }

            // Look target assignment if target is specified
            if (_characterActor != null)
            {
                if (!string.IsNullOrEmpty(feedback.target))
                {
                    _characterActor.FindTarget(feedback.target);
                }
                else
                {
                    _characterActor.ClearTarget();
                }
            }
        }

        private void OnGUI()
        {
            if (!showDebugGUI) return;

            float width = 280f;
            float height = 380f;
            float x = 10f;
            float y = 10f;

            // Apply a nice semi-transparent dark skin style for the window
            GUI.Box(new Rect(x, y, width, height), "");
            GUILayout.BeginArea(new Rect(x + 10, y + 10, width - 20, height - 20));
            
            GUILayout.Label("<b>AVATAR ANIMATION DEBUGGER</b>", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true });
            GUILayout.Space(5);

            debugOverride = GUILayout.Toggle(debugOverride, "Enable Debug Override");
            GUILayout.Space(10);

            if (debugOverride)
            {
                GUILayout.Label($"Pleasure: {debugPleasure:F2}");
                debugPleasure = GUILayout.HorizontalSlider(debugPleasure, -1f, 1f);

                GUILayout.Label($"Arousal: {debugArousal:F2}");
                debugArousal = GUILayout.HorizontalSlider(debugArousal, 0f, 1f);

                GUILayout.Label($"Dominance: {debugDominance:F2}");
                debugDominance = GUILayout.HorizontalSlider(debugDominance, 0f, 1f);

                GUILayout.Space(10);
                GUILayout.Label("<b>Play Animations:</b>", new GUIStyle(GUI.skin.label) { richText = true });
                
                if (_characterActor != null && _characterActor.characterSO != null && _characterActor.characterSO.animations != null && _characterActor.characterSO.animations.Length > 0)
                {
                    for (int i = 0; i < _characterActor.characterSO.animations.Length; i++)
                    {
                        var anim = _characterActor.characterSO.animations[i];
                        if (!string.IsNullOrEmpty(anim.key) && GUILayout.Button(anim.key))
                        {
                            PlayAnimation(anim.key);
                        }
                    }
                }
                else
                {
                    GUILayout.Label("No animations in CharacterSO.");
                }
            }
            else
            {
                if (_characterActor != null)
                {
                    GUILayout.Label($"Pleasure: {_characterActor.currentPleasure:F2}");
                    GUILayout.Label($"Arousal: {_characterActor.currentArousal:F2}");
                    GUILayout.Label($"Dominance: {_characterActor.currentDominance:F2}");
                    
                    GameObject targetObj = _characterActor.CurrentTarget;
                    GUILayout.Label($"Look Target: {(targetObj != null ? targetObj.name : "None")}");
                }
                
                GUILayout.Label($"Cur Pleasure: {_currentPleasure:F2}");
                GUILayout.Label($"Cur Arousal: {_currentArousal:F2}");
                GUILayout.Label($"Cur Dominance: {_currentDominance:F2}");
                GUILayout.Label($"Look Rig Weight: {_currentLookWeight:F2}");
            }

            GUILayout.EndArea();
        }
    }
}
