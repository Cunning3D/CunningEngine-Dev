using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CunningEngine.Editor.Coverlay {
    /// <summary>Minimal static file server for the embedded Cunning Player.</summary>
    public sealed class CunningLocalHttpServer : IDisposable {
        readonly string _root;
        TcpListener _listener;
        CancellationTokenSource _cts;
        Task _loop;

        public int Port { get; private set; }
        public bool Running => _listener != null;

        public CunningLocalHttpServer(string rootAbsPath) {
            _root = Path.GetFullPath(rootAbsPath ?? "");
            if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
        }

        public void Start(int preferredPort = 0) {
            if (Running) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, preferredPort);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Dispose() {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _cts = null;
            _listener = null;
            _loop = null;
            Port = 0;
        }

        async Task AcceptLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                TcpClient client = null;
                try {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client, ct), ct);
                } catch {
                    try { client?.Close(); } catch { }
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        static string ReadLine(Stream s) {
            var sb = new StringBuilder(128);
            while (true) {
                int b = s.ReadByte();
                if (b < 0) break;
                if (b == '\n') break;
                if (b == '\r') continue;
                sb.Append((char)b);
                if (sb.Length > 8192) break;
            }
            return sb.ToString();
        }

        async Task HandleClient(TcpClient c, CancellationToken ct) {
            using (c) {
                c.NoDelay = true;
                using var s = c.GetStream();
                s.ReadTimeout = 4000;
                s.WriteTimeout = 4000;

                try {
                    var req = ReadLine(s);
                    if (string.IsNullOrEmpty(req)) return;
                    var parts = req.Split(' ');
                    if (parts.Length < 2) return;
                    var method = parts[0].Trim().ToUpperInvariant();
                    var rawPath = parts[1].Trim();
                    for (;;) { var h = ReadLine(s); if (string.IsNullOrEmpty(h)) break; } // drain headers
                    if (method != "GET" && method != "HEAD") { await WriteStatus(s, 405, "Method Not Allowed", "text/plain", ""); return; }

                    var pathOnly = rawPath;
                    var q = rawPath.IndexOf('?');
                    if (q >= 0) pathOnly = rawPath.Substring(0, q);
                    pathOnly = Uri.UnescapeDataString(pathOnly).Replace('\\', '/');
                    if (string.IsNullOrEmpty(pathOnly) || pathOnly == "/") pathOnly = "/index.html";
                    if (pathOnly.StartsWith("/")) pathOnly = pathOnly.Substring(1);

                    var abs = Path.GetFullPath(Path.Combine(_root, pathOnly));
                    if (!abs.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(abs)) {
                        await WriteStatus(s, 404, "Not Found", "text/plain", "404");
                        return;
                    }

                    var ext = Path.GetExtension(abs).ToLowerInvariant();
                    var ctType = ext switch {
                        ".html" => "text/html; charset=utf-8",
                        ".js" => "text/javascript; charset=utf-8",
                        ".css" => "text/css; charset=utf-8",
                        ".wasm" => "application/wasm",
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".json" => "application/json; charset=utf-8",
                        ".cda" => "application/octet-stream",
                        _ => "application/octet-stream",
                    };
                    var bytes = await File.ReadAllBytesAsync(abs, ct).ConfigureAwait(false);
                    await WriteBytes(s, 200, "OK", ctType, bytes, method == "HEAD").ConfigureAwait(false);
                } catch {
                    try { await WriteStatus(s, 500, "Internal Server Error", "text/plain", "500"); } catch { }
                }
            }
        }

        static async Task WriteStatus(Stream s, int code, string msg, string ct, string body) {
            var b = Encoding.UTF8.GetBytes(body ?? "");
            await WriteBytes(s, code, msg, ct, b, false).ConfigureAwait(false);
        }

        static async Task WriteBytes(Stream s, int code, string msg, string ct, byte[] body, bool headOnly) {
            body ??= Array.Empty<byte>();
            var hdr =
                $"HTTP/1.1 {code} {msg}\r\n" +
                $"Content-Type: {ct}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                $"Cache-Control: no-cache\r\n" +
                $"Access-Control-Allow-Origin: *\r\n" +
                $"Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(hdr);
            await s.WriteAsync(hb, 0, hb.Length).ConfigureAwait(false);
            if (!headOnly && body.Length != 0) await s.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }
    }
}

