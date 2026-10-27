// 后台管理 HTTP 路由层：静态资源 + REST 接口 + token 鉴权。
//
// 与源端（Spring Boot）保持同一套 URL 与返回体：
//   URL     /{模块}/v1/{动作}      例：/account/v1、/config/v1/getConfigList
//   返回体  { code, message, responseId, data }，code=20000 表示成功
// 前端 axios 拦截器只认 code，所以这里必须严格照抄，不能改成 HTTP 状态码表达业务错误。
//
// 纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;
using System.Text;

public delegate object AdminHandler(AdminCtx ctx);

public sealed class AdminAccount
{
    public long Id;
    public string Name;
    public bool WebAdmin;
    public long LastLoginUnix;
}

public sealed class AdminCtx
{
    public AdminApi Api;
    public AdminHttpRequest Req;
    public readonly Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public object RawBody;
    public object Body;
    public string Token;
    public AdminAccount User;

    public string Param(string key)
        => Params.TryGetValue(key, out var v) ? v : null;

    public long ParamLong(string key)
        => long.TryParse(Param(key), out var v) ? v : 0;

    public string Query(string key, string fallback = null) => Req.QueryValue(key, fallback);
    public int QueryInt(string key, int fallback = 0) => Req.QueryInt(key, fallback);

    public long QueryLong(string key, long fallback = 0)
        => long.TryParse(Req.QueryValue(key), out var v) ? v : fallback;

    public string BodyString(string key, string fallback = "") => AdminJson.GetString(Body, key, fallback);
    public long BodyLong(string key, long fallback = 0) => AdminJson.GetLong(Body, key, fallback);
    public int BodyInt(string key, int fallback = 0) => AdminJson.GetInt(Body, key, fallback);
    public bool BodyBool(string key, bool fallback = false) => AdminJson.GetBool(Body, key, fallback);
}

public sealed class AdminApi
{
    sealed class Route
    {
        public string Method;
        public string[] Segments;
        public AdminHandler Handler;
        public bool RequireAuth;
    }

    sealed class Session
    {
        public AdminAccount Account;
        public DateTime Expires;
    }

    readonly List<Route> routes = new List<Route>();
    readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
    readonly object sessionGate = new object();
    readonly HashSet<string> stubLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public AdminStaticBundle Statics;
    public Action<string> Log;
    public bool LogStubs = true;

    // —————————— 注册 ——————————
    public void Get(string pattern, AdminHandler h, bool auth = true) => Add("GET", pattern, h, auth);
    public void Post(string pattern, AdminHandler h, bool auth = true) => Add("POST", pattern, h, auth);
    public void Put(string pattern, AdminHandler h, bool auth = true) => Add("PUT", pattern, h, auth);
    public void Delete(string pattern, AdminHandler h, bool auth = true) => Add("DELETE", pattern, h, auth);

    void Add(string method, string pattern, AdminHandler handler, bool auth)
    {
        var segs = pattern.Trim('/').Split('/');
        routes.Add(new Route { Method = method, Segments = segs, Handler = handler, RequireAuth = auth });
    }

