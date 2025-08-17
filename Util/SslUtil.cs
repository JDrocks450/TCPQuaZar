using OpenSSL.Crypto;
using OpenSSL.SSL;
using OpenSSL.X509;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace QuazarAPI.Util
{
    /// <summary>
    /// Handles SSL/TLS connections and provides utility methods for SSL streams.
    /// </summary>
    internal static class SslUtil
    {
        static ConcurrentDictionary<uint, SslStream> _streams = new ConcurrentDictionary<uint, SslStream>();
        public static SslStream GetSslStream(uint ID) => _streams[ID];
        /// <summary>
        /// Takes the incoming TcpClient connection and attempts to perform an SSL handshake for the client
        /// </summary>
        /// <param name="ServerCertificate"></param>
        /// <param name="newConnection"></param>
        /// <param name="ID"></param>
        /// <returns></returns>
        public static SslStream SslHandshake(X509Certificate ServerCertificate, TcpClient newConnection, uint ID, X509Chain? ClientCertificates)
        {
            // Check if the connection is already authenticated
            if (_streams.TryGetValue(ID, out SslStream existingStream))
            {
                QConsole.WriteLine(nameof(SslUtil), $"Client {ID} already has an SSL stream.");
                return existingStream;
            }
            QConsole.WriteLine(nameof(SslUtil), $"Client {ID} starting SSL Authentication...");
            // Create a new SslStream for the connection
            SslStream ssl = new SslStream(newConnection.GetStream(), true);

            // attempt to authenticate the SslStream as a server
            ssl.AuthenticateAsServer(ServerCertificate, false, ClientCertificates, SslProtocols.Tls, SslStrength.All, true);

            //display information
            QConsole.WriteLine(nameof(SslUtil), $"Client {ID} SSL Authentication Completed.");
            //QConsole.WriteLine(nameof(SslUtil), $"===SSL INFORMATION===\nSecurity Level:\n{ssl.GetSecurityLevelString()}\nServices:\n{ssl.GetSecurityServicesString()}");

            // Add the new SslStream to the dictionary
            _streams.AddOrUpdate(ID, ssl, (key, oldValue) => ssl);
            return ssl;
        }
        /// <summary>
        /// Attempts to remove the <see cref="SslStream"/> for a given client ID and disposes of it.
        /// </summary>
        /// <param name="ID"></param>
        public static void RemoveSslStream(uint ID)
        {
            if (_streams.TryRemove(ID, out SslStream stream))
            {
                stream.Dispose();
                QConsole.WriteLine(nameof(SslUtil), $"Removed SSL stream for client {ID}.");
            }
            else
            {
                QConsole.WriteLine(nameof(SslUtil), $"No SSL stream found for client {ID}.");
            }
        }

        public static string GetSecurityLevelString(this SslStream stream)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Cipher: {0} version {1}", stream.Ssl.CurrentCipher.Description, stream.Ssl.CurrentCipher.Version));
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
    }
}
