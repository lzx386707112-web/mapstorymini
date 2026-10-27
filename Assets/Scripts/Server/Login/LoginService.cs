// 登录服务。对齐源端 Client.login(...) 与 LoginPasswordHandler.handlePacket(...)：
//
//   Client.login 返回的 loginok（gms-server/src/main/java/org/gms/client/Client.java:675）
//     3  = 账号被封停（banned 先于密码校验，所以封停账号不会泄露密码对错）
//     4  = 密码错误                ← 注意不是 6
//     5  = 账号不存在
//     6  = 尝试次数过多 / 数据库异常
//     7  = 该账号已在别处登录（源端走顶号）
//     15 = accId 非法
//   LoginPasswordHandler 的额外分支
//     14 = 拿不到 remoteHost         → getLoginFailed(14)
//     10 = 同 IP/MAC 注册账号达上限   → getLoginFailed(10)
//     2  = 封停/临时封停              → getPermBan / getTempBan（LOGIN_STATUS reason=2 + 原因 + 到期时间）
//
// 这些编号就是发给客户端的 reason 字节，客户端据此弹「Notice」提示框，
// 所以本地服务端必须原样返回，不能自己另立一套。
using System;

public enum LoginResultCode
{
    Ok = 0,
    /// <summary>封停 / 临时封停（源端 getPermBan、getTempBan）。</summary>
    Banned = 2,
    /// <summary>IP / MAC 被封（源端 getLoginFailed(3)）。</summary>
    Blocked = 3,
    /// <summary>密码错误（源端 loginok = 4）。</summary>
    WrongPassword = 4,
    /// <summary>账号不存在，且没开自动注册（源端 loginok = 5）。</summary>
    NotRegistered = 5,
    /// <summary>系统错误 / 数据库异常（源端 loginok = 6）。</summary>
    Error = 6,
    /// <summary>该账号已在别处登录（源端 loginok = 7，源端直接顶号放行）。</summary>
    AlreadyLoggedIn = 7,
    /// <summary>同 IP/MAC 注册账号数达上限（源端 10）。</summary>
    RegisterLimit = 10,
    /// <summary>拿不到客户端地址等非法请求（源端 14）。</summary>
    Invalid = 14,
}

public sealed class LoginResult
{
    public LoginResultCode code;
    public int accountId;
    public string accountName;
    public bool justRegistered;
    /// <summary>封停到期时间（仅 Banned 用；null 表示永久封停）。源端 getTempBan 把时间一起发给客户端。</summary>
    public string banUntil;
}

public static class LoginService
{
    // 源端的 loginattempt 计数属于 TCP 会话状态（Client.java:678），
    // 本地单机没有会话概念、登录页又常驻，照搬会把玩家永久锁死在登录页，
    // 所以这里不实现「失败 5 次返 6」，只在成功时清账。
    public static LoginResult Login(DbStore db, string name, string pwd)
    {
        if (string.IsNullOrEmpty(name))
            return new LoginResult { code = LoginResultCode.Invalid };

        var acc = AccountsDao.FindByName(db, name);
        if (acc == null)
        {
            // 源端 LoginPasswordHandler:75 —— automatic_register 且 loginok == 5 才建号。
            if (!GameConfigDao.GetBool(db, "automatic_register", true))
                return new LoginResult { code = LoginResultCode.NotRegistered };
            int id = AccountsDao.AutoRegister(db, name, pwd);
            AccountsDao.UpdateLoginState(db, id, true);
            return new LoginResult { code = LoginResultCode.Ok, accountId = id, accountName = name, justRegistered = true };
        }

        int accId = (int)System.Convert.ToInt64(acc["id"]);
        if (accId <= 0)
            return new LoginResult { code = LoginResultCode.Invalid };

        // Client.login:708 —— 封停判定在密码校验之前。
        if (DbStore.Int(acc, "banned") != 0)
            return new LoginResult { code = LoginResultCode.Banned, accountId = accId, accountName = name };

        string stored = (string)acc["password"];
        if (!AccountsDao.VerifyPassword(pwd, stored))
            return new LoginResult { code = LoginResultCode.WrongPassword, accountId = accId, accountName = name };

        // LoginPasswordHandler:134 —— tempban 在密码通过之后才判，所以密码错时先报密码错。
        if (IsTempBanned(DbStore.Str(acc, "tempban"), out string until))
            return new LoginResult { code = LoginResultCode.Banned, accountId = accId, accountName = name, banUntil = until };

        AccountsDao.UpdateLoginState(db, accId, true);
        return new LoginResult { code = LoginResultCode.Ok, accountId = accId, accountName = name };
    }

    // tempban 默认值 '2005-05-11 00:00:00' 表示「没有临时封禁」，晚于当前时间才算生效。
    static bool IsTempBanned(string tempban, out string until)
    {
        until = null;
        if (string.IsNullOrEmpty(tempban)) return false;
        if (!DateTime.TryParse(tempban, out var date) || date <= DateTime.Now) return false;
        until = date.ToString("yyyy-MM-dd HH:mm");
        return true;
    }

    public static void Logout(DbStore db, int accountId)
        => AccountsDao.UpdateLoginState(db, accountId, false);
}
