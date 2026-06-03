using System.Collections.Generic;
using UnityEngine;

namespace Conkist.GDK.SmartAgents
{
    public class CharacterActor : MonoBehaviour
    {
        public CharacterSO characterSO;

        public int currentAffinity { get; private set; }
        public int currentExcitement { get; private set; }
        private GameObject currentTarget;

        public void UpdateMood(int affinity, int excitement)
        {
            currentAffinity += affinity;
            currentAffinity = Mathf.Clamp(currentAffinity, -10, 10);
            currentExcitement += excitement;
            currentExcitement = Mathf.Clamp(currentExcitement, -5, 5);
        }

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
