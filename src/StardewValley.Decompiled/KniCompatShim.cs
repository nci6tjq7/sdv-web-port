// KNI Compatibility Shim — provides types that exist in MonoGame.Framework
// but are missing from KNI's Xna.Framework.Audio.
// This allows the decompiled SDV source to compile without modification.

using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Audio
{
    /// <summary>
    /// Stub for MonoGame's CueDefinition class (not in KNI).
    /// SDV uses this for audio cue management.
    /// </summary>
    public class CueDefinition
    {
        public string name;
        public int[] category;
        public bool limitInstances;
        public int instanceLimit;
        public float[] limitBehaviors;
        public List<XactSound> sounds;
        public int[] weightedSounds;
        public int totalSoundWeights;
        public float dbVolume;
        public float? pitch;
        public float? volume;

        public CueDefinition()
        {
            sounds = new List<XactSound>();
        }

        public void SetSound(byte[] data, int soundCount, int[] weightedSounds, int totalSoundWeights)
        {
            // Stub — no audio in WASM
        }

        public void SetSound(byte[] data, int categoryIndex, bool looped, bool useReverb)
        {
            // Stub — no audio in WASM
        }

        public Action OnModified;
    }

    /// <summary>
    /// Stub for XactSound (used by CueDefinition)
    /// </summary>
    public class XactSound
    {
        public byte[] data;
        public bool looped;
        public float volume;
    }

    /// <summary>
    /// Stub for NoAudioHardwareException
    /// </summary>
    public class NoAudioHardwareException : Exception
    {
        public NoAudioHardwareException() : base("No audio hardware available") { }
        public NoAudioHardwareException(string message) : base(message) { }
    }
}
