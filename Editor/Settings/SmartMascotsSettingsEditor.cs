using System;
using System.Linq;
using LLMUnity;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>
    /// Inspector for SmartMascotsSettings. Shows the Offline or Online group by provider.
    /// Has no reference to Whisper, ElevenLabs or uLipSync types: it reads their scripting symbols and loaded assemblies only.
    /// </summary>
    [CustomEditor(typeof(SmartMascotsSettings))]
    public class SmartMascotsSettingsEditor : UnityEditor.Editor
    {
        private const string LowEndModel = "Qwen3.5-2B-Q4_K_M.gguf";
        private bool _showKeys;

        [MenuItem("Conkist/GDK/AI/Settings")]
        private static void Open()
        {
            const string path = "Assets/SmartMascotsSettings.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SmartMascotsSettings>(path);
            if (asset == null)
            {
                asset = CreateInstance<SmartMascotsSettings>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Section("LLM Provider");
            var provider = serializedObject.FindProperty("provider");
            EditorGUILayout.PropertyField(provider, new GUIContent("Provider (LLMUnity = offline, OpenRouter = online)"));

            if ((LLMProvider)provider.enumValueIndex == LLMProvider.LLMUnity) OfflineGroup();
            else OnlineGroup();

            Section("RAG (knowledge base, local only)");
            var rag = serializedObject.FindProperty("rag");
            Field(rag, "enabled", "Use RAG");
            using (new EditorGUI.DisabledScope(!rag.FindPropertyRelative("enabled").boolValue))
            {
                Field(rag, "topResults", "Results per question");
                Field(rag, "preferEmbeddings", "Use embeddings when available");
                Field(rag, "forceRebuild", "Rebuild embeddings on start");
            }
            EditorGUILayout.HelpBox("Embeddings need an LLMUnity RAG component and do not run on WebGL. Otherwise a keyword index (BM25) is used. No online RAG.", MessageType.None);

            Section("Speech to Text");
            ProviderRow(serializedObject.FindProperty("speechToText"), "showRecordButton", "Show record button", IsSymbolOn("CONKIST_WHISPER"),
                "Whisper not enabled. Open Conkist > GDK > AI > Setup Whisper.", "Conkist/GDK/AI/Setup Whisper");

            Section("Text to Speech (ElevenLabs)");
            var tts = serializedObject.FindProperty("textToSpeech");
            ProviderRow(tts, "enableElevenLabs", "Use ElevenLabs", IsSymbolOn("CONKIST_ELEVENLABS"),
                "ElevenLabs not enabled. Open Conkist > GDK > AI > Setup ElevenLabs.", "Conkist/GDK/AI/Setup ElevenLabs");
            using (new EditorGUI.DisabledScope(!tts.FindPropertyRelative("enableElevenLabs").boolValue))
            {
                SecretRow(tts.FindPropertyRelative("apiKey"), "API key");
                Field(tts, "voiceId", "Voice ID");
                Field(tts, "modelId", "Model ID");
            }

            Section("Lip Sync");
            bool lip = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "uLipSync.Runtime");
            ProviderRow(serializedObject.FindProperty("lipSync"), "enabled", "Use lip sync", lip,
                "uLipSync not installed. Open Conkist > GDK > AI > Setup Lipsync.", "Conkist/GDK/AI/Setup Lipsync");

            serializedObject.ApplyModifiedProperties();
        }

        private void OfflineGroup()
        {
            Section("Offline (LLMUnity)");
            var o = serializedObject.FindProperty("offline");
            ModelRow(o.FindPropertyRelative("model"));
            Field(o, "jsonGrammar", "Force complete JSON (grammar)");
            using (new EditorGUI.DisabledScope(!o.FindPropertyRelative("jsonGrammar").boolValue))
                Field(o, "requireAnimation", "Require a body animation in every action");
            Field(o, "applyToSceneLLM", "Apply values below to the scene LLM");
            using (new EditorGUI.DisabledScope(!o.FindPropertyRelative("applyToSceneLLM").boolValue))
            {
                Field(o, "contextSize", "Context size");
                Field(o, "numGPULayers", "GPU layers");
                Field(o, "numThreads", "Threads (-1 = auto)");
                Field(o, "reasoning", "Thinking mode (keep off for JSON)");
                Field(o, "temperature", "Temperature");
                Field(o, "numPredict", "Max tokens (-1 = no limit)");
            }
            if (GUILayout.Button("Low-end phone preset (Qwen 3.5 2B, context 2048, thinking off)")) ApplyLowEndPreset(o);
        }

        private void OnlineGroup()
        {
            Section("Online (OpenRouter)");
            var o = serializedObject.FindProperty("online");
            Field(o, "model", "Model or preset");
            SecretRow(o.FindPropertyRelative("apiKey"), "API key");
        }

        // The base model. A per-agent override lives on the ChatAgentWrapper and is applied after this one.
        private static void ModelRow(SerializedProperty model)
        {
            string[] names = LLMManager.modelEntries.Where(e => !e.lora && !e.embeddingOnly).Select(e => e.filename).ToArray();
            int current = Array.IndexOf(names, model.stringValue);
            int picked = EditorGUILayout.Popup("Base model (installed)", current, names.Length > 0 ? names : new[] { "(none added to LLMUnity)" });
            if (names.Length > 0 && picked >= 0 && picked != current) model.stringValue = names[picked];
            EditorGUILayout.PropertyField(model, new GUIContent("Base model file name"));
            if (!string.IsNullOrEmpty(model.stringValue) && !names.Contains(model.stringValue))
                EditorGUILayout.HelpBox("This model is not in LLMUnity's model list. Add or download it on the LLM component first, or loading fails at startup.", MessageType.Warning);
            else if (string.IsNullOrEmpty(model.stringValue))
                EditorGUILayout.HelpBox("Empty: the model set on the scene's LLM component is used.", MessageType.None);
        }

        private static void ApplyLowEndPreset(SerializedProperty o)
        {
            o.FindPropertyRelative("model").stringValue = LowEndModel;
            o.FindPropertyRelative("applyToSceneLLM").boolValue = true;
            o.FindPropertyRelative("contextSize").intValue = 2048;
            o.FindPropertyRelative("reasoning").boolValue = false;
            o.FindPropertyRelative("temperature").floatValue = 0.2f;
        }

        private static void Field(SerializedProperty group, string name, string label) =>
            EditorGUILayout.PropertyField(group.FindPropertyRelative(name), new GUIContent(label));

        private static void ProviderRow(SerializedProperty group, string prop, string label, bool installed, string missingHelp, string menu)
        {
            using (new EditorGUI.DisabledScope(!installed))
                Field(group, prop, label);
            if (installed) return;
            EditorGUILayout.HelpBox(missingHelp, MessageType.Info);
            if (GUILayout.Button("Open setup")) EditorApplication.ExecuteMenuItem(menu);
        }

        private void SecretRow(SerializedProperty p, string label)
        {
            EditorGUILayout.BeginHorizontal();
            p.stringValue = _showKeys
                ? EditorGUILayout.TextField(label, p.stringValue)
                : EditorGUILayout.PasswordField(label, p.stringValue);
            _showKeys = GUILayout.Toggle(_showKeys, "Show", "Button", GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(p.stringValue))
                EditorGUILayout.HelpBox("Key stored in this asset. Do not commit it and do not ship it in a player build; use a proxy server instead.", MessageType.Warning);
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static bool IsSymbolOn(string symbol) =>
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains(symbol);
    }
}
