using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>Default speech to text when no provider symbol is enabled. Does nothing.</summary>
    public sealed class NullSpeechToText : ISpeechToText
    {
        public static readonly NullSpeechToText Instance = new NullSpeechToText();

        public bool IsAvailable => false;
        public bool IsReady => false;
        public bool IsRecording => false;
#pragma warning disable CS0067 // never raised by the null provider
        public event Action<string> TranscriptionFailed;
#pragma warning restore CS0067

        public Task<bool> InitializeAsync(IProgress<float> progress = null) => Task.FromResult(false);
        public void StartRecording() { }
        public Task<string> StopAndTranscribeAsync() => Task.FromResult<string>(null);
    }

    /// <summary>Default text to speech when no provider symbol is enabled. Does nothing.</summary>
    public sealed class NullTextToSpeech : ITextToSpeech
    {
        public static readonly NullTextToSpeech Instance = new NullTextToSpeech();

        public bool IsAvailable => false;
        public Task<AudioClip> TextToSpeech(string text, string voiceId = null) => Task.FromResult<AudioClip>(null);
    }
}
