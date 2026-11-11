using System.Collections.Generic;
using UnityEngine;

namespace Conkist.GDK.AI
{
    public class CharacterActor : MonoBehaviour
    {
        public CharacterSO characterSO;

        public float currentPleasure { get; private set; } = 0f;
        public float currentArousal { get; private set; } = 0f;
        public float currentDominance { get; private set; } = 0.5f;
        private GameObject currentTarget;

        public GameObject CurrentTarget => currentTarget;

        public void UpdateMood(float pleasureDelta, float arousalDelta, float dominanceDelta)
        {
            currentPleasure += pleasureDelta;
            currentPleasure = Mathf.Clamp(currentPleasure, -1f, 1f);
            currentArousal += arousalDelta;
            currentArousal = Mathf.Clamp01(currentArousal);
            currentDominance += dominanceDelta;
            currentDominance = Mathf.Clamp01(currentDominance);
        }

        // Legacy compatibility accessors
        public int currentAffinity => Mathf.RoundToInt((currentPleasure - 0.5f) * 20f);
        public int currentExcitement => Mathf.RoundToInt((currentArousal - 0.5f) * 10f);

        public void FindTarget(string targetName)
        {
            currentTarget = GameObject.Find(targetName);
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }
    }
}
