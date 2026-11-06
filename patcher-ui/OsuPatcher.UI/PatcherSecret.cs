using System;
using System.Security.Cryptography;
using System.Text;

namespace OsuPatcher.UI.Attestation
{
    /// <summary>
    /// Signs the Patcher's requests to the server.
    /// </summary>
    internal static class PatcherSecret
    {
        /// <summary>Returns the signature of <paramref name="payload"/> as lower-case hex.</summary>
        public static string Sign(string payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var key = new byte[32];
            try
            {
                var a = PatcherSecretBlobA.Bytes;
                var b = PatcherSecretBlobB.Bytes;
                for (int i = 0; i < 32; i++)
                    key[i] = (byte)(a[i] ^ b[i]);
                using (var h = new HMACSHA256(key))
                {
                    var mac = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    var hex = new StringBuilder(mac.Length * 2);
                    for (int i = 0; i < mac.Length; i++)
                        hex.Append(mac[i].ToString("x2"));
                    return hex.ToString();
                }
            }
            finally
            {
                for (int i = 0; i < key.Length; i++) key[i] = 0;
            }
        }
    }
}
