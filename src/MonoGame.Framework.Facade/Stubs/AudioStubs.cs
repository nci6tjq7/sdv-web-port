using System;

namespace Microsoft.Xna.Framework.Audio
{
    // Stub types for XACT audio features not available in KNI's Blazor.GL platform.
    // These types exist in the .dll version of Xna.Framework.Audio but are stripped
    // from the .wasm version (browser doesn't support XACT audio).
    // SDV references these types in field declarations; the stubs allow type loading
    // to succeed. Actual audio functionality is not available in browser.

    public class WaveBank : IDisposable
    {
        public WaveBank() { }
        public WaveBank(AudioEngine audioEngine, string nonStreamingWaveBankFilename) { }
        public void Dispose() { }
    }

    public class AudioEngine : IDisposable
    {
        public AudioEngine(string settingsFile) { }
        public void Dispose() { }
        public AudioCategory GetCategory(string name) => new AudioCategory();
        public void Update() { }
    }

    public class Cue : IDisposable
    {
        public void Play() { }
        public void Pause() { }
        public void Resume() { }
        public void Stop(AudioStopOptions options) { }
        public void Dispose() { }
        public bool IsPlaying => false;
        public bool IsPaused => false;
        public bool IsStopped => true;
    }

    public class SoundBank : IDisposable
    {
        public SoundBank(AudioEngine engine, string fileName) { }
        public Cue GetCue(string name) => new Cue();
        public void Dispose() { }
    }

    public class AudioCategory
    {
        public string Name => "";
        public void SetVolume(float volume) { }
        public void Pause() { }
        public void Resume() { }
        public void Stop(AudioStopOptions options) { }
    }

    public class InstancePlayLimitException : Exception
    {
        public InstancePlayLimitException() : base() { }
        public InstancePlayLimitException(string message) : base(message) { }
    }

    public class NoAudioHardwareException : Exception
    {
        public NoAudioHardwareException() : base() { }
        public NoAudioHardwareException(string message) : base(message) { }
    }

    public class NoMicrophoneConnectedException : Exception
    {
        public NoMicrophoneConnectedException() : base() { }
        public NoMicrophoneConnectedException(string message) : base(message) { }
    }

    public class Microphone
    {
        public static Microphone Default => new Microphone();
        public string Name => "";
        public MicrophoneState State => MicrophoneState.Stopped;
        public int BufferDuration => 0;
        public void Start() { }
        public void Stop() { }
        public int GetData(byte[] buffer) => 0;
    }

    public enum MicrophoneState
    {
        Started,
        Stopped
    }
}
