using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace Microsoft.Xna.Framework
{
    public static partial class TitleContainer
    {
        public static string Location
        {
            get { return "Content"; }
        }

        public static Stream OpenStream(string name)
        {
            // Log the call stack so we can see WHO is calling OpenStream.
            // This helps diagnose cases where OpenStream is unexpectedly NOT called.
            Console.WriteLine("[TitleContainer] OpenStream: " + name);
            var st = new StackTrace();
            for (int i = 1; i < Math.Min(4, st.FrameCount); i++)
            {
                var f = st.GetFrame(i);
                Console.WriteLine("[TitleContainer]   #" + i + ": " + f.GetMethod().DeclaringType?.FullName + "::" + f.GetMethod().Name);
            }
            string safeName = name.Replace('\\', '/');
            string url = "/deps/" + safeName.TrimStart('/');
            Console.WriteLine("[TitleContainer] Fetching: " + url);
            try
            {
                byte[] data = FetchSync(url);
                Console.WriteLine("[TitleContainer] Got: " + (data == null ? "null" : data.Length + " bytes"));
                if (data == null || data.Length == 0)
                    throw new FileNotFoundException("HTTP fetch failed: " + url, url);
                return new MemoryStream(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TitleContainer] ERROR: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("[TitleContainer] Stack: " + ex.StackTrace);
                throw;
            }
        }

        // ReadToPointer — used by FNA's Audio system (WaveBank, AudioEngine, SoundBank).
        // In WASM we can't return a raw pointer, so we read into a managed buffer
        // and pin it. The caller (FNA) uses this for XACT audio file mapping.
        internal static IntPtr ReadToPointer(string name, out IntPtr size)
        {
            Stream stream = OpenStream(name);
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            // Pin the buffer and return the pointer
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            size = (IntPtr)buffer.Length;
            return handle.AddrOfPinnedObject();
        }

        [JSImport("globalThis.SDV.fetchSync")]
        private static partial byte[] FetchSync(string url);
    }
}
