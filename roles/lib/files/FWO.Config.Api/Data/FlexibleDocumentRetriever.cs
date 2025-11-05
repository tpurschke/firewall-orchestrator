using System;
using System.Net.Http;
using Microsoft.IdentityModel.Protocols;

namespace FWO.Config.Api.Data
{
    public class FlexibleDocumentRetriever : IDocumentRetriever
    {
        private readonly HttpClient httpClient;
        private readonly string baseDirectory;

        public bool RequireHttps { get; set; } = true;

        public FlexibleDocumentRetriever(HttpClient? httpClient = null, string? baseDirectory = null)
        {
            this.httpClient = httpClient ?? new HttpClient();
            this.baseDirectory = baseDirectory ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
        }

        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
            {
                string resolvedPath = ResolveFilePath(address);
                return await System.IO.File.ReadAllTextAsync(resolvedPath, cancel);
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                if (RequireHttps && uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("HTTPS is required when retrieving the ADFS metadata.");
                }

                using HttpResponseMessage response = await httpClient.GetAsync(uri, cancel);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancel);
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                if (string.IsNullOrEmpty(uri.LocalPath))
                {
                    throw new InvalidOperationException("The metadata file URI does not contain a local path.");
                }

                return await System.IO.File.ReadAllTextAsync(uri.LocalPath, cancel);
            }

            throw new InvalidOperationException($"Unsupported metadata URI scheme \"{uri.Scheme}\".");
        }

        private string ResolveFilePath(string path)
        {
            string resolvedPath = System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.Combine(baseDirectory, path);

            resolvedPath = System.IO.Path.GetFullPath(resolvedPath);

            if (!System.IO.File.Exists(resolvedPath))
            {
                throw new System.IO.FileNotFoundException($"Metadata file \"{resolvedPath}\" does not exist.", resolvedPath);
            }

            return resolvedPath;
        }
    }
}
