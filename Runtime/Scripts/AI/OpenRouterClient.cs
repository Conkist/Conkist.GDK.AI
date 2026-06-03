using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Conkist.GDK.Serialization;

namespace Conkist.GDK.SmartAgents
{
    [System.Serializable]
    public class OpenRouterMessage
    {
        public string role;
        public string content;

        public OpenRouterMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [System.Serializable]
    public class OpenRouterResponse
    {
        public Choice[] choices;

        [System.Serializable]
        public class Choice
        {
            public OpenRouterMessage message;
        }
    }

    /// <summary>
    /// A serializable client to interact with the OpenRouter API.
    /// Integrates with the Unity Inspector and manages its own history.
    /// </summary>
    [System.Serializable]
    public class OpenRouterClient
    {
        [SerializeField] private string apiKey;
        [SerializeField] private string model = "meta-llama/llama-3-8b-instruct:free";

        private string _systemPrompt;
        private List<OpenRouterMessage> _history = new List<OpenRouterMessage>();

        public string ApiKey
        {
            get => apiKey;
            set => apiKey = value;
        }

        public string Model
        {
            get => model;
            set => model = value;
        }

        public void Initialize(string systemPrompt)
        {
            _systemPrompt = systemPrompt;
            ClearHistory();
        }

        public void ClearHistory()
        {
            _history.Clear();
            if (!string.IsNullOrEmpty(_systemPrompt))
            {
                _history.Add(new OpenRouterMessage("system", _systemPrompt));
            }
        }

        public async System.Threading.Tasks.Task<string> ChatAsync(string promptToSend)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[OpenRouterClient] OpenRouter API Key is missing! Please assign it in the Inspector.");
                return "";
            }

            // Add user message to history
            _history.Add(new OpenRouterMessage("user", promptToSend));

            // Create Request payload using an anonymous type serialized to JSON
            var requestPayload = new
            {
                model = model,
                messages = _history,
                response_format = new { type = "json_object" }
            };

            string jsonPayload = requestPayload.ToJson();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

            string url = "https://openrouter.ai/api/v1/chat/completions";

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();

                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                // OpenRouter optional but recommended headers
                www.SetRequestHeader("HTTP-Referer", "https://github.com/renatoconkist/SmartAgents");
                www.SetRequestHeader("X-Title", "SmartAgents Unity Client");

                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[OpenRouterClient] OpenRouter API Error: {www.error}\nResponse: {www.downloadHandler.text}");
                    return "";
                }

                string responseJson = www.downloadHandler.text;
                Debug.Log($"[OpenRouterClient] OpenRouter Response: {responseJson}");

                try
                {
                    var rawResponse = responseJson.FromJson<OpenRouterResponse>();
                    if (rawResponse != null && rawResponse.choices != null && rawResponse.choices.Length > 0)
                    {
                        string reply = rawResponse.choices[0].message.content;
                        // Add assistant reply to history
                        _history.Add(new OpenRouterMessage("assistant", reply));
                        return reply;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[OpenRouterClient] Failed to parse OpenRouter response: {ex.Message}");
                }

                return "";
            }
        }
    }
}
