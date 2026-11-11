using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Events;
using Conkist.GDK.AI;
using Conkist.GDK.AI.Providers;
using VContainer;

public class ChatUIController : MonoBehaviour
{
    public UnityEvent OnStartRecording;
    public UnityEvent OnStopRecording;
    public UnityEvent<string> OnMessageSubmitted;

    [Header("Chat Agent Connection")]
    [SerializeField] private AgentWrapper _chatAgent;

    // Speech to text comes from whichever provider is enabled (injected by AILifetimeScope).
    [Inject] private ISpeechToText _speechToText;
    private ISpeechToText Stt => _speechToText ?? NullSpeechToText.Instance;

    private UIDocument _uiDocument;
    private ScrollView _messageLog;
    private TextField _messageInput;
    private Button _sendButton;
    private Button _recordButton;
    private Button _sendRecordButton;

    private bool _isRecording = false;

    private void OnEnable()
    {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null) return;

        var root = _uiDocument.rootVisualElement;

        _messageLog = root.Q<ScrollView>("BubbleMessageLog");
        _typingIndicatorBubble = root.Q<VisualElement>("TypingIndicator");
        _messageInput = root.Q<TextField>("MessageInput");
        _sendButton = root.Q<Button>("SendButton");
        _recordButton = root.Q<Button>("RecordButton");
        _sendRecordButton = root.Q<Button>("SendRecordButton");

        if (_sendButton != null) _sendButton.clicked += OnSendButtonClicked;
        if (_recordButton != null) _recordButton.clicked += OnRecordButtonClicked;
        if (_sendRecordButton != null) _sendRecordButton.clicked += OnSendRecordButtonClicked;

        if (_messageInput != null)
        {
            _messageInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _messageInput.RegisterValueChangedCallback(OnInputTextChanged);
        }

        if (_chatAgent != null)
        {
            OnMessageSubmitted.AddListener(_chatAgent.ForceMessageToAgent);
            _chatAgent.OnReceiveMessage.AddListener(ReceiveMessage);
            _chatAgent.OnTypingIndicatorChanged.AddListener(ShowTypingIndicator);
            _chatAgent.OnAllActionsCompleted.AddListener(OnCharacterActionsCompleted);
            ShowTypingIndicator(_chatAgent.IsTyping);
        }

        Stt.TranscriptionFailed += OnTranscriptionFailed;

