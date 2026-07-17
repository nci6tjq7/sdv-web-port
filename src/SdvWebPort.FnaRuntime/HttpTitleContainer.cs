using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework
{
    /// <summary>
    /// HTTP-based replacement for FNA's TitleContainer.OpenStream.
    ///
    /// In WASM, File.OpenRead can't access HTTP-served static files.
    /// This shim fetches Content files via HTTP from /deps/Content/.
    ///
    /// FNA's ContentManager calls TitleContainer.OpenStream(assetName) where
    /// assetName is like "Data\BigCraftables" (no .xnb extension, with backslashes).
    /// ContentManager prepends RootDirectory ("Content") before calling OpenStream.
    ///
    /// So OpenStream receives: "Content\Data\BigCraftables.xnb"
    /// We convert to: "/deps/Content/Data/BigCraftables.xnb" and fetch via HTTP.
    /// </summary>
    public static class HttpTitleContainer
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string _baseUrl = "/deps/";

        /// <summary>
        /// Set the base URL where Content files are served.
        /// Default: /deps/ (files at wwwroot/deps/Content/)
        /// </summary>
        public static void SetBaseUrl(string url)
        {
            _baseUrl = url;
            Console.WriteLine($"[HttpTitleContainer] Base URL: {_baseUrl}");
        }

        /// <summary>
        /// The root directory for content files.
        /// SDV's LocalizedContentManager.GetContentRoot() reads this property.
        /// In FNA, TitleContainer doesn't have a Location property (it's MonoGame),
        /// but SDV expects it. We return "Content" to match SDV's expectation.
        /// </summary>
        public static string Location
        {
            get { return "Content"; }
        }

        /// <summary>
        /// Open a stream to a Content file via HTTP fetch.
        /// This is a synchronous wrapper around HttpClient.GetAsync.
        /// </summary>
        public static Stream OpenStream(string name)
        {
            // Normalize path separators (\ → /)
            string safeName = name.Replace('\\', '/');

            // Build the URL
            string url;
            if (safeName.StartsWith("/"))
            {
                // Already an absolute path
                url = safeName;
            }
            else
            {
                url = _baseUrl.TrimEnd('/') + "/" + safeName.TrimStart('/');
            }

            Console.WriteLine($"[HttpTitleContainer] Fetching: {url}");

            try
            {
                // Synchronous HTTP fetch (blocking — OK for WASM single-threaded)
                var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new FileNotFoundException(
                        $"HTTP {response.StatusCode}: {url}", url
                    );
                }

                // Read the content into a MemoryStream
                var stream = new MemoryStream();
                response.Content.ReadAsStreamAsync().GetAwaiter().GetResult().CopyTo(stream);
                stream.Position = 0;
                return stream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpTitleContainer] ERROR fetching {url}: {ex.Message}");
                throw;
            }
        }
    }
}
