// 后台管理系统的本地 HTTP 服务（纯 C#，不依赖 UnityEngine）。
//
// 为什么不用 HttpListener：Android / IL2CPP 上 System.Net.HttpListener 会抛
// PlatformNotSupportedException，单机 APK 里起不来。这里直接用 TcpListener 写一个
// 够用的 HTTP/1.1 实现：只处理「请求行 + 头 + Content-Length 正文」，响应一次一连接。
//
// 只监听 127.0.0.1：后台只给本机浏览器用，不暴露到局域网。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public sealed class AdminHttpRequest
{
    public string Method = "GET";
    public string RawPath = "/";          // 未解码的 path
    public string Path = "/";             // 解码后的 path
    public string RawQuery = "";
    public readonly Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string BodyText = "";
    public byte[] BodyBytes = Array.Empty<byte>();

    public string Header(string name)
        => Headers.TryGetValue(name, out var v) ? v : null;

    public string QueryValue(string name, string fallback = null)
        => Query.TryGetValue(name, out var v) ? v : fallback;

    public int QueryInt(string name, int fallback = 0)
        => Query.TryGetValue(name, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;
}

public sealed class AdminHttpResponse
{
    public int Status = 200;
    public string ContentType = "application/json; charset=utf-8";
    public byte[] Body = Array.Empty<byte>();
    public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static AdminHttpResponse Json(object payload, int status = 200)
    {
        return new AdminHttpResponse
        {
            Status = status,
            ContentType = "application/json; charset=utf-8",
            Body = Encoding.UTF8.GetBytes(AdminJson.Write(payload)),
        };
    }

    public static AdminHttpResponse Text(string text, int status = 200, string type = "text/plain; charset=utf-8")
    {
        return new AdminHttpResponse { Status = status, ContentType = type, Body = Encoding.UTF8.GetBytes(text ?? "") };
    }

    public static AdminHttpResponse Bytes(byte[] data, string type, int status = 200)
    {
        return new AdminHttpResponse { Status = status, ContentType = type, Body = data ?? Array.Empty<byte>() };
    }
}

public sealed class AdminHttpServer : IDisposable
{
    public const int MaxBodyBytes = 16 * 1024 * 1024;

    readonly Func<AdminHttpRequest, AdminHttpResponse> handler;
    readonly Action<string> log;
    TcpListener listener;
    Thread acceptThread;
    volatile bool running;

    public int Port { get; private set; }
    public bool Running => running;

    public AdminHttpServer(int port, Func<AdminHttpRequest, AdminHttpResponse> handler, Action<string> log = null)
    {
        Port = port;
        this.handler = handler;
        this.log = log;
    }

    // 依次尝试 port..port+9，全部占用则抛异常（调用方负责提示）。
    public static AdminHttpServer StartOn(int firstPort, Func<AdminHttpRequest, AdminHttpResponse> handler,
        Action<string> log = null, int attempts = 10)
    {
        Exception last = null;
        for (int i = 0; i < attempts; i++)
        {
            var srv = new AdminHttpServer(firstPort + i, handler, log);
            try
            {
                srv.Start();
                return srv;
            }
            catch (Exception e)
            {
                last = e;
                srv.Dispose();
            }
        }
        throw new InvalidOperationException("本地端口全部被占用（" + firstPort + ".." + (firstPort + attempts - 1) + "）", last);
    }

    public void Start()
    {
        if (running) return;
        listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start(64);
        // 端口被占时 TcpListener 在 Start 里就抛，这里读回真实端口（port=0 时有用）
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        running = true;
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AdminHttpAccept" };
        acceptThread.Start();
        log?.Invoke("后台管理服务已启动: http://127.0.0.1:" + Port + "/");
    }

    void AcceptLoop()
    {
        while (running)
        {
            TcpClient client = null;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (!running) return;
                continue;
            }

            var c = client;
            ThreadPool.QueueUserWorkItem(_ => HandleClient(c));
        }
    }

    void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;
                client.NoDelay = true;

                using (var stream = client.GetStream())
                {
                    var req = ReadRequest(stream);
                    if (req == null) return;

                    AdminHttpResponse res;
                    try
                    {
                        res = handler(req) ?? AdminHttpResponse.Json(AdminJson.Fail("空响应"), 500);
                    }
                    catch (Exception e)
                    {
                        log?.Invoke("处理请求出错 " + req.Method + " " + req.RawPath + " : " + e.Message);
                        res = AdminHttpResponse.Json(AdminJson.Fail(e.Message), 500);
                    }

                    WriteResponse(stream, req, res);
                }
            }
        }
        catch (Exception e)
        {
            log?.Invoke("连接处理失败: " + e.Message);
        }
    }

    // —————————— 请求解析 ——————————
    static AdminHttpRequest ReadRequest(NetworkStream stream)
    {
        byte[] carry;
        var head = ReadHead(stream, out carry);
        if (head == null) return null;

        int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd < 0 ? head : head.Substring(0, lineEnd);
        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return null;

        var req = new AdminHttpRequest
        {
            Method = parts[0].ToUpperInvariant(),
        };

        string target = parts[1];
        int q = target.IndexOf('?');
        req.RawPath = q < 0 ? target : target.Substring(0, q);
        req.RawQuery = q < 0 ? "" : target.Substring(q + 1);
        req.Path = DecodePath(req.RawPath);
        ParseQuery(req.RawQuery, req.Query);

        // 头
        int cursor = lineEnd < 0 ? head.Length : lineEnd + 2;
        while (cursor < head.Length)
        {
            int next = head.IndexOf("\r\n", cursor, StringComparison.Ordinal);
            if (next < 0) next = head.Length;
            string line = head.Substring(cursor, next - cursor);
            cursor = next + 2;
            if (line.Length == 0) break;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string name = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            req.Headers[name] = value;
        }

        // 正文：头部读取时可能已经把正文一起读进 carry，先吃掉这部分
        if (int.TryParse(req.Header("Content-Length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var len) && len > 0)
        {
            if (len > MaxBodyBytes) throw new InvalidOperationException("请求体过大");
            var buf = new byte[len];
            int read = 0;
            if (carry != null && carry.Length > 0)
            {
                read = Math.Min(len, carry.Length);
                Array.Copy(carry, 0, buf, 0, read);
            }
            while (read < len)
            {
                int n = stream.Read(buf, read, len - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != len) Array.Resize(ref buf, read);
            req.BodyBytes = buf;
            req.BodyText = Encoding.UTF8.GetString(buf);
        }

        return req;
    }

    // 读到 \r\n\r\n 为止；剩下的字节（可能是正文开头）通过 carry 返回，不能丢。
    static string ReadHead(NetworkStream stream, out byte[] carry)
    {
        carry = null;
        var acc = new List<byte>(1024);
        var buf = new byte[4096];
        int headEnd = -1;

        while (acc.Count < 64 * 1024)
        {
            int n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            acc.AddRange(new ArraySegment<byte>(buf, 0, n));
            headEnd = FindHeadEnd(acc);
            if (headEnd >= 0) break;
        }

        if (acc.Count == 0) return null;
        if (headEnd < 0) headEnd = acc.Count;

        var headBytes = acc.GetRange(0, headEnd).ToArray();
        int bodyStart = Math.Min(acc.Count, headEnd + 4);
        int carryLen = acc.Count - bodyStart;
        if (carryLen > 0) carry = acc.GetRange(bodyStart, carryLen).ToArray();

        return Encoding.UTF8.GetString(headBytes);
    }

    static int FindHeadEnd(List<byte> data)
    {
        // 头部最大 64 KB，整段扫描的代价可以忽略；不要只从尾部找——
        // 第一次 Read 可能一次就把头和正文都读进来。
        for (int i = 0; i + 3 < data.Count; i++)
        {
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                return i;
        }
        return -1;
    }

    static string DecodePath(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "/";
        try { return Uri.UnescapeDataString(raw); }
        catch (Exception) { return raw; }
    }

    static void ParseQuery(string raw, Dictionary<string, string> into)
    {
        if (string.IsNullOrEmpty(raw)) return;
        foreach (var pair in raw.Split('&'))
        {
            if (pair.Length == 0) continue;
            int eq = pair.IndexOf('=');
            string k = eq < 0 ? pair : pair.Substring(0, eq);
            string v = eq < 0 ? "" : pair.Substring(eq + 1);
            try
            {
                k = Uri.UnescapeDataString(k.Replace('+', ' '));
                v = Uri.UnescapeDataString(v.Replace('+', ' '));
            }
            catch (Exception) { }
            if (k.Length > 0) into[k] = v;
        }
    }

    // —————————— 响应写出 ——————————
    static void WriteResponse(NetworkStream stream, AdminHttpRequest req, AdminHttpResponse res)
    {
        var sb = new StringBuilder(256);
        sb.Append("HTTP/1.1 ").Append(res.Status).Append(' ').Append(StatusText(res.Status)).Append("\r\n");
        sb.Append("Content-Type: ").Append(res.ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(res.Body?.Length ?? 0).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Headers: Authorization, Content-Type\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n");
        foreach (var kv in res.Headers)
            sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
        sb.Append("Connection: close\r\n\r\n");

        var headBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headBytes, 0, headBytes.Length);
        if (res.Body != null && res.Body.Length > 0 && req.Method != "HEAD")
            stream.Write(res.Body, 0, res.Body.Length);
        stream.Flush();
    }

    static string StatusText(int code)
    {
        switch (code)
        {
            case 200: return "OK";
            case 204: return "No Content";
            case 301: return "Moved Permanently";
            case 302: return "Found";
            case 304: return "Not Modified";
            case 400: return "Bad Request";
            case 401: return "Unauthorized";
            case 403: return "Forbidden";
            case 404: return "Not Found";
            case 405: return "Method Not Allowed";
            case 411: return "Length Required";
            case 413: return "Payload Too Large";
            case 500: return "Internal Server Error";
            case 501: return "Not Implemented";
            case 503: return "Service Unavailable";
            default: return "OK";
        }
    }

    public void Dispose()
    {
        running = false;
        try { listener?.Stop(); } catch (Exception) { }
        listener = null;
    }
}
