namespace Conkist.GDK.SmartAgents
{
    [System.Serializable]
    public class AgentActions
    {
        public string message;
        public float pleasure_mod;
        public float arousal_mod;
        public float dominance_mod;
        public string task;
        public string target;
        public float duration;

        [System.Serializable]
        public class AnimateCommand
        {
            public string name;
            public float speed = 1f;
            public int layer = 0;
        }

        public AnimateCommand animate;

        public float resultingPleasure;
        public float resultingArousal;
        public float resultingDominance;

        // Legacy compatibility fields
        public int affinity_mod;
        public int excitement_mod;
        public int resultingAffinity;
        public int resultingExcitement;
    }

    [System.Serializable]
    public class AgentFeedback
    {
        public string thought;
        public AgentActions[] actions;
    }
}
