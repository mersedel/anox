using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ANOX.Server;

/// <summary>Loads the server's TLS certificate from the PEM files named in server.json.</summary>
public static class TlsCertificate
{
    /// <param name="configPath">Only used in error messages, so the admin knows which file to edit.</param>
    /// <exception cref="InvalidDataException">TLS is not configured or the files can't be used; the message says why.</exception>
    public static SslStreamCertificateContext Load(ServerConfig config, string configPath)
    {
        if (string.IsNullOrWhiteSpace(config.TlsCertificatePath) || string.IsNullOrWhiteSpace(config.TlsKeyPath))
        {
            throw new InvalidDataException(
                $"TLS is not configured. Set {nameof(ServerConfig.TlsCertificatePath)} (PEM chain, e.g. fullchain.pem) " +
                $"and {nameof(ServerConfig.TlsKeyPath)} (PEM private key, e.g. privkey.pem) in {configPath}");
        }

        string certificatePath = config.ResolvePath(config.TlsCertificatePath);
        string keyPath = config.ResolvePath(config.TlsKeyPath);

        try
        {
            // Takes the first certificate in the file as the server's own and fails if the key doesn't belong to it.
            X509Certificate2 certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

            // A key loaded from PEM is ephemeral (memory only), and on Windows SChannel can't use such a key
            // on the server side: the handshake just fails. Round-tripping through PKCS#12 gives it a real key
            // container. Linux (OpenSSL) works with the PEM key as is.
            if (OperatingSystem.IsWindows())
            {
                using X509Certificate2 ephemeral = certificate;
                certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
            }

            // Everything after the first certificate is the intermediate chain. Clients only trust the root,
            // so the server must send the intermediates itself, and it only does that when given a context with them.
            X509Certificate2Collection chain = new X509Certificate2Collection();
            chain.ImportFromPemFile(certificatePath);
            chain.RemoveAt(0);

            // offline: build the chain from these files only, without downloading anything at startup.
            return SslStreamCertificateContext.Create(certificate, chain, offline: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new InvalidDataException(
                $"Can't load the TLS certificate ({certificatePath}) or key ({keyPath}): {e.Message} " +
                $"Check the paths in {configPath}, that the key belongs to this certificate, and that the server's user can read both files.");
        }
    }
}
