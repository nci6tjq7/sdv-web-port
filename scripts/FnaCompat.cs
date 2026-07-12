using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Input;

namespace Microsoft.Xna.Framework.Audio
{
    public class CueDefinition
    {
        public string name;
        public bool limitInstances;
        public int instanceLimit;
        public float[] limitBehaviors;
        public List<object> sounds = new List<object>();
        public int[] weightedSounds;
        public int totalSoundWeights;
        public float dbVolume;
        public Action OnModified;
        public void SetSound(byte[] data, int ci, bool l, bool r) { }
        public void SetSound(Array data, int ci, bool l, bool r) { }
        public void SetSound(object data, int ci, bool l, bool r) { }
    }
    
    public class OggStreamSoundEffect { public OggStreamSoundEffect(string fp) { } }
    
    public static class FnaAudioExtensions
    {
        public static int GetCategoryIndex(this AudioEngine e, string n) => 0;
        public static float[] GetReverbSettings(this AudioEngine e) => new float[32];
        public static void AddCue(this SoundBank b, CueDefinition d) { }
        public static bool Exists(this SoundBank b, string n) => false;
        public static CueDefinition GetCueDefinition(this SoundBank b, string n) => new CueDefinition();
        public static SoundEffect FromStream(Stream stream, bool looped) => SoundEffect.FromStream(stream);
    }
    
    public static class FnaCueExtensions
    {
        public static float get_Volume(this Cue c) => 0f;
        public static void set_Volume(this Cue c, float v) { }
        public static float get_Pitch(this Cue c) => 0f;
        public static void set_Pitch(this Cue c, float v) { }
        public static bool get_IsPitchBeingControlledByRPC(this Cue c) => false;
    }
}

namespace Microsoft.Xna.Framework
{
    public class TextInputEventArgs : EventArgs
    {
        public char Character { get; private set; }
        public Keys Key { get; private set; }
        public TextInputEventArgs(char c, Keys k) { Character = c; Key = k; }
    }
    
    public static class FnaGameWindowExtensions
    {
        public static bool CenterOnDisplay(this GameWindow w, int i) => true;
        public static Rectangle GetDisplayBounds(this GameWindow w, int i) => new Rectangle(0,0,1280,720);
        public static int GetDisplayIndex(this GameWindow w) => 0;
    }
}

namespace Microsoft.Xna.Framework.Graphics
{
    public static class FnaGraphicsExtensions
    {
        public static void Begin(this SpriteBatch sb, SpriteSortMode s, BlendState b, SamplerState ss)
        {
            sb.Begin(s, b, ss, null, null, null, Matrix.Identity);
        }
        public static int get_ActualWidth(this Texture2D t) => t.Width;
        public static int get_ActualHeight(this Texture2D t) => t.Height;
        public static void SetImageSize(this Texture2D t, int w, int h) { }
    }
}

namespace xTile.ObjectModel
{
    public static class FnaPropertyValueExtensions
    {
        public static bool StartsWith(this PropertyValue v, string s) => v.ToString().StartsWith(s);
        public static bool Contains(this PropertyValue v, string s) => v.ToString().Contains(s);
        public static bool Contains(this PropertyValue v, string s, StringComparison sc) => v.ToString().Contains(s, sc);
        public static int get_Length(this PropertyValue v) => v.ToString().Length;
    }
}

namespace Microsoft.Xna.Framework.Graphics
{
    public static class FnaGraphicsDeviceManagerExtensions
    {
        public static bool get_HardwareModeSwitch(this GraphicsDeviceManager gdm) => false;
        public static void set_HardwareModeSwitch(this GraphicsDeviceManager gdm, bool value) { }
        public static bool HardwareModeSwitch
        {
            get => false;
            set { }
        }
    }
}

namespace Microsoft.Xna.Framework
{
    public static class FnaGameWindowTextInputExtensions
    {
        // Stub for GameWindow.TextInput event (MG extension)
        public static void add_TextInput(this GameWindow gw, EventHandler<TextInputEventArgs> handler) { }
        public static void remove_TextInput(this GameWindow gw, EventHandler<TextInputEventArgs> handler) { }
    }
}
