using System.Collections;
using System.IO;
using Conkist.GDK;
using Conkist.GDK.AI;
using Conkist.GDK.AI.Providers;
using VContainer;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UIElements;

public class ScreenManager : MonoBehaviour
{
    [Header("UI Documents")]
    [SerializeField] private UIDocument _menuScreenDoc;
    [SerializeField] private UIDocument _chatScreenDoc;
    [SerializeField] private UIDocument _optionScreenDoc;

    [Header("Transition Settings")]
    [SerializeField] private float _transitionDelay = 0.3f; // Default matched to 0.3s transitions

    [Header("Transition Events")]
    public UnityEvent OnTransitionToMenu;
    public UnityEvent OnTransitionToChat;
    public UnityEvent OnTransitionToOptions;

    [Header("Optional External Controllers")]
    [SerializeField] private ChatUIController _chatUIController;

    // Injected by AILifetimeScope; the null provider is used when the scene has no scope.
    [Inject] private ISpeechToText _speechToText;
    private ISpeechToText Stt => _speechToText ?? NullSpeechToText.Instance;

    private GameObject _previousScreenGo;
    private Coroutine _currentTransition;

    private void Awake()
    {
        // Set the initial active states: Menu active, others inactive
        if (_menuScreenDoc != null) _menuScreenDoc.gameObject.SetActive(true);
        if (_chatScreenDoc != null) _chatScreenDoc.gameObject.SetActive(false);
        if (_optionScreenDoc != null) _optionScreenDoc.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Play transitions for the initial Menu screen on startup using a safe Coroutine to ensure rootVisualElement is ready
        if (_menuScreenDoc != null && _menuScreenDoc.gameObject.activeSelf)
        {
            StartCoroutine(CoActivateStartTransitions());
        }
        // Load the speech-to-text engine (whichever provider is enabled) at start
        StartCoroutine(CoLoadSpeechToText());
    }

    private IEnumerator CoActivateStartTransitions()
    {
        // Wait until rootVisualElement is fully initialized and cloned into the panel
        while (_menuScreenDoc == null || _menuScreenDoc.rootVisualElement == null)
        {
            yield return null;
        }
        ActivateScreenTransitions(_menuScreenDoc.gameObject);
    }

    private void OnEnable()
    {
        SetupEvents();
    }

    private void OnDisable()
    {
        TeardownEvents();
    }

