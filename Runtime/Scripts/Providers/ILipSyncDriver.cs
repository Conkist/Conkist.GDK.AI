namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Mouth driver for one avatar. A component on the avatar implements it, so it is found with
    /// GetComponentInChildren and is not injected: there is one per character.
    /// </summary>
    public interface ILipSyncDriver
    {
        bool IsAvailable { get; }

        /// <summary>True: the driver follows live audio. False: the caller drives it with <see cref="ShowViseme"/>.</summary>
        void SetAudioDriven(bool enabled);

        /// <summary>Shows one phoneme (a, i, u, e, o, or "-" for silence).</summary>
        void ShowViseme(string phoneme);

        void Silence();
    }
}