    // —————————— 会话 ——————————
    public string CreateSession(AdminAccount acc)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 8);
        lock (sessionGate)
        {
            sessions[token] = new Session { Account = acc, Expires = DateTime.UtcNow.AddHours(12) };
        }
        return token;
    }

    public void DropSession(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (sessionGate) sessions.Remove(token);
    }

    AdminAccount Resolve(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        lock (sessionGate)
        {
            if (!sessions.TryGetValue(token, out var s)) return null;
            if (s.Expires < DateTime.UtcNow) { sessions.Remove(token); return null; }
            s.Expires = DateTime.UtcNow.AddHours(12);
            return s.Account;
        }
    }

    static string TokenOf(AdminHttpRequest req)
    {
        var raw = req.Header("Authorization");
        if (string.IsNullOrEmpty(raw)) return null;
        if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return raw.Substring(7).Trim();
        return raw.Trim();
    }

    // —————————— 分发 ——————————
    public AdminHttpResponse Handle(AdminHttpRequest req)
    {
        if (req.Method == "OPTIONS") return new AdminHttpResponse { Status = 204, Body = Array.Empty<byte>() };

        string[] segs = req.Path.Trim('/').Split('/');
        bool pathMatched = false;

        foreach (var route in routes)
        {
            if (route.Method != req.Method) continue;
            if (route.Segments.Length != segs.Length) continue;

            var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool ok = true;
            for (int i = 0; i < segs.Length; i++)
            {
                var p = route.Segments[i];
                if (p.Length > 2 && p[0] == '{' && p[p.Length - 1] == '}')
                    captured[p.Substring(1, p.Length - 2)] = Uri.UnescapeDataString(segs[i]);
                else if (!string.Equals(p, segs[i], StringComparison.OrdinalIgnoreCase))
                { ok = false; break; }
            }
            if (!ok) continue;

            pathMatched = true;
            var ctx = new AdminCtx { Api = this, Req = req, Token = TokenOf(req) };
            foreach (var kv in captured) ctx.Params[kv.Key] = kv.Value;

            if (route.RequireAuth)
            {
                ctx.User = Resolve(ctx.Token);
                if (ctx.User == null)
                {
                    var unauthorized = AdminHttpResponse.Json(AdminJson.Fail("登录已过期，请重新登录", 401), 401);
                    return unauthorized;
                }
            }

            if (req.BodyText.Length > 0)
            {
                try
                {
                    ctx.RawBody = AdminJson.Parse(req.BodyText);
                    ctx.Body = AdminJson.UnwrapSubmit(ctx.RawBody);
                }
                catch (Exception e)
                {
                    return AdminHttpResponse.Json(AdminJson.Fail("请求体不是合法 JSON: " + e.Message), 400);
                }
            }

            try
            {
                var result = route.Handler(ctx);
                if (result is AdminHttpResponse raw) return raw;
                return AdminHttpResponse.Json(AdminJson.Ok(result));
            }
            catch (AdminApiException e)
            {
                return AdminHttpResponse.Json(AdminJson.Fail(e.Message));
            }
            catch (Exception e)
            {
                Log?.Invoke("接口异常 " + req.Method + " " + req.Path + " : " + e);
                return AdminHttpResponse.Json(AdminJson.Fail(e.Message), 500);
            }
        }

        if (pathMatched)
            return AdminHttpResponse.Json(AdminJson.Fail("方法不允许: " + req.Method, 405), 405);

        // 1) 实体静态文件（js/css/图片/favicon…）优先，避免和 /assets/xxx 这类接口抢路径
        if (Statics != null && Statics.TryGetExact(req.Path, out var exact))
            return AdminHttpResponse.Bytes(exact.Data, exact.ContentType);

        // 2) 未实现的接口：返回成功 + 空数据，让对应页面渲染成空表而不是弹错误。
        // 这些模块（掉落/商店/图鉴/任务/地图检测等）的数据源还没接，见 docs/admin-console.md。
        if (LooksLikeApi(req.Path))
        {
            if (LogStubs)
            {
                lock (stubLogged)
                {
                    if (stubLogged.Add(req.Method + " " + req.Path))
                        Log?.Invoke("后台接口未接入（返回空数据）: " + req.Method + " " + req.Path);
                }
            }
            return AdminHttpResponse.Json(AdminJson.Ok(null));
        }

        // 3) 剩下的当 SPA 路由，回 index.html
        if (Statics != null && Statics.TryGet(req.Path, out var file))
            return AdminHttpResponse.Bytes(file.Data, file.ContentType);

        return AdminHttpResponse.Json(AdminJson.Fail("找不到资源: " + req.Path, 404), 404);
    }

    static bool LooksLikeApi(string path)
    {
        if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)) return true;
        return path.Contains("/v1/") || path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }
}

// 业务异常：消息直接回给前端弹窗（code=-1）。
public sealed class AdminApiException : Exception
{
    public AdminApiException(string message) : base(message) { }
}
