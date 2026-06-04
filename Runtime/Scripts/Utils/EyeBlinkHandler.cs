using System.Collections;
using UnityEngine;

namespace Conkist.AI.Animation
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

        // Start is called before the first frame update
        void Start()
        {
            ScheduleNextBlink();
        }

        // Update is called once per frame
        void Update()
        {
            if (!isBlinking)
            {
                blinkTimer -= Time.deltaTime;
                if (blinkTimer <= 0)
                {
                    StartCoroutine(Blink(0)); // Default to both eyes
                    ScheduleNextBlink();
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

            // Blink closing
            float t = 0;
            while (t < blinkDuration)
            {
                t += Time.deltaTime;
                foreach (var eye in eyes)
                {
                    if ((side == 0) || (side == 1 && eye.isRightEye) || (side == -1 && !eye.isRightEye))
                    {
                        foreach(var blend in eye.config)
                        {
                            float blendValue = blend.GetRelativeValue(t / blinkDuration);
                            headMesh.SetBlendShapeWeight(blend.blendShapeIndex, blendValue);
                        }
                    }
                }
                yield return null;
            }

            // Blink opening
            t = 0;
            while (t < blinkDuration)
            {
                t += Time.deltaTime;
                foreach (var eye in eyes)
                {
                    if ((side == 0) || (side == 1 && eye.isRightEye) || (side == -1 && !eye.isRightEye))
                    {
                        foreach (var blend in eye.config)
                        {
                            float blendValue = blend.GetRelativeValue(t / blinkDuration, inverse: true);
                            headMesh.SetBlendShapeWeight(blend.blendShapeIndex, blendValue);
                        }
                    }
                }
                yield return null;
            }

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
            foreach (var eye in eyes)
            {
                foreach(var blend in eye.config)
                {
                    headMesh.SetBlendShapeWeight(blend.blendShapeIndex, blend.startValue);
                }
            }
            isBlinking = false;
        }
    }
}