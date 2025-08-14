using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace QuazarAPI.Util
{
    internal static class SslUtil
    {
        public static bool SslHandshake(X509Certificate2 Certificate, TcpClient newConnection, uint ID, out Exception FailureReason)
        {            
            FailureReason = null;
            using SslStream ssl = new SslStream(newConnection.GetStream(), true);
            try
            {
                ssl.AuthenticateAsServer(new SslServerAuthenticationOptions()
                {
                    AllowRenegotiation = true,
                    AllowTlsResume = true,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Ssl2 | SslProtocols.Ssl3| SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13,
                    ServerCertificate = Certificate,                                        
                });

                //display information
                QConsole.WriteLine(nameof(SslUtil), $"Client {ID} SSL Authenticated: {ssl.IsAuthenticated} with {ssl.CipherAlgorithm} ({ssl.CipherStrength} bits) and {ssl.HashAlgorithm} ({ssl.HashStrength} bits)");
                QConsole.WriteLine(nameof(SslUtil), $"===SSL INFORMATION===\nSecurity Level:\n{ssl.GetSecurityLevelString()}\nServices:\n{ssl.GetSecurityServicesString()}");
            }
            catch (Exception authEx)
            {
                FailureReason = authEx;
                QConsole.WriteLine(nameof(SslUtil), $"SSL Handshake failed for Client {ID}: {authEx.Message}");
                return false;
            }
            return true;
        }
        public static string GetSecurityLevelString(this SslStream stream)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Cipher: {0} strength {1}", stream.CipherAlgorithm, stream.CipherStrength));
            sb.AppendLine(string.Format("Hash: {0} strength {1}", stream.HashAlgorithm, stream.HashStrength));
            sb.AppendLine(string.Format("Key exchange: {0} strength {1}", stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength));
            sb.AppendLine(string.Format("Protocol: {0}", stream.SslProtocol));
            return sb.ToString();
        }
        public static string GetSecurityServicesString(this SslStream stream)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Is authenticated: {0} as server? {1}", stream.IsAuthenticated, stream.IsServer));
            sb.AppendLine(string.Format("IsSigned: {0}", stream.IsSigned));
            sb.AppendLine(string.Format("Is Encrypted: {0}", stream.IsEncrypted));
            sb.AppendLine(string.Format("Is mutually authenticated: {0}", stream.IsMutuallyAuthenticated));
            return sb.ToString();
        }
        public static string GetCertificateInformationString(this SslStream stream)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Certificate revocation list checked: {0}", stream.CheckCertRevocationStatus));

            X509Certificate localCertificate = stream.LocalCertificate;
            if (stream.LocalCertificate != null)
            {
                sb.AppendLine(string.Format("Local cert was issued to {0} and is valid from {1} until {2}.",
                    localCertificate.Subject,
                    localCertificate.GetEffectiveDateString(),
                    localCertificate.GetExpirationDateString()));
            }
            else
            {
                sb.AppendLine(string.Format("Local certificate is null."));
            }
            // Display the properties of the client's certificate.
            X509Certificate remoteCertificate = stream.RemoteCertificate;
            if (stream.RemoteCertificate != null)
            {
                sb.AppendLine(string.Format("Remote cert was issued to {0} and is valid from {1} until {2}.",
                    remoteCertificate.Subject,
                    remoteCertificate.GetEffectiveDateString(),
                    remoteCertificate.GetExpirationDateString()));
            }
            else
            {
                sb.AppendLine(string.Format("Remote certificate is null."));
            }
            return sb.ToString();
        }
    }
}
