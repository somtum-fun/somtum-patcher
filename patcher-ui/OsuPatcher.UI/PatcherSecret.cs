using System;
using System.Security.Cryptography;
using System.Text;

namespace OsuPatcher.UI.Attestation
{
    /// <summary>
    /// 0.9.4 §A — reconstruct the Patcher attestation secret at the moment of HMAC-compute
    /// and zero it immediately after. The secret itself is split across
    /// <see cref="PatcherSecretBlobA"/> and <see cref="PatcherSecretBlobB"/> (two generated
    /// files, XOR'd at runtime). This single file holds the reconstruct method; Obfuscar
    /// renames it + the two blob classes + the blob fields in the shipped exe, so a
    /// decompile pass has to notice "this method XORs two byte arrays and that result
    /// drives HMACSHA256" instead of grepping for a <c>GetSecret</c> symbol.
    ///
    /// Why this shape rather than a simple embedded byte[]:
    /// - <c>strings patcher-ui.exe</c> never sees the 64-char hex secret because it isn't a
    ///   string, it's two byte arrays. The arrays are uniformly random (fresh mask every
    ///   build) so a frequency-count sweep doesn't flag them either.
    /// - A grepping attacker sees two byte-array initialisers in two different types —
    ///   they still need to find the one method that XORs them, which Obfuscar renames.
    /// - Rate limit (6/min/username, 30/min/ip, anomaly at 5 usernames/ip/hr) is the real
    ///   defense regardless. This raises the extraction cost from "ILSpy, Ctrl-F" to
    ///   "ILSpy, read control flow, understand the shape"; combined with the rate limit
    ///   that stays in place after extraction, the farm economics stay unattractive.
    /// </summary>
    internal static class PatcherSecret
    {
        /// <summary>
        /// Computes HMAC-SHA256 over <paramref name="payload"/> with the reconstructed
        /// secret and returns the digest as lower-case hex. Reconstruction happens in a
        /// local byte[] that we zero before returning so the full secret only lives in
        /// managed heap for the duration of one HMAC compute.
        /// </summary>
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
                // Clear the reconstructed secret from managed heap. CLR may still have
                // copies in HMAC's internal state (unavoidable without P/Invoking an
                // unmanaged HMAC), but this closes the obvious window.
                for (int i = 0; i < key.Length; i++) key[i] = 0;
            }
        }
    }
}
