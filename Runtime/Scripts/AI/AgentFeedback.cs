namespace Conkist.GDK.SmartAgents
{
    [System.Serializable]
    public class AgentFeedback
    {
        public string thought;
        public string message;
        public int affinity_mod;
        public int excitement_mod;
        public string task;
        public string target;
        public float duration;
    }
}
