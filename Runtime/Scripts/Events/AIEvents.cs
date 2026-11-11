namespace Conkist.GDK.AI
{
    /// <summary>
    /// Event triggered when an AI agent produces a chat message response.
    /// </summary>
    public struct AgentMessageEvent
    {
        public string AgentName;
        public string Message;

        public AgentMessageEvent(string agentName, string message)
        {
            AgentName = agentName;
            Message = message;
        }
    }

    /// <summary>
    /// Event triggered when local Whisper speech-to-text transcription completes.
    /// </summary>
    public struct AgentTranscriptionEvent
    {
        public string TranscribedText;

        public AgentTranscriptionEvent(string transcribedText)
        {
            TranscribedText = transcribedText;
        }
    }
}
