using Conkist.GDK;
using UnityEngine;
using UnityEngine.Events;

namespace Conkist.GDK.AI
{
    [DefaultExecutionOrder(-90)]
    public class ApplicationManager : SingletonBehaviour<ApplicationManager>
    {
        [Header("Settings")]
        [SerializeField] private SmartMascotsSettings _settings;

        public SmartMascotsSettings Settings
        {
            get => _settings;
            set => _settings = value;
        }

        [Header("Events")]
        public UnityEvent OnCloseApplication;

        public void CloseApplication()
        {
            Debug.Log("[ApplicationManager] CloseApplication invoked.");
            OnCloseApplication?.Invoke();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void RestartApp()
        {
            Debug.Log("[ApplicationManager] RestartApp invoked. Reloading active scene...");
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
