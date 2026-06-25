using UnityEngine;
using UnityEngine.Events;
using System.Runtime.InteropServices;

namespace Conkist.GDK.SmartAgents
{
    public class WebGLAgentWrapper : AgentWrapper
    {
        [Header("WebGL Bridge Settings")]
        [SerializeField] private string _jsMethodName = "OnUnityMessageSubmitted";

        [Header("Events")]
        public UnityEvent<string> OnExternalMessageSubmitted;

        [Header("Debug & Testing")]
        [TextArea(5, 10)]
        [SerializeField] private string _simulatedJsonFeedback;

        #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WebGLBridge_PostMessage(string methodName, string data);
        #endif

        public override void ForceMessageToAgent(string text)
        {
            Debug.Log($"[WebGLAgentWrapper] ForceMessageToAgent: {text}");
            
            // Set typing indicator while we wait for external host to query LLM and reply
            SetTyping(true);

            // Invoke local event for custom standalone listeners
            OnExternalMessageSubmitted?.Invoke(text);

            #if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                WebGLBridge_PostMessage(_jsMethodName, text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WebGLAgentWrapper] Failed to call external JS method {_jsMethodName}: {ex.Message}");
            }
            #else
            Debug.Log($"[WebGLAgentWrapper] Standalone/Editor mode - JS message dispatch skipped. Method: '{_jsMethodName}', Message: '{text}'");
            #endif
        }

        /// <summary>
        /// This method is intended to be called by WebGL JavaScript host using:
        /// unityInstance.SendMessage('YourCharacterObjectName', 'OnReceiveExternalFeedback', jsonString);
        /// </summary>
        /// <param name="jsonResponse">The JSON string matching the AgentFeedback or AgentActions[] schema.</param>
        public void OnReceiveExternalFeedback(string jsonResponse)
        {
            Debug.Log($"[WebGLAgentWrapper] OnReceiveExternalFeedback received payload:\n{jsonResponse}");

            string cleaned = CleanJsonResponse(jsonResponse);
            var feedbacks = DeserializeAgentFeedbacks(cleaned);
            if (feedbacks != null && feedbacks.Length > 0)
            {
                StopActiveFeedback();
                _feedbackCoroutine = StartCoroutine(ExecuteFeedbacksSequentially(feedbacks));
            }
            else
            {
                Debug.LogError("[WebGLAgentWrapper] Failed to parse received external JSON feedback.");
                SetTyping(false);
                OnAllActionsCompleted?.Invoke();
            }
        }

        [ContextMenu("Simulate JS Feedback")]
        public void SimulateJsFeedback()
        {
            if (string.IsNullOrEmpty(_simulatedJsonFeedback))
            {
                Debug.LogWarning("[WebGLAgentWrapper] Please fill in the Simulated Json Feedback field in the inspector before simulating.");
                return;
            }
            OnReceiveExternalFeedback(_simulatedJsonFeedback);
        }
    }
}
