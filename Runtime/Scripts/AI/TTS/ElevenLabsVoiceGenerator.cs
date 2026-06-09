using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Conkist.GDK.SmartAgents.TTS
{
    /// <summary>
    /// ElevenLabs implementation for Text-To-Speech generation.
    /// </summary>
    public class ElevenLabsVoiceGenerator : VoiceGeneratorBase
    {
        [Header("ElevenLabs Settings")]
        public string apiKey;
        public string voiceId = "21m00Tcm4TlvDq8ikWAM"; // Default: Rachel
        public string modelId = "eleven_multilingual_v2";

        [Range(0f, 1f)] public float stability = 0.5f;
        [Range(0f, 1f)] public float similarityBoost = 0.75f;

        [System.Serializable]
        private class ElevenLabsRequest
        {
            public string text;
            public string model_id;
            public VoiceSettings voice_settings;
        }

        [System.Serializable]
        private class VoiceSettings
        {
            public float stability;
            public float similarity_boost;
        }

        public override async Task<AudioClip> TextToSpeech(string text, string customVoiceId = null)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[ElevenLabs] API Key is missing! Please assign it in the Inspector.");
                return null;
            }

            string activeVoiceId = !string.IsNullOrEmpty(customVoiceId) ? customVoiceId : voiceId;
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{activeVoiceId}";

            var requestData = new ElevenLabsRequest
            {
                text = text,
                model_id = modelId,
                voice_settings = new VoiceSettings
                {
                    stability = stability,
                    similarity_boost = similarityBoost
                }
            };

            string json = JsonUtility.ToJson(requestData);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);

                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("xi-api-key", apiKey);
                www.SetRequestHeader("Accept", "audio/mpeg");

                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    string responseText = "";
                    if (www.downloadHandler != null && www.downloadHandler.data != null)
                    {
                        try
                        {
                            responseText = Encoding.UTF8.GetString(www.downloadHandler.data);
                        }
                        catch (System.Exception) {}
                    }
                    Debug.LogError($"[ElevenLabs] API Error: {www.error}\nResponse: {responseText}");
                    return null;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                return clip;
            }
        }
    }
}
