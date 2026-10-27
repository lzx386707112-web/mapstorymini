// 登录服务。对齐源端 LoginPasswordHandler.java:55-156：
//   - 账号不存在 + automatic_register=true → 自动注册后登录
//   - 登录成功 → lastlogin/loggedin 更新
//   - 顶号（loginok=7）：本地单机只有一个会话，直接允许
using System;

public enum LoginResultCode
{
    Ok = 0,
    WrongPassword = 6,
    Banned = 3,
    TempBanned = 4,
    Invalid = 14,
}

public sealed class LoginResult
{
    public LoginResultCode code;
    public int accountId;
    public string accountName;
    public bool justRegistered;
}

public static class LoginService
{
    public static LoginResult Login(DbStore db, string name, string pwd)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 13)
            return new LoginResult { code = LoginResultCode.Invalid };

        var acc = AccountsDao.FindByName(db, name);
        if (acc == null)
        {
            if (!GameConfigDao.GetBool(db, "automatic_register", true))
                return new LoginResult { code = LoginResultCode.Invalid };
            int id = AccountsDao.AutoRegister(db, name, pwd);
            AccountsDao.UpdateLoginState(db, id, true);
            return new LoginResult { code = LoginResultCode.Ok, accountId = id, accountName = name, justRegistered = true };
        }

        string stored = (string)acc["password"];
        if (!AccountsDao.VerifyPassword(pwd, stored))
            return new LoginResult { code = LoginResultCode.WrongPassword };

        int accId = (int)System.Convert.ToInt64(acc["id"]);

        // 封禁：后台 accounts.banned / tempban 必须真的挡住登录，否则后台的「封停」只是改了个没人看的字段。
        // 源端 LoginPasswordHandler 同样先判 banned 再判 tempban。
        if (DbStore.Int(acc, "banned") != 0)
            return new LoginResult { code = LoginResultCode.Banned, accountId = accId, accountName = name };
        if (IsTempBanned(DbStore.Str(acc, "tempban")))
            return new LoginResult { code = LoginResultCode.TempBanned, accountId = accId, accountName = name };

        AccountsDao.UpdateLoginState(db, accId, true);
        return new LoginResult { code = LoginResultCode.Ok, accountId = accId, accountName = name };
    }

    // tempban 默认值 '2005-05-11 00:00:00' 表示「没有临时封禁」，晚于当前时间才算生效。
    static bool IsTempBanned(string tempban)
    {
        if (string.IsNullOrEmpty(tempban)) return false;
        return DateTime.TryParse(tempban, out var until) && until > DateTime.Now;
    }

    public static void Logout(DbStore db, int accountId)
        => AccountsDao.UpdateLoginState(db, accountId, false);
}