    public void SetupEvents()
    {
        // Menu Screen Buttons & Elements
        if (_menuScreenDoc != null && _menuScreenDoc.rootVisualElement != null)
        {
            var menuRoot = _menuScreenDoc.rootVisualElement;
            BindButtonSFX(menuRoot);

            var startBtn = menuRoot.Q<Button>("StartChatButton");
            if (startBtn != null)
            {
                startBtn.clicked -= OnStartChatClicked; // Prevent double subscription
                startBtn.clicked += OnStartChatClicked;
            }

            var menuBtn = menuRoot.Q<Button>("MenuButton");
            if (menuBtn != null)
            {
                menuBtn.clicked -= OnMenuOpenFromStart;
                menuBtn.clicked += OnMenuOpenFromStart;
            }
        }

        // Chat Screen Buttons & Elements
        if (_chatScreenDoc != null && _chatScreenDoc.rootVisualElement != null)
        {
            var chatRoot = _chatScreenDoc.rootVisualElement;
            BindButtonSFX(chatRoot);

            var menuBtn = chatRoot.Q<Button>("MenuButton");
            if (menuBtn != null)
            {
                menuBtn.clicked -= OnMenuOpenFromChat;
                menuBtn.clicked += OnMenuOpenFromChat;
            }

            var messageInput = chatRoot.Q<TextField>("MessageInput");
            if (messageInput != null)
            {
                messageInput.UnregisterCallback<KeyDownEvent>(OnMessageInputKeyDown);
                messageInput.RegisterCallback<KeyDownEvent>(OnMessageInputKeyDown);
            }
        }

        // Options Screen Buttons & Elements
        if (_optionScreenDoc != null && _optionScreenDoc.rootVisualElement != null)
        {
            var optionRoot = _optionScreenDoc.rootVisualElement;
            BindButtonSFX(optionRoot);

            var backBtn = optionRoot.Q<Button>("BackButton");
            if (backBtn != null)
            {
                backBtn.clicked -= CloseOptions;
                backBtn.clicked += CloseOptions;
            }

            var helpBtn = optionRoot.Q<Button>("HelpButton");
            if (helpBtn != null)
            {
                helpBtn.clicked -= OnHelpClicked;
                helpBtn.clicked += OnHelpClicked;
            }

            var creditsBtn = optionRoot.Q<Button>("CreditsButton");
            if (creditsBtn != null)
            {
                creditsBtn.clicked -= OnCreditsClicked;
                creditsBtn.clicked += OnCreditsClicked;
            }

            var exitBtn = optionRoot.Q<Button>("ExitButton");
            if (exitBtn != null)
            {
                exitBtn.clicked -= OnExitClicked;
                exitBtn.clicked += OnExitClicked;
            }

            var restartBtn = optionRoot.Q<Button>("RestartButton");
            if (restartBtn != null)
            {
                restartBtn.clicked -= OnRestartClicked;
                restartBtn.clicked += OnRestartClicked;
            }

            var volumeSlider = optionRoot.Q<Slider>("VolumeSlider");
            if (volumeSlider != null)
            {
                volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);
                volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
            }

            var notificationsToggle = optionRoot.Q<Toggle>("NotificationsToggle");
            if (notificationsToggle != null)
            {
                notificationsToggle.UnregisterValueChangedCallback(OnNotificationsToggled);
                notificationsToggle.RegisterValueChangedCallback(OnNotificationsToggled);
            }
        }
    }

    public void TeardownEvents()
    {
        // Menu Screen Buttons
        if (_menuScreenDoc != null && _menuScreenDoc.rootVisualElement != null)
        {
            var menuRoot = _menuScreenDoc.rootVisualElement;
            UnbindButtonSFX(menuRoot);

            var startBtn = menuRoot.Q<Button>("StartChatButton");
            if (startBtn != null) startBtn.clicked -= OnStartChatClicked;

            var menuBtn = menuRoot.Q<Button>("MenuButton");
            if (menuBtn != null) menuBtn.clicked -= OnMenuOpenFromStart;
        }

        // Chat Screen Buttons
        if (_chatScreenDoc != null && _chatScreenDoc.rootVisualElement != null)
        {
            var chatRoot = _chatScreenDoc.rootVisualElement;
            UnbindButtonSFX(chatRoot);

            var menuBtn = chatRoot.Q<Button>("MenuButton");
            if (menuBtn != null) menuBtn.clicked -= OnMenuOpenFromChat;

            var messageInput = chatRoot.Q<TextField>("MessageInput");
            if (messageInput != null)
            {
                messageInput.UnregisterCallback<KeyDownEvent>(OnMessageInputKeyDown);
            }
        }

        // Options Screen Buttons
        if (_optionScreenDoc != null && _optionScreenDoc.rootVisualElement != null)
        {
            var optionRoot = _optionScreenDoc.rootVisualElement;
            UnbindButtonSFX(optionRoot);

            var backBtn = optionRoot.Q<Button>("BackButton");
            if (backBtn != null) backBtn.clicked -= CloseOptions;

            var helpBtn = optionRoot.Q<Button>("HelpButton");
            if (helpBtn != null) helpBtn.clicked -= OnHelpClicked;

            var creditsBtn = optionRoot.Q<Button>("CreditsButton");
            if (creditsBtn != null) creditsBtn.clicked -= OnCreditsClicked;

            var exitBtn = optionRoot.Q<Button>("ExitButton");
            if (exitBtn != null) exitBtn.clicked -= OnExitClicked;

            var restartBtn = optionRoot.Q<Button>("RestartButton");
            if (restartBtn != null) restartBtn.clicked -= OnRestartClicked;

            var volumeSlider = optionRoot.Q<Slider>("VolumeSlider");
            if (volumeSlider != null) volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);

            var notificationsToggle = optionRoot.Q<Toggle>("NotificationsToggle");
            if (notificationsToggle != null) notificationsToggle.UnregisterValueChangedCallback(OnNotificationsToggled);
        }
    }

    private GameObject _lastActiveScreenBeforeHide;

    /// <summary>
    /// Shows or hides all UI screens managed by the ScreenManager.
    /// This can be called from external scripts, or via unity messages (e.g. from WebGL JavaScript triggers).
    /// </summary>
    public void SetUIVisibility(bool visible)
    {
        if (!visible)
        {
            // Record currently active screen to restore it later
            if (_menuScreenDoc != null && _menuScreenDoc.gameObject.activeSelf)
                _lastActiveScreenBeforeHide = _menuScreenDoc.gameObject;
            else if (_chatScreenDoc != null && _chatScreenDoc.gameObject.activeSelf)
                _lastActiveScreenBeforeHide = _chatScreenDoc.gameObject;
            else if (_optionScreenDoc != null && _optionScreenDoc.gameObject.activeSelf)
                _lastActiveScreenBeforeHide = _optionScreenDoc.gameObject;

            if (_menuScreenDoc != null) _menuScreenDoc.gameObject.SetActive(false);
            if (_chatScreenDoc != null) _chatScreenDoc.gameObject.SetActive(false);
            if (_optionScreenDoc != null) _optionScreenDoc.gameObject.SetActive(false);
        }
        else
        {
            // Restore last active screen or default to Menu Screen
            GameObject target = _lastActiveScreenBeforeHide != null ? _lastActiveScreenBeforeHide : (_menuScreenDoc != null ? _menuScreenDoc.gameObject : null);
            if (target != null)
            {
                target.SetActive(true);
                ActivateScreenTransitions(target);
            }
        }
    }

    private void BindButtonSFX(VisualElement root)
    {
        if (root == null) return;
        var buttons = root.Query<Button>().Build();
        foreach (var btn in buttons)
        {
            btn.clicked -= OnGenericButtonClicked;
            btn.clicked += OnGenericButtonClicked;
        }
    }

    private void UnbindButtonSFX(VisualElement root)
    {
        if (root == null) return;
        var buttons = root.Query<Button>().Build();
        foreach (var btn in buttons)
        {
            btn.clicked -= OnGenericButtonClicked;
        }
    }

    private void OnGenericButtonClicked()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    private void OnMessageInputKeyDown(KeyDownEvent evt)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayKeyboardType();
        }
    }

    private void OnStartChatClicked()
    {
        TriggerTransition(_menuScreenDoc.gameObject, _chatScreenDoc.gameObject, OnTransitionToChat);
    }

    private void OnMenuOpenFromStart()
    {
        TriggerTransition(_menuScreenDoc.gameObject, _optionScreenDoc.gameObject, OnTransitionToOptions);
    }

    private void OnMenuOpenFromChat()
    {
        TriggerTransition(_chatScreenDoc.gameObject, _optionScreenDoc.gameObject, OnTransitionToOptions);
    }

    private void CloseOptions()
    {
        var targetScreen = _previousScreenGo != null ? _previousScreenGo : _menuScreenDoc.gameObject;
        var transitionEvent = targetScreen == _chatScreenDoc.gameObject ? OnTransitionToChat : OnTransitionToMenu;
        TriggerTransition(_optionScreenDoc.gameObject, targetScreen, transitionEvent);
    }

    private void TriggerTransition(GameObject currentScreen, GameObject targetScreen, UnityEvent transitionEvent)
    {
        if (_currentTransition != null)
        {
            StopCoroutine(_currentTransition);
        }
        _currentTransition = StartCoroutine(CoPerformTransition(currentScreen, targetScreen, transitionEvent));
    }

    private IEnumerator CoPerformTransition(GameObject currentScreen, GameObject targetScreen, UnityEvent transitionEvent)
    {
        bool isOpeningOptions = targetScreen == _optionScreenDoc.gameObject;
        bool isClosingOptions = currentScreen == _optionScreenDoc.gameObject;

        if (isOpeningOptions)
        {
            Debug.Log("[ScreenManager] Slide-in Options Sidebar Panel (keeping previous screen active underneath).");
            _previousScreenGo = currentScreen;

            // Play option-in sound effect
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayOptionIn();
            }

            // 1. Activate Options Sidebar overlay
            targetScreen.SetActive(true);
            ActivateScreenTransitions(targetScreen);

            // 2. Play transition callbacks
            if (transitionEvent != null) transitionEvent.Invoke();

            // 3. Wait for the slide-in transition to complete (0.3s)
            yield return new WaitForSeconds(_transitionDelay);
        }
        else if (isClosingOptions)
        {
            Debug.Log("[ScreenManager] Slide-out Options Sidebar Panel.");

            // Play option-out sound effect
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayOptionOut();
            }

            // 1. Clear Options active class to trigger the slide-out (RTL to original off-screen right)
            var currentUiDoc = currentScreen.GetComponent<UIDocument>();
            if (currentUiDoc != null && currentUiDoc.rootVisualElement != null)
            {
                var root = currentUiDoc.rootVisualElement;
                VisualElement settingsScreen = root.ClassListContains("settings-screen") ? root : root.Q<VisualElement>(className: "settings-screen");
                if (settingsScreen != null) settingsScreen.RemoveFromClassList("settings-screen--active");
            }

            if (transitionEvent != null) transitionEvent.Invoke();

            // 2. Wait for the slide-out transition to complete (0.3s)
            yield return new WaitForSeconds(_transitionDelay);

            // 3. Deactivate Options Screen overlay
            currentScreen.SetActive(false);
        }
        else
        {
            // Standard transitions (e.g. Menu <-> Chat)
            Debug.Log($"[ScreenManager] Standard Transition from {currentScreen.name} to {targetScreen.name}.");

            // Reset transition active classes on the current screen before hiding it to prepare it for next entrance
            var currentUiDoc = currentScreen.GetComponent<UIDocument>();
            if (currentUiDoc != null && currentUiDoc.rootVisualElement != null)
            {
                var root = currentUiDoc.rootVisualElement;
                VisualElement menuScreen = root.ClassListContains("menu-screen") ? root : root.Q<VisualElement>(className: "menu-screen");
                VisualElement vnArea = root.ClassListContains("vn-area") ? root : root.Q<VisualElement>(className: "vn-area");
                VisualElement chatArea = root.ClassListContains("chat-area") ? root : root.Q<VisualElement>(className: "chat-area");
                VisualElement settingsScreen = root.ClassListContains("settings-screen") ? root : root.Q<VisualElement>(className: "settings-screen");

                if (menuScreen != null) menuScreen.RemoveFromClassList("menu-screen--active");
                if (vnArea != null) vnArea.RemoveFromClassList("vn-area--active");
                if (chatArea != null) chatArea.RemoveFromClassList("chat-area--active");
                if (settingsScreen != null) settingsScreen.RemoveFromClassList("settings-screen--active");
            }

            currentScreen.SetActive(false);

            if (transitionEvent != null) transitionEvent.Invoke();

            yield return new WaitForSeconds(_transitionDelay);

            targetScreen.SetActive(true);
            ActivateScreenTransitions(targetScreen);
        }

        // Re-setup callbacks to ensure events bind correctly to active document
        SetupEvents();

        _currentTransition = null;
    }

    private void ActivateScreenTransitions(GameObject targetScreenGo)
    {
        StartCoroutine(CoPlayEntranceTransitions(targetScreenGo));
    }

    private IEnumerator CoPlayEntranceTransitions(GameObject targetScreenGo)
    {
        var uiDoc = targetScreenGo.GetComponent<UIDocument>();
        if (uiDoc == null) yield break;

        // Wait until rootVisualElement is fully initialized
        while (uiDoc.rootVisualElement == null)
        {
            yield return null;
        }

        var root = uiDoc.rootVisualElement;

        // 1. Resolve elements safely (checking both root container and Q descendants)
        VisualElement menuScreen = root.ClassListContains("menu-screen") ? root : root.Q<VisualElement>(className: "menu-screen");
        VisualElement vnArea = root.ClassListContains("vn-area") ? root : root.Q<VisualElement>(className: "vn-area");
        VisualElement chatArea = root.ClassListContains("chat-area") ? root : root.Q<VisualElement>(className: "chat-area");
        VisualElement settingsScreen = root.ClassListContains("settings-screen") ? root : root.Q<VisualElement>(className: "settings-screen");

        // 2. Reset transition active classes to set up base starting state (invisible/off-screen)
        if (menuScreen != null) menuScreen.RemoveFromClassList("menu-screen--active");
        if (vnArea != null) vnArea.RemoveFromClassList("vn-area--active");
        if (chatArea != null) chatArea.RemoveFromClassList("chat-area--active");
        if (settingsScreen != null) settingsScreen.RemoveFromClassList("settings-screen--active");

        // 3. Wait exactly one frame so Unity's UI Toolkit registers the starting state in layout pass
        yield return null;

        // 4. Add the active classes in the next frame to trigger USS transitions smoothly
        if (menuScreen != null) menuScreen.AddToClassList("menu-screen--active");
        if (vnArea != null) vnArea.AddToClassList("vn-area--active");
        if (chatArea != null) chatArea.AddToClassList("chat-area--active");
        if (settingsScreen != null) settingsScreen.AddToClassList("settings-screen--active");

        Debug.Log($"[ScreenManager] Entrance transition played for {targetScreenGo.name}.");
    }

    private void OnVolumeChanged(ChangeEvent<float> evt)
    {
        Debug.Log($"[ScreenManager] Volume level changed: {evt.newValue:F2}");
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMasterVolume(evt.newValue);
        }
        else
        {
            AudioListener.volume = evt.newValue;
        }
    }

    private void OnNotificationsToggled(ChangeEvent<bool> evt)
    {
        Debug.Log($"[ScreenManager] Notifications toggled: {(evt.newValue ? "ON" : "OFF")}");
    }

    private void OnHelpClicked()
    {
        Debug.Log("[ScreenManager] Help & Support button clicked.");
    }

    private void OnCreditsClicked()
    {
        Debug.Log("[ScreenManager] Credits button clicked.");
    }

    private void OnExitClicked()
    {
        Debug.Log("[ScreenManager] Exiting application...");
        if (ApplicationManager.HasInstance)
        {
            ApplicationManager.Instance.CloseApplication();
        }
        else
        {
            Application.Quit();
        }
    }

    private void OnRestartClicked()
    {
        Debug.Log("[ScreenManager] Restart button clicked.");
        if (ApplicationManager.HasInstance)
        {
            ApplicationManager.Instance.RestartApp();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }

    private IEnumerator CoLoadSpeechToText()
    {
        bool hasMenu = _menuScreenDoc != null && _menuScreenDoc.gameObject.activeInHierarchy;

        if (hasMenu)
        {
            // Wait until menu screen root visual element is ready
            while (_menuScreenDoc.rootVisualElement == null)
            {
                yield return null;
            }
        }

        // No provider enabled (symbol off): nothing to load, the record button stays disabled.
        if (!Stt.IsAvailable) yield break;

        var root = hasMenu ? _menuScreenDoc.rootVisualElement : null;
        var startBtn = root?.Q<Button>("StartChatButton");
        var loadingContainer = root?.Q<VisualElement>("LoadingContainer");
        var progressFill = root?.Q<VisualElement>("ProgressBarFill");

        if (startBtn != null) startBtn.style.display = DisplayStyle.None;
        if (loadingContainer != null) loadingContainer.style.display = DisplayStyle.Flex;
        if (progressFill != null) progressFill.style.width = Length.Percent(0f);

        var progress = new Progress01(value =>
        {
            if (progressFill != null) progressFill.style.width = Length.Percent(value * 100f);
        });

        var init = Stt.InitializeAsync(progress);
        while (!init.IsCompleted) yield return null;

        if (init.IsFaulted) Debug.LogError($"[ScreenManager] Speech-to-text failed to initialize: {init.Exception}");
        else if (init.Result)
        {
            yield return new WaitForSeconds(0.25f);
        }

        if (loadingContainer != null) loadingContainer.style.display = DisplayStyle.None;
        if (startBtn != null) startBtn.style.display = DisplayStyle.Flex;
    }

    // Progress<T> posts to the thread pool in some contexts; this one calls back immediately on the caller's thread.
    private sealed class Progress01 : System.IProgress<float>
    {
        private readonly System.Action<float> _onReport;
        public Progress01(System.Action<float> onReport) { _onReport = onReport; }
        public void Report(float value) => _onReport(value);
    }
}
