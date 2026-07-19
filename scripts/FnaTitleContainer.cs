using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;

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

        /// <summary>
        /// HTTP-based replacement for File.ReadAllText.
        /// Used by SDV's ContentHashParser.ParseFromFile to load ContentHashes.json
        /// (the file manifest). In WASM, File.ReadAllText can't access HTTP-served
        /// files, so we fetch via the same mechanism as OpenStream.
        ///
        /// The path passed in is typically Path.Combine(GetContentRoot(), "ContentHashes.json")
        /// = something like "Content/ContentHashes.json" or "Content\ContentHashes.json".
        /// We normalize backslashes to forward slashes and prepend /deps/.
        /// </summary>
        public static string ReadAllText(string path)
        {
            Console.WriteLine("[TitleContainer] ReadAllText CALLED with path: " + path);
            Console.WriteLine("[TitleContainer] ReadAllText stack trace:");
            var st = new StackTrace();
            for (int i = 1; i < Math.Min(6, st.FrameCount); i++)
            {
                var f = st.GetFrame(i);
                Console.WriteLine("[TitleContainer]   #" + i + ": " + f.GetMethod().DeclaringType?.FullName + "::" + f.GetMethod().Name);
            }
            string safeName = path.Replace('\\', '/');
            // Strip leading "Content/" if present (we prepend /deps/ which already includes Content)
            // Actually, /deps/ serves the wwwroot/deps/ directory which contains Content/.
            // The path "Content/ContentHashes.json" should become "/deps/Content/ContentHashes.json".
            string url = "/deps/" + safeName.TrimStart('/');
            Console.WriteLine("[TitleContainer] ReadAllText fetching: " + url);
            try
            {
                byte[] data = FetchSync(url);
                Console.WriteLine("[TitleContainer] ReadAllText got: " + (data == null ? "null" : data.Length + " bytes"));
                if (data == null || data.Length == 0)
                    throw new FileNotFoundException("HTTP fetch failed: " + url, url);
                string text = Encoding.UTF8.GetString(data);
                Console.WriteLine("[TitleContainer] ReadAllText decoded: " + text.Length + " chars");
                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TitleContainer] ReadAllText ERROR: " + ex.GetType().Name + ": " + ex.Message);
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

        /// <summary>
        /// Returns the preloaded ContentHashes.json manifest as a string.
        /// The manifest is fetched by main.js during init and stored in
        /// globalThis.__manifestJson. This method retrieves it via JS interop.
        ///
        /// Used by the patched ContentHashParser.ParseFromFile (the entire
        /// method body is replaced by the SDV patcher to call this method
        /// instead of File.ReadAllText).
        /// </summary>
        public static string GetManifestJson()
        {
            Console.WriteLine("[TitleContainer] GetManifestJson CALLED");
            string json = GetManifestJsonJS();
            Console.WriteLine("[TitleContainer] GetManifestJson returned: " + (json == null ? "null" : json.Length + " chars"));
            return json;
        }

        [JSImport("globalThis.SDV.getManifestJson")]
        private static partial string GetManifestJsonJS();

        [JSImport("globalThis.SDV.fetchSync")]
        private static partial byte[] FetchSync(string url);
    }
}
