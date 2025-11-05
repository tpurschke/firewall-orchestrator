using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.IdentityModel.Protocols;

namespace FWO.Config.Api.Data
{
    public sealed record AdfsMetadataSource(Uri MetadataUri, IDocumentRetriever DocumentRetriever, bool RequireHttpsMetadata);

    public static class AdfsMetadataHelper
    {
        public static bool TryCreateMetadataSource(AdfsSettings settings, Uri authorityUri, string baseDirectory, out AdfsMetadataSource? source, out string? errorMessage)
        {
            source = null;
            errorMessage = null;

            string? metadataInput = !string.IsNullOrWhiteSpace(settings.MetadataFileUrl)
                ? settings.MetadataFileUrl
                : settings.MetadataAddress;

            if (string.IsNullOrWhiteSpace(metadataInput))
            {
                errorMessage = "No metadata location configured.";
                return false;
            }

            if (metadataInput.StartsWith("~"))
            {
                metadataInput = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), metadataInput[1..]);
            }

            Uri? metadataUri = CreateMetadataUri(metadataInput, baseDirectory, out string? uriError);
            if (metadataUri == null)
            {
                errorMessage = uriError ?? $"Unable to interpret metadata location \"{metadataInput}\".";
                return false;
            }

            bool requireHttps = ShouldRequireHttpsMetadata(authorityUri, metadataUri);

            FlexibleDocumentRetriever documentRetriever = new(baseDirectory: baseDirectory)
            {
                RequireHttps = requireHttps
            };

            source = new AdfsMetadataSource(metadataUri, documentRetriever, requireHttps);
            return true;
        }

        public static bool ShouldRequireHttpsMetadata(Uri authorityUri, Uri? metadataUri)
        {
            if (!string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (metadataUri is not null &&
                !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static bool TryEnsureHostResolvable(Uri uri, out string? errorMessage)
        {
            errorMessage = null;

            if (!uri.IsAbsoluteUri)
            {
                errorMessage = $"Metadata URI \"{uri}\" must be absolute.";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return true;
            }

            try
            {
                _ = Dns.GetHostEntry(uri.Host);
                return true;
            }
            catch (SocketException exception)
            {
                errorMessage = $"Host resolution failed for \"{uri}\". ({exception.Message})";
                return false;
            }
        }

        private static Uri? CreateMetadataUri(string metadataInput, string baseDirectory, out string? errorMessage)
        {
            errorMessage = null;

            if (Uri.TryCreate(metadataInput, UriKind.Absolute, out Uri? absoluteUri))
            {
                if (absoluteUri.IsFile && !System.IO.File.Exists(absoluteUri.LocalPath))
                {
                    errorMessage = $"Metadata file \"{absoluteUri.LocalPath}\" does not exist.";
                    return null;
                }

                return absoluteUri;
            }

            string resolvedPath = System.IO.Path.IsPathRooted(metadataInput)
                ? metadataInput
                : System.IO.Path.Combine(baseDirectory, metadataInput);

            resolvedPath = System.IO.Path.GetFullPath(resolvedPath);

            if (!System.IO.File.Exists(resolvedPath))
            {
                errorMessage = $"Metadata file \"{resolvedPath}\" does not exist.";
                return null;
            }

            return new Uri(resolvedPath);
        }
    }
}
