using System;
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
            string safeName = name.Replace('\\', '/');
            string url = "/deps/" + safeName.TrimStart('/');
            try
            {
                byte[] data = FetchSync(url);
                if (data == null || data.Length == 0)
                    throw new FileNotFoundException("HTTP fetch failed: " + url, url);
                return new MemoryStream(data);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[TC] ERROR " + name + ": " + ex.Message);
                throw;
            }
        }

        public static string ReadAllText(string path)
        {
            string safeName = path.Replace('\\', '/');
            string url = "/deps/" + safeName.TrimStart('/');
            byte[] data = FetchSync(url);
            if (data == null || data.Length == 0)
                throw new FileNotFoundException("HTTP fetch failed: " + url, url);
            return Encoding.UTF8.GetString(data);
        }

        internal static IntPtr ReadToPointer(string name, out IntPtr size)
        {
            Stream stream = OpenStream(name);
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            size = (IntPtr)buffer.Length;
            return handle.AddrOfPinnedObject();
        }

        public static string GetManifestJson()
        {
            return GetManifestJsonJS();
        }

        [JSImport("globalThis.SDV.getManifestJson")]
        private static partial string GetManifestJsonJS();

        [JSImport("globalThis.SDV.fetchSync")]
        private static partial byte[] FetchSync(string url);
    }
}
