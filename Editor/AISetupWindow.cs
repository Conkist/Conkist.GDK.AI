using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>
    /// One small setup window per optional piece of Conkist GDK AI: Lipsync, Whisper, ElevenLabs.
    /// Deliberately has no reference to the runtime assembly, so it still compiles when a dependency is missing.
    /// Package installs and scripting symbols change only when the user clicks a button.
    /// </summary>
    public class AISetupWindow : EditorWindow
    {
        private enum Kind { Lipsync, Whisper, ElevenLabs }

        private const string ULipSyncId = "com.hecomi.ulipsync";
        private const string ULipSyncUrl = "https://github.com/hecomi/uLipSync.git#upm";
        private const string WhisperId = "com.whisper.unity";
        private const string WhisperUrl = "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity";
        private const string ElevenLabsSymbol = "CONKIST_ELEVENLABS";
        private const string WhisperSymbol = "CONKIST_WHISPER";
        private const string WarnedKey = "Conkist.GDK.AI.SetupWarned";

        private static readonly NamedBuildTarget[] SymbolTargets =
        {
            NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL
        };

        [SerializeField] private Kind _kind;
        private ListRequest _list;

        // Whisper model download (ggml models from the whisper.cpp Hugging Face repo).
        private const string ModelBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
        private static readonly string[] ModelNames = { "ggml-tiny.bin", "ggml-base.bin", "ggml-small.bin" };
        private static readonly string[] ModelLabels = { "tiny (~75 MB, fastest)", "base (~142 MB)", "small (~466 MB, best quality)" };
        [SerializeField] private int _modelIndex;
        private UnityWebRequest _modelRequest;
        private string _modelTarget;
        private AddRequest _add;
        private RemoveRequest _remove;
        private HashSet<string> _installed = new HashSet<string>();
        private HashSet<string> _embedded = new HashSet<string>();

        [MenuItem("Conkist/GDK/AI/Setup Lipsync")]
        private static void OpenLipsync() => Open(Kind.Lipsync);

        [MenuItem("Conkist/GDK/AI/Setup Whisper")]
        private static void OpenWhisper() => Open(Kind.Whisper);

        [MenuItem("Conkist/GDK/AI/Setup ElevenLabs")]
        private static void OpenElevenLabs() => Open(Kind.ElevenLabs);

        private static void Open(Kind kind)
        {
            var window = CreateWindow<AISetupWindow>($"Setup {kind}");
            window._kind = kind;
            window.minSize = new Vector2(380, 150);
            window.Show();
        }

        // uLipSync is required but cannot be declared in package.json (UPM does not accept git URLs there).
        [InitializeOnLoadMethod]
        private static void WarnIfLipsyncMissing()
        {
            if (SessionState.GetBool(WarnedKey, false)) return;
            SessionState.SetBool(WarnedKey, true);
            var request = Client.List(true);
            EditorApplication.update += Poll;

            void Poll()
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success && !request.Result.Any(p => p.name == ULipSyncId))
                {
                    Debug.LogWarning("[Conkist GDK AI] Required package uLipSync is not installed. Open Conkist > GDK > AI > Setup Lipsync.");
                }
            }
        }

        private void OnEnable() => Refresh();
        private void OnDisable() => CancelModelDownload();

        private void Refresh() => _list = Client.List(true);

        private void OnInspectorUpdate()
        {
            if (_modelRequest != null)
            {
                if (_modelRequest.isDone) FinishModelDownload();
                Repaint();
            }
            if (_list != null && _list.IsCompleted)
            {
                if (_list.Status == StatusCode.Success)
                {
                    _installed = new HashSet<string>(_list.Result.Select(p => p.name));
                    _embedded = new HashSet<string>(_list.Result.Where(p => p.source == PackageSource.Embedded).Select(p => p.name));
                }
                _list = null;
                Repaint();
            }
            if (_remove != null && _remove.IsCompleted)
            {
                if (_remove.Status == StatusCode.Failure) Debug.LogError($"[Conkist GDK AI] Remove failed: {_remove.Error.message}");
                _remove = null;
                Refresh();
            }
            if (_add != null && _add.IsCompleted)
            {
                if (_add.Status == StatusCode.Failure) Debug.LogError($"[Conkist GDK AI] Install failed: {_add.Error.message}");
                else if (_add.Result.name == WhisperId) SetSymbol(WhisperSymbol, true);
                _add = null;
                Refresh();
            }
        }

        private void WhisperRow()
        {
            bool installed = _installed.Contains(WhisperId);
            bool symbolOn = IsSymbolOn(WhisperSymbol);
            bool on = installed && symbolOn;
            bool busy = _add != null || _remove != null;
            string state = on ? "Enabled" : installed ? "Package installed, symbol off" : "Disabled";
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Whisper (optional)", state);
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button(on ? "Disable" : "Enable", GUILayout.Width(90)))
                {
                    if (on)
                    {
                        // Symbol first: a symbol without the package would not compile.
                        SetSymbol(WhisperSymbol, false);
                        // An embedded copy lives in the project's Packages folder: never remove it, only clear the symbol.
                        if (!_embedded.Contains(WhisperId)) _remove = Client.Remove(WhisperId);
                        else Refresh();
                    }
                    else if (installed)
                    {
                        // Package already there (for example embedded): only the symbol is missing.
                        SetSymbol(WhisperSymbol, true);
                        Refresh();
                    }
                    else
                    {
                        _add = Client.Add(WhisperUrl);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Local speech to text (ISpeechToText). Native platforms only; not used on WebGL. Enable installs whisper.unity (if missing) and sets CONKIST_WHISPER on Standalone, Android, iOS and WebGL. Disable clears the symbol and removes the package, unless it is embedded in the project. After enabling, select a GameObject and run Conkist > GDK > AI > Add Whisper Speech-to-Text to Selection. Keep an AILifetimeScope in the scene.", MessageType.None);
        }

        private static string ModelDir => Path.Combine(Application.dataPath, "StreamingAssets", "Whisper");

        private void WhisperModelRow()
        {
            string name = ModelNames[_modelIndex];
            bool exists = File.Exists(Path.Combine(ModelDir, name));
            bool busy = _modelRequest != null;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(busy))
                _modelIndex = EditorGUILayout.Popup("Whisper model", _modelIndex, ModelLabels);
            using (new EditorGUI.DisabledScope(busy || exists))
            {
                if (GUILayout.Button(exists ? "Installed" : "Download", GUILayout.Width(90))) StartModelDownload(name);
            }
            EditorGUILayout.EndHorizontal();

            if (busy)
            {
                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(rect, _modelRequest.downloadProgress, $"{name}  {(_modelRequest.downloadProgress * 100f):0}%");
                if (GUILayout.Button("Cancel")) CancelModelDownload();
            }
            EditorGUILayout.HelpBox($"Saves the model in Assets/StreamingAssets/Whisper/ ({name}). Then set the WhisperManager Model Path to Whisper/{name}. Models are large: keep them out of git (add Assets/StreamingAssets/Whisper/*.bin to .gitignore).", MessageType.None);
        }

        private void StartModelDownload(string name)
        {
            Directory.CreateDirectory(ModelDir);
            _modelTarget = Path.Combine(ModelDir, name);
            string temp = _modelTarget + ".download";
            _modelRequest = UnityWebRequest.Get(ModelBaseUrl + name);
            _modelRequest.downloadHandler = new DownloadHandlerFile(temp) { removeFileOnAbort = true };
            _modelRequest.SendWebRequest();
        }

        private void CancelModelDownload()
        {
            _modelRequest?.Abort();
            _modelRequest?.Dispose();
            _modelRequest = null;
        }

        private void FinishModelDownload()
        {
            var req = _modelRequest;
            _modelRequest = null;
            string temp = _modelTarget + ".download";
            if (req.result == UnityWebRequest.Result.Success)
            {
                if (File.Exists(_modelTarget)) File.Delete(_modelTarget);
                File.Move(temp, _modelTarget);
                AssetDatabase.Refresh();
                Debug.Log($"[Conkist GDK AI] Whisper model saved: {_modelTarget}");
            }
            else
            {
                Debug.LogError($"[Conkist GDK AI] Whisper model download failed: {req.error}");
                if (File.Exists(temp)) File.Delete(temp);
            }
            req.Dispose();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            switch (_kind)
            {
                case Kind.Lipsync:
                    PackageRow("uLipSync (required)", ULipSyncId, ULipSyncUrl,
                        "Lip sync and facial blend shapes (ILipSyncDriver). Installed from GitHub. After install, select the avatar and run Conkist > GDK > AI > Add Lip Sync Driver to Selection.");
                    break;
                case Kind.Whisper:
                    WhisperRow();
                    WhisperModelRow();
                    break;
                case Kind.ElevenLabs:
                    SymbolRow("ElevenLabs (optional)", ElevenLabsSymbol,
                        "Text to speech (ITextToSpeech) through the ElevenLabs web API; no package is needed. After enabling, run Conkist > GDK > AI > Add ElevenLabs Text-to-Speech to Selection. Sets symbol CONKIST_ELEVENLABS on Standalone, Android, iOS and WebGL. Keep the API key out of builds that ship to players.");
                    break;
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_add != null))
            {
                if (GUILayout.Button("Refresh")) Refresh();
            }
        }

        private void PackageRow(string label, string packageId, string gitUrl, string help)
        {
            bool installed = _installed.Contains(packageId);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, installed ? "Installed" : "Missing");
            using (new EditorGUI.DisabledScope(installed || _add != null))
            {
                if (GUILayout.Button("Install", GUILayout.Width(90))) _add = Client.Add(gitUrl);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(help, MessageType.None);
        }

        private static void SymbolRow(string label, string symbol, string help)
        {
            bool on = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains(symbol);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, on ? "Enabled" : "Disabled");
            if (GUILayout.Button(on ? "Disable" : "Enable", GUILayout.Width(90))) SetSymbol(symbol, !on);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(help, MessageType.None);
        }

        private static bool IsSymbolOn(string symbol) =>
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains(symbol);

        private static void SetSymbol(string symbol, bool on)
        {
            foreach (var target in SymbolTargets)
            {
                var symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
                symbols.Remove(symbol);
                if (on) symbols.Add(symbol);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }
    }
}
