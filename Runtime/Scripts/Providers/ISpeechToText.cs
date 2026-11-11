using System;
using System.Threading.Tasks;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Speech to text. Consumers depend on this interface only; the engine (Whisper, ...) lives in its own
    /// provider assembly that is compiled in by a scripting symbol.
    /// </summary>
    public interface ISpeechToText
    {
        /// <summary>A real engine is present. False for the null provider.</summary>
        bool IsAvailable { get; }

        /// <summary>The engine can transcribe now (model loaded).</summary>
        bool IsReady { get; }

        bool IsRecording { get; }

        /// <summary>Raised with a reason when a transcription fails.</summary>
        event Action<string> TranscriptionFailed;

        /// <summary>Loads the engine. Returns false and raises <see cref="TranscriptionFailed"/> on failure. Progress is 0..1.</summary>
        Task<bool> InitializeAsync(IProgress<float> progress = null);

        void StartRecording();

        /// <summary>Stops the microphone and transcribes. Returns null when it fails.</summary>
        Task<string> StopAndTranscribeAsync();
    }
}
