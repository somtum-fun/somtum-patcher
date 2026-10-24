using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OsuPatcher.UI
{
    /// <summary>
    /// 0.9.0 §3 named-pipe handoff for the server-signed launch token. Keeps PKMS_LAUNCH_TOKEN
    /// out of osu!'s environment block — same-user processes could otherwise walk the PEB and
    /// lift the token for their lifetime of the osu! session. The pipe is named after the osu!
    /// pid (<c>\\.\pipe\pkms-launch-{pid}</c>) and refuses any client whose
    /// <c>GetNamedPipeClientProcessId</c> isn't that pid, so an attacker who learns the pipe
    /// name can't race in: pids are unique and pkms always connects from the one we just
    /// spawned. One-shot: delivers the token to the first matching client, then shuts down.
    /// </summary>
    internal static class LaunchTokenPipeServer
    {
        // Match pkms's \\.\pipe\pkms-launch-<pid> client (net.c read_launch_token_from_pipe).
        private const string PipeNameFormat = @"\\.\pipe\pkms-launch-{0}";

        private const uint PIPE_ACCESS_OUTBOUND = 0x00000002;
        private const uint PIPE_TYPE_BYTE = 0x00000000;
        private const uint PIPE_WAIT = 0x00000000;
        private const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
        private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
        private const int ERROR_PIPE_CONNECTED = 535;
        private const uint INVALID_HANDLE_VALUE = unchecked((uint)-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateNamedPipeW(
            string lpName, uint dwOpenMode, uint dwPipeMode,
            uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize,
            uint nDefaultTimeOut, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConnectNamedPipe(IntPtr hNamedPipe, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DisconnectNamedPipe(IntPtr hNamedPipe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Spawns a background thread that serves <paramref name="token"/> to the process with
        /// id <paramref name="osuProcessId"/> over the per-pid handoff pipe. The thread exits
        /// after one successful delivery or when <paramref name="timeout"/> elapses (whichever
        /// first); non-matching client connections are refused silently. Caller must set
        /// <c>PKMS_LAUNCH_PIPE=1</c> in the child env so pkms knows to look for the pipe.
        ///
        /// Returns a <see cref="ManualResetEventSlim"/> that is signalled when the server
        /// thread exits (either delivered the token or timed out). Headless (CLI) callers
        /// that are about to call <c>Application.Shutdown()</c> MUST wait on this handle
        /// first — the server thread is <c>IsBackground=true</c>, so it would otherwise be
        /// killed with the process before pkms (loaded a moment later via patcher-runtime)
        /// has a chance to connect. The returned handle is caller-owned; dispose when done.
        /// </summary>
        public static ManualResetEventSlim StartOneShot(int osuProcessId, string token, TimeSpan timeout)
        {
            var done = new ManualResetEventSlim(false);
            if (osuProcessId <= 0 || string.IsNullOrEmpty(token))
            {
                done.Set();
                return done;
            }

            var thread = new Thread(() =>
            {
                try { Serve(osuProcessId, token, timeout); }
                finally { done.Set(); }
            })
            {
                IsBackground = true,
                Name = "pkms-launch-pipe"
            };
            thread.Start();
            return done;
        }

        private static void Serve(int osuProcessId, string token, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var pipeName = string.Format(PipeNameFormat, osuProcessId);
            var payload = EncodePayload(token);

            while (DateTime.UtcNow < deadline)
            {
                var pipe = CreateNamedPipeW(pipeName,
                    PIPE_ACCESS_OUTBOUND | FILE_FLAG_FIRST_PIPE_INSTANCE,
                    PIPE_TYPE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                    nMaxInstances: 1,
                    nOutBufferSize: 1024,
                    nInBufferSize: 0,
                    nDefaultTimeOut: 1000,
                    lpSecurityAttributes: IntPtr.Zero);
                if ((uint)pipe.ToInt64() == INVALID_HANDLE_VALUE || pipe == IntPtr.Zero)
                    return;   // can't create — Wine missing feature, out of names, etc.

                bool connected = ConnectNamedPipe(pipe, IntPtr.Zero)
                    || Marshal.GetLastWin32Error() == ERROR_PIPE_CONNECTED;
                if (!connected)
                {
                    CloseHandle(pipe);
                    continue;
                }

                if (!GetNamedPipeClientProcessId(pipe, out var clientPid)
                    || (int)clientPid != osuProcessId)
                {
                    // Someone else connected first (or the kernel refused to tell us who).
                    // Drop them, re-create a fresh pipe, keep waiting for the real osu! pid.
                    DisconnectNamedPipe(pipe);
                    CloseHandle(pipe);
                    continue;
                }

                WriteFile(pipe, payload, (uint)payload.Length, out _, IntPtr.Zero);
                FlushFileBuffers(pipe);
                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
                return;
            }
        }

        // Wire: 2-byte little-endian length, then <length> UTF-8 bytes.
        private static byte[] EncodePayload(string token)
        {
            var body = Encoding.UTF8.GetBytes(token);
            if (body.Length > ushort.MaxValue)
                throw new InvalidOperationException("launch token too long for pipe wire format");
            var buf = new byte[body.Length + 2];
            buf[0] = (byte)(body.Length & 0xff);
            buf[1] = (byte)((body.Length >> 8) & 0xff);
            Buffer.BlockCopy(body, 0, buf, 2, body.Length);
            return buf;
        }
    }
}
