using System.Collections;
using UnityEngine;

namespace Conkist.GDK.AI.Animation
{
    [System.Serializable]
    public class EyeBlendRangeConfig
    {
        public BlendShapeConfig[] config;
        public bool isRightEye;
    }

    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class EyeBlinkHandler : MonoBehaviour
    {
        // The SkinnedMeshRenderer that holds the blendshapes
        public SkinnedMeshRenderer headMesh;

        // List of blendshapes to control during a blink
        public EyeBlendRangeConfig[] eyes;

        // Blink duration in seconds
        public float blinkDuration = 0.1f;

        // Minimum and maximum time between random blinks in seconds
        public float minTimeBetweenBlinks = 3f;
        public float maxTimeBetweenBlinks = 10f;

        // Timer for random blinks
        private float blinkTimer;

        // Use this to track if a blink is currently happening
        private bool isBlinking;

        // Use this to suspend procedural random blinking
        public bool isBlinkingSuspended { get; set; } = false;

        private float _currentBlinkProgress;
        private bool _currentBlinkInverse;
        private int _currentBlinkSide;

        // Start is called before the first frame update
        void Start()
        {
            ScheduleNextBlink();
        }

        // Update is called once per frame
        void Update()
        {
            if (!isBlinking && !isBlinkingSuspended)
            {
                // Check if eyes are already closed/controlled by an external animator state
                bool externalClosed = false;
                if (eyes != null)
                {
                    foreach (var eye in eyes)
                    {
                        if (eye.config != null)
                        {
                            foreach (var blend in eye.config)
                            {
                                if (headMesh != null)
                                {
                                    float weight = headMesh.GetBlendShapeWeight(blend.blendShapeIndex);
                                    float maxDiff = Mathf.Abs(blend.endValue - blend.startValue);
                                    float threshold = maxDiff > 0f ? maxDiff * 0.75f : 75f;
                                    if (Mathf.Abs(weight - blend.startValue) > threshold)
                                    {
                                        externalClosed = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (externalClosed) break;
                    }
                }

                if (externalClosed)
                {
                    ScheduleNextBlink();
                    return;
                }

                blinkTimer -= Time.deltaTime;
                if (blinkTimer <= 0)
                {
                    StartCoroutine(Blink(0)); // Default to both eyes
                    ScheduleNextBlink();
                }
            }
        }

        void LateUpdate()
        {
            if (isBlinking)
            {
                ApplyBlinkWeights(_currentBlinkSide, _currentBlinkProgress, _currentBlinkInverse);
            }
        }

        private void ApplyBlinkWeights(int side, float progress, bool inverse)
        {
            if (headMesh == null || eyes == null) return;

            foreach (var eye in eyes)
            {
                if ((side == 0) || (side == 1 && eye.isRightEye) || (side == -1 && !eye.isRightEye))
                {
                    if (eye.config != null)
                    {
                        foreach (var blend in eye.config)
                        {
                            float blendValue = blend.GetRelativeValue(progress, inverse);
                            headMesh.SetBlendShapeWeight(blend.blendShapeIndex, blendValue);
                        }
                    }
                }
            }
        }

        private void ResetEyesToOpen()
        {
            if (eyes == null || headMesh == null) return;
            foreach (var eye in eyes)
            {
                if (eye.config != null)
                {
                    foreach (var blend in eye.config)
                    {
                        headMesh.SetBlendShapeWeight(blend.blendShapeIndex, blend.startValue);
                    }
                }
            }
        }

        // Schedule the next random blink
        void ScheduleNextBlink()
        {
            blinkTimer = Random.Range(minTimeBetweenBlinks, maxTimeBetweenBlinks);
        }

        // Coroutine for handling the blink animation with a parameter for side
        IEnumerator Blink(int side)
        {
            isBlinking = true;
            _currentBlinkSide = side;

            // Blink closing
            float t = 0;
            while (t < blinkDuration)
            {
                _currentBlinkProgress = t / blinkDuration;
                _currentBlinkInverse = false;
                yield return null;
                t += Time.deltaTime;
            }

            // Blink opening
            t = 0;
            while (t < blinkDuration)
            {
                _currentBlinkProgress = t / blinkDuration;
                _currentBlinkInverse = true;
                yield return null;
                t += Time.deltaTime;
            }

            ResetEyesToOpen();
            isBlinking = false;
        }

        // Public method to force a blink with a side parameter
        public void ForceBlink(int side = 0)
        {
            if (!isBlinking)
            {
                StartCoroutine(Blink(side));
            }
        }

        // Public method to force open eyes (override any ongoing blink)
        public void ForceOpenEyes()
        {
            StopAllCoroutines();
            ResetEyesToOpen();
            isBlinking = false;
        }
    }
}