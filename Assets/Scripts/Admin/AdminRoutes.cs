// 后台管理接口表：URL 与源端 Spring Boot 控制器一一对应，前端一行代码都不用改。
//
// 已接入（数据落在当前单机服务端的表里）：
//   /auth/v1/*          登录 / 登出 / 续期
//   /account/v1/*       账号列表、资料、封停、重置在线、角色所在图
//   /character/v1/*     角色列表（含在线）、改名、传送到射手村
//   /inventory/v1/*     背包类型、角色选择、道具增删改
//   /config/v1/*        game_config 服务端参数
//   /server/v1/*        在线状态、版本、游戏内广播
//
// 未接入的模块（掉落/商店/图鉴/任务/地图检测/天气/BOT/扭蛋…）由 AdminApi 的兜底返回空数据，
// 页面能打开但不显示内容，清单见 docs/admin-console.md。
using System;
using System.Collections.Generic;

public static class AdminRoutes
{
    public static void Register(AdminApi api)
    {
        RegisterAuth(api);
        RegisterAccount(api);
        RegisterCharacter(api);
        RegisterInventory(api);
        RegisterConfig(api);
        RegisterServer(api);
    }

    // —————————— 认证 ——————————
    static void RegisterAuth(AdminApi api)
    {
        api.Post("/auth/v1/login", ctx =>
        {
            string name = ctx.BodyString("username");
            string pwd = ctx.BodyString("password");
            var acc = AdminBackend.Authenticate(name, pwd);
            if (acc == null) throw new AdminApiException("账号或密码错误，或该账号没有后台权限（webadmin）");
            return AdminJson.Obj("token", ctx.Api.CreateSession(acc));
        }, auth: false);

        api.Delete("/auth/v1/logout", ctx =>
        {
            ctx.Api.DropSession(ctx.Token);
            return null;
        }, auth: false);

        api.Get("/auth/v1/refreshToken", ctx =>
        {
            ctx.Api.DropSession(ctx.Token);
            return AdminJson.Obj("token", ctx.Api.CreateSession(ctx.User));
        });

        // 当前登录的后台账号。前端用 webadmin 决定角色（admin/user），必须是 true 才看得到菜单。
        api.Get("/account/v1/info", ctx =>
        {
            var dto = AdminBackend.AccountInfo(ctx.User.Id);
            if (dto == null) throw new AdminApiException("后台账号已被删除");
            return dto;
        });

        // 菜单走前端本地路由（settings.json 里 menuFromServer=false），这里返回空数组即可。
        api.Get("/account/v1/menu", ctx => new List<object>());
    }

    // —————————— 账号 ——————————
    static void RegisterAccount(AdminApi api)
    {
        api.Get("/account/v1", ctx => AdminBackend.AccountPage(
            ctx.QueryInt("page", 1), ctx.QueryInt("size", 20), ctx.QueryLong("id"),
            ctx.Query("name"), ctx.Query("lastLoginStart"), ctx.Query("lastLoginEnd"),
            ctx.Query("createdAtStart"), ctx.Query("createdAtEnd")));

        api.Get("/account/v1/{id}/characters/maps", ctx => AdminBackend.CharacterMaps(ctx.ParamLong("id")));

        api.Post("/account/v1", ctx =>
        {
            var form = AdminJson.AsDict(ctx.Body);
            bool hasLanguage = form != null && form.ContainsKey("language");
            AdminBackend.AddAccount(
                ctx.BodyString("name"),
                ctx.BodyString("password"),
                ctx.BodyString("birthday"),
                hasLanguage ? ctx.BodyLong("language") : (long?)null);
            return null;
        });

        api.Put("/account/v1/{id}", ctx =>
        {
            AdminBackend.UpdateAccount(ctx.ParamLong("id"), AdminJson.AsDict(ctx.Body));
            return null;
        });

        api.Delete("/account/v1/{id}", ctx =>
        {
            AdminBackend.DeleteAccount(ctx.ParamLong("id"));
            return null;
        });

        api.Put("/account/v1/{id}/ban", ctx =>
        {
            AdminBackend.BanAccount(ctx.ParamLong("id"), ctx.BodyString("reason"));
            return null;
        });

        api.Put("/account/v1/{id}/unban", ctx =>
        {
            AdminBackend.UnbanAccount(ctx.ParamLong("id"));
            return null;
        });

        api.Put("/account/v1/{id}/reset/logged", ctx =>
        {
            AdminBackend.ResetLoggedIn(ctx.ParamLong("id"));
            return null;
        });

        api.Put("/account/v1/{id}/moveToHenesys", ctx =>
        {
            AdminBackend.MoveAccountToHenesys(ctx.ParamLong("id"));
            return null;
        });
    }

    // —————————— 角色 ——————————
    static void RegisterCharacter(AdminApi api)
    {
        api.Post("/character/v1/online/list", ctx => AdminBackend.OnlineCharacterPage(
            ctx.BodyInt("pageNo", 1), ctx.BodyInt("pageSize", 20),
            ctx.BodyLong("id"), ctx.BodyString("name"), ctx.BodyLong("map")));

        api.Put("/character/v1/{id}/moveToHenesys", ctx =>
        {
            AdminBackend.MoveCharacterToHenesys(ctx.ParamLong("id"));
            return null;
        });

        api.Put("/character/v1/moveToHenesys", ctx =>
            AdminBackend.MoveCharactersToHenesys(ctx.BodyLong("id"), ctx.BodyString("name")));

        api.Put("/character/v1/{id}/changeName", ctx =>
        {
            AdminBackend.ChangeCharacterName(ctx.ParamLong("id"), ctx.BodyString("newName"));
            return null;
        });
    }

    // —————————— 背包 ——————————
    static void RegisterInventory(AdminApi api)
    {
        api.Get("/inventory/v1/getInventoryTypeList", ctx => AdminBackend.InventoryTypes());

        api.Post("/inventory/v1/getCharacterList", ctx => AdminBackend.InventoryCharacterPage(
            ctx.BodyLong("characterId"), ctx.BodyString("characterName"), ctx.BodyLong("accountId"),
            ctx.BodyInt("pageNo", 1), ctx.BodyInt("pageSize", 20)));

        api.Post("/inventory/v1/getInventoryList", ctx => AdminBackend.InventoryPage(
            ctx.BodyLong("characterId"), ctx.BodyLong("accountId"), ctx.BodyInt("inventoryType"),
            ctx.BodyInt("pageNo", 1), ctx.BodyInt("pageSize", 20)));

        api.Post("/inventory/v1/updateInventory", ctx =>
        {
            AdminBackend.UpdateInventory(AdminJson.AsDict(ctx.Body));
            return null;
        });

        api.Post("/inventory/v1/deleteInventory", ctx =>
        {
            AdminBackend.DeleteInventory(AdminJson.AsDict(ctx.Body));
            return null;
        });
    }

    // —————————— 服务端参数 ——————————
    static void RegisterConfig(AdminApi api)
    {
        api.Get("/config/v1/getConfigTypeList", ctx => AdminBackend.ConfigTypeList());

        api.Post("/config/v1/getConfigList", ctx => AdminBackend.ConfigPage(
            ctx.BodyString("type"), ctx.BodyString("subType"), ctx.BodyString("filter"),
            ctx.BodyInt("pageNo", 1), ctx.BodyInt("pageSize", 20)));

        api.Post("/config/v1/addConfig", ctx =>
        {
            AdminBackend.AddConfig(AdminJson.AsDict(ctx.Body));
            return null;
        });

        api.Post("/config/v1/updateConfig", ctx =>
        {
            AdminBackend.UpdateConfig(AdminJson.AsDict(ctx.Body));
            return null;
        });

        api.Delete("/config/v1/deleteConfig/{id}", ctx =>
        {
            AdminBackend.DeleteConfig(ctx.ParamLong("id"));
            return null;
        });

        api.Post("/config/v1/deleteConfigList", ctx =>
        {
            var list = AdminJson.AsList(ctx.Body);
            if (list == null) throw new AdminApiException("请选择要删除的参数");
            return AdminBackend.DeleteConfigList(list);
        });
    }

    // —————————— 服务器状态 ——————————
    static void RegisterServer(AdminApi api)
    {
        api.Get("/server/v1/online", ctx => AdminBackend.Ready);
        api.Get("/server/v1/version", ctx => AdminBackend.Version);

        // 单机版没有独立进程可停，这几个按钮明确报错，避免用户以为点了生效。
        api.Get("/server/v1/startServer", ctx => throw new AdminApiException("单机版没有独立服务端进程，无需启动"));
        api.Get("/server/v1/restartServer", ctx => throw new AdminApiException("单机版没有独立服务端进程，无法重启"));
        api.Get("/server/v1/shutdown", ctx => throw new AdminApiException("单机版请直接退出游戏"));
        api.Post("/server/v1/stopServerWithMsgAndInternal", ctx =>
            throw new AdminApiException("单机版没有独立服务端进程，无法停服"));

        api.Post("/server/v1/broadcast", ctx =>
        {
            string msg = ctx.BodyString("message");
            if (string.IsNullOrEmpty(msg)) throw new AdminApiException("广播内容不能为空");
            AdminBackend.Broadcast?.Invoke(msg);
            return null;
        });
    }
}
