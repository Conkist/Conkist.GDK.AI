using UnityEngine;

namespace Conkist.GDK.SmartAgents
{
    [CreateAssetMenu(fileName = "ApplicationSettings", menuName = "SmartAgents/ApplicationSettings")]
    public class ApplicationSettingsSO : ScriptableObject
    {
        [Header("Default LLM Settings")]
        public LLMProvider defaultProvider = LLMProvider.LLMUnity;

        [Header("Default OpenRouter Settings")]
        public string defaultOpenRouterApiKey;
        public string defaultOpenRouterModel = "meta-llama/llama-3-8b-instruct:free";
    }
}
