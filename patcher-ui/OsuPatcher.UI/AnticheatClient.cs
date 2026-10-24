using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OsuPatcher.UI.Attestation;

namespace OsuPatcher.UI
{
    /// <summary>
    /// Fetches a one-shot launch token from the server before <see cref="InjectorCore.LaunchOsu"/>
    /// starts osu!. pkms (loaded inside osu!.exe) reads <c>PKMS_LAUNCH_TOKEN</c> from the child env
    /// on first <c>/anticheat/key</c> and the server consumes it there — a replay or a
    /// server-signed-but-missing ticket fails. See <c>docs/0.9.0-roadmap.md §1</c> in
    /// pad-krapow-mu-sap-injector.
    /// </summary>
    /// <remarks>
    /// We fail CLOSED on a reachable-but-angry server (401/403/5xx) because that's the exact shape
    /// an attacker's man-in-the-middle produces; we fail OPEN on no response (timeout, DNS, offline)
    /// so a transient outage doesn't lock legitimate players out. The server-side feature flag
    /// <c>plugins:tomyum.handshake-flag:RequireLaunchToken</c> stays off until the whole fleet is on
    /// a pkms that forwards the token — this class is additive during rollout.
    /// </remarks>
    internal static class AnticheatClient
    {
#if SOMTUM_DEV
        private const string Host = "osu.blueskychan.dev";
#else
        private const string Host = "osu.somtum.fun";
#endif

        private static readonly string LaunchTokenUrl = "https://" + Host + "/anticheat/launch-token";

        /// <summary>pkms's build id, stamped into the payload at build time by build-bundle.sh.</summary>
        public static readonly string PkmsBuildId = ReadPkmsBuildId();

        /// <summary>
        /// POST /anticheat/launch-token with (username, build_id); returns the raw env-var payload
        /// (<c>&lt;nonce&gt;:&lt;deadline_unix&gt;:&lt;sig&gt;</c>) or null when the server didn't
        /// answer. Caller sets this into the child process's <c>PKMS_LAUNCH_TOKEN</c> env.
        /// </summary>
        public static string IssueLaunchToken(string username)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(PkmsBuildId))
                return null;

            // The .NET Framework 4.7.2 default is TLS 1.0 — ensure TLS 1.2 reaches our Caddy.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(LaunchTokenUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.KeepAlive = false;
                // 0.9.4 §A — patcher attestation. Compute a per-request HMAC over
                // (username|build_id|ts|nonce) with the embedded shared secret. The nonce
                // is 16 bytes of CSPRNG (base64url-no-pad) and ts is UTC Unix seconds. The
                // server-side gate (HandshakeFlagOptions.PatcherSecret) verifies constant-
                // time and refuses requests outside the clock skew window. During rollout
                // (RequirePatcherAttestation=false) the server logs mismatches as warns
                // rather than refusing, so a stale Patcher still works; the flag flips
                // once every live Patcher is 0.9.4+.
                var reqTs = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var reqNonceBytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(reqNonceBytes);
                var reqNonce = Convert.ToBase64String(reqNonceBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
                var reqSigned = username + "|" + PkmsBuildId + "|" + reqTs + "|" + reqNonce;
                var reqAttestation = PatcherSecret.Sign(reqSigned);

                var payload = "{\"username\":" + JsonString(username)
                            + ",\"build_id\":" + JsonString(PkmsBuildId)
                            + ",\"ts\":" + reqTs
                            + ",\"nonce\":" + JsonString(reqNonce)
                            + ",\"attestation\":" + JsonString(reqAttestation) + "}";
                var payloadBytes = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = payloadBytes.Length;
                using (var reqStream = request.GetRequestStream())
                    reqStream.Write(payloadBytes, 0, payloadBytes.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    var body = reader.ReadToEnd();
                    // Minimal JSON scrape: the three fields come in a flat object. A real parser
                    // (DataContractJsonSerializer) would need a shared record and more types.
                    var nonce = ExtractJsonString(body, "nonce");
                    var deadline = ExtractJsonNumber(body, "deadline_unix");
                    var sig = ExtractJsonString(body, "sig");
                    if (nonce == null || deadline == null || sig == null)
                        return null;
                    return nonce + ":" + deadline + ":" + sig;
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse resp)
            {
                // Server answered unhappily (bad fields, no secret, service unavailable). Fail
                // closed by returning null — the server will refuse the subsequent /anticheat/key
                // if RequireLaunchToken is on, which is what we want.
                System.Diagnostics.Debug.WriteLine("launch-token: HTTP " + (int)resp.StatusCode);
                return null;
            }
            catch (Exception)
            {
                // Timeout / DNS / offline: fail open; the server-side feature flag decides whether
                // the subsequent /anticheat/key is accepted without a token.
                return null;
            }
        }

        /// <summary>
        /// Reads osu!'s stored username from <c>osu!.&lt;WindowsUser&gt;.cfg</c> next to osu!.exe
        /// (same source pkms uses in net.c :: read_osu_username). The two agree because the server
        /// HMACs the ticket against the username provided here, and pkms sends that same username
        /// in /anticheat/key.
        /// </summary>
        public static string ReadOsuUsername(string osuExePath)
        {
            if (string.IsNullOrEmpty(osuExePath))
                return null;
            try
            {
                var dir = Path.GetDirectoryName(osuExePath);
                if (string.IsNullOrEmpty(dir))
                    return null;
                var cfg = Path.Combine(dir, "osu!." + Environment.UserName + ".cfg");
                if (!File.Exists(cfg))
                    return null;
                foreach (var line in File.ReadAllLines(cfg, Encoding.UTF8))
                {
                    // "Username = <name>" or "Username=<name>"; the game writes both over its life.
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    if (!string.Equals(key, "Username", StringComparison.Ordinal))
                        continue;
                    var value = line.Substring(eq + 1).Trim();
                    return value.Length > 0 ? value : null;
                }
            }
            catch { }
            return null;
        }

        private static string ReadPkmsBuildId()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("pkms-build-id.txt"))
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd().Trim();
                }
            }
            catch { return null; }
        }

        private static string JsonString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string ExtractJsonString(string body, string field)
        {
            // "field"\s*:\s*"<value>" — minimal, non-nested. The response is a flat 3-field object.
            var needle = "\"" + field + "\"";
            int i = body.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i = body.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i++;
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length || body[i] != '"') return null;
            i++;
            int start = i;
            while (i < body.Length && body[i] != '"') i++;
            if (i >= body.Length) return null;
            return body.Substring(start, i - start);
        }

        private static string ExtractJsonNumber(string body, string field)
        {
            var needle = "\"" + field + "\"";
            int i = body.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i = body.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i++;
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            int start = i;
            while (i < body.Length && (char.IsDigit(body[i]) || body[i] == '-' || body[i] == '+'))
                i++;
            return i > start ? body.Substring(start, i - start) : null;
        }
    }
}
