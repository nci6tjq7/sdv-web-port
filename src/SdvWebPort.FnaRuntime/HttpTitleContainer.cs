using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;

namespace Microsoft.Xna.Framework
{
    /// <summary>
    /// HTTP-based replacement for FNA's TitleContainer.OpenStream.
    ///
    /// In WASM, File.OpenRead can't access HTTP-served static files.
    /// This shim fetches Content files via synchronous XMLHttpRequest
    /// (using JS interop) from /deps/Content/.
    ///
    /// IMPORTANT: HttpClient.GetAsync().GetAwaiter().GetResult() deadlocks in
    /// WASM single-threaded environment. We use JS XMLHttpRequest in synchronous
    /// mode instead, which is blocking but doesn't deadlock.
    /// </summary>
    public static partial class HttpTitleContainer
    {
        private static string _baseUrl = "/deps/";

        public static void SetBaseUrl(string url)
        {
            _baseUrl = url;
            Console.WriteLine($"[HttpTitleContainer] Base URL: {_baseUrl}");
        }

        public static string Location
        {
            get { return "Content"; }
        }

        /// <summary>
        /// Open a stream to a Content file via synchronous XMLHttpRequest.
        /// </summary>
        public static Stream OpenStream(string name)
        {
            string safeName = name.Replace('\\', '/');

            string url;
            if (safeName.StartsWith("/"))
            {
                url = safeName;
            }
            else
            {
                url = _baseUrl.TrimEnd('/') + "/" + safeName.TrimStart('/');
            }

            Console.WriteLine($"[HttpTitleContainer] Fetching: {url}");

            try
            {
                // Use JS interop to call synchronous XMLHttpRequest.
                // This is blocking (like File.OpenRead) but doesn't deadlock
                // because XMLHttpRequest runs in the browser's native network stack.
                byte[] data = FetchSync(url);
                if (data == null)
                {
                    throw new FileNotFoundException($"HTTP fetch failed: {url}", url);
                }
                Console.WriteLine($"[HttpTitleContainer] Got {data.Length} bytes for {url}");
                return new MemoryStream(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpTitleContainer] ERROR fetching {url}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// JS interop: fetch a URL synchronously via XMLHttpRequest.
        /// Returns the response as a byte array, or null if the request failed.
        /// </summary>
        [JSImport("globalThis.SDV.fetchSync")]
        private static partial byte[] FetchSync(string url);
    }
}