        UpdateActionButtons();
    }

    private void OnDisable()
    {
        if (_sendButton != null) _sendButton.clicked -= OnSendButtonClicked;
        if (_recordButton != null) _recordButton.clicked -= OnRecordButtonClicked;
        if (_sendRecordButton != null) _sendRecordButton.clicked -= OnSendRecordButtonClicked;

        if (_messageInput != null)
        {
            _messageInput.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _messageInput.UnregisterValueChangedCallback(OnInputTextChanged);
        }

        if (_chatAgent != null)
        {
            OnMessageSubmitted.RemoveListener(_chatAgent.ForceMessageToAgent);
            _chatAgent.OnReceiveMessage.RemoveListener(ReceiveMessage);
            _chatAgent.OnTypingIndicatorChanged.RemoveListener(ShowTypingIndicator);
            _chatAgent.OnAllActionsCompleted.RemoveListener(OnCharacterActionsCompleted);
        }

        Stt.TranscriptionFailed -= OnTranscriptionFailed;
    }

    private void OnInputTextChanged(ChangeEvent<string> evt)
    {
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        if (_isRecording) return;

        bool hasText = _messageInput != null && !string.IsNullOrWhiteSpace(_messageInput.value);

        if (_sendButton != null) 
            _sendButton.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
            
        if (_recordButton != null) 
        {
            bool recordAllowed = SmartMascotsSettings.Current?.speechToText.showRecordButton ?? true;
            _recordButton.style.display = hasText || !recordAllowed ? DisplayStyle.None : DisplayStyle.Flex;

            _recordButton.SetEnabled(Stt.IsReady);
        }

        if (_sendRecordButton != null)
            _sendRecordButton.style.display = DisplayStyle.None;
    }

    private void OnSendButtonClicked()
    {
        SubmitMessage();
    }

    private void OnRecordButtonClicked()
    {
        _isRecording = true;

        if (_recordButton != null) _recordButton.style.display = DisplayStyle.None;
        if (_sendButton != null) _sendButton.style.display = DisplayStyle.None;
        if (_sendRecordButton != null) _sendRecordButton.style.display = DisplayStyle.Flex;

        if (_messageInput != null) _messageInput.SetEnabled(false);

        Stt.StartRecording();
        OnStartRecording?.Invoke();
        Debug.Log("STT: Recording started.");
    }

    private async void OnSendRecordButtonClicked()
    {
        _isRecording = false;
        OnStopRecording?.Invoke();

        Debug.Log("STT: Recording stopped, processing audio...");

        UpdateActionButtons();
        SetInputEnabled(false);

        string text = await Stt.StopAndTranscribeAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            SubmitMessage(text);
        }
        else
        {
            // A failure already went through OnTranscriptionFailed; this covers silence.
            SetInputEnabled(true);
        }
    }

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            if (evt.shiftKey)
            {
                // Shift pressionado: evita o envio e permite quebrar a linha nativamente
                return;
            }
            else
            {
                // Apenas Enter pressionado: previne a quebra de linha e envia a mensagem
                evt.StopPropagation();
                evt.PreventDefault();
                
                // Agendar para o próximo tick para evitar ArgumentOutOfRangeException
                // quando o UIElements tenta processar a inserção enquanto o texto foi limpo.
                _messageInput.schedule.Execute(() => SubmitMessage());
            }
        }
    }

    public void SubmitMessage(string text = "")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (_messageInput == null || string.IsNullOrWhiteSpace(_messageInput.value)) 
            {
                SetInputEnabled(true);
                if (_messageInput != null) _messageInput.Focus();
                return;
            }

            text = _messageInput.value;
            
            // Clear input
            _messageInput.SetValueWithoutNotify(string.Empty);
        }

        text = text.Trim();

        SetInputEnabled(false);

        // Add sent message
        AddMessage(text, true);

        // Invoke the UnityEvent for other scripts to listen
        OnMessageSubmitted?.Invoke(text);

        // Clear input
        _messageInput.SetValueWithoutNotify(string.Empty);

        UpdateActionButtons();
    }

    private void SetInputEnabled(bool enabled)
    {
        if (_messageInput != null) _messageInput.SetEnabled(enabled);

        bool sttReady = Stt.IsReady;

        if (_recordButton != null) _recordButton.SetEnabled(enabled && sttReady);
        if (_sendButton != null) _sendButton.SetEnabled(enabled);
        if (_sendRecordButton != null) _sendRecordButton.SetEnabled(enabled && sttReady);
    }

    private void OnTranscriptionFailed(string error)
    {
        Debug.LogWarning($"STT: Transcription failed: {error}");
        SetInputEnabled(true);
        if (_messageInput != null) _messageInput.Focus();
    }

    private void OnCharacterActionsCompleted()
    {
        SetInputEnabled(true);
        if (_messageInput != null) _messageInput.Focus();
    }

    private VisualElement _typingIndicatorBubble;

    public void ShowTypingIndicator(bool show)
    {
        Debug.Log("ChatUIController::ShowTypingIndicator(" + show + ")");
        if (_typingIndicatorBubble == null) return;

        if (show)
        {
            _typingIndicatorBubble.style.display = DisplayStyle.Flex;
            _typingIndicatorBubble.BringToFront();
            if (_messageLog != null)
            {
                _messageLog.schedule.Execute(() =>
                {
                    _messageLog.ScrollTo(_typingIndicatorBubble);
                });
            }
        }
        else
        {
            _typingIndicatorBubble.style.display = DisplayStyle.None;
        }
    }

    /// <summary>
    /// Call this method when a message is received from another actor (e.g., AI/Whisper).
    /// </summary>
    /// <param name="text">The message content.</param>
    public void ReceiveMessage(string text)
    {
        AddMessage(text, false);
    }

    public void AddMessage(string text)
    {
        AddMessage(text, true);
    }

    private void AddMessage(string text, bool isSent)
    {
        if (_messageLog == null) return;

        // Create the bubble container
        var bubbleContainer = new VisualElement();
        bubbleContainer.AddToClassList("message-bubble");
        bubbleContainer.AddToClassList(isSent ? "sent" : "received");

        // Create the message text label
        var messageLabel = new Label(text);
        messageLabel.AddToClassList("message-text");

        // Assemble and add to log
        bubbleContainer.Add(messageLabel);
        _messageLog.Add(bubbleContainer);

        // Scroll to the new bubble after a brief delay to allow layout update
        _messageLog.schedule.Execute(() =>
        {
            _messageLog.ScrollTo(bubbleContainer);
        });
    }
}
