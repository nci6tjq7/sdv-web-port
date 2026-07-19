using System;
using System.IO;
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
            Console.WriteLine("[TitleContainer] OpenStream: " + name);
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
                Console.WriteLine("[TitleContainer] ERROR: " + ex.Message);
                throw;
            }
        }

        [JSImport("globalThis.SDV.fetchSync")]
        private static partial byte[] FetchSync(string url);
    }
}
