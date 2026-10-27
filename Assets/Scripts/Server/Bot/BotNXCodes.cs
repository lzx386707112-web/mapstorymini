using System;
using System.Text;

namespace Server.Bot
{
    public sealed class BotNXCodes
    {
        readonly DbStore db;
        readonly Random random;
        public BotNXCodes(DbStore db, Random random = null) { this.db = db; this.random = random ?? new Random(); }
        public string Generate()
        {
            const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
            var code = new StringBuilder();
            for (int section = 0; section < 3; section++)
            { if (section > 0) code.Append('-'); for (int i = 0; i < 5; i++) code.Append(alphabet[random.Next(alphabet.Length)]); }
            return code.ToString();
        }
        public int Create(string code, int quantity = 10000)
        {
            code = code.Replace("-", "");
            lock (db)
            {
                if (db.Table("nxcode").Find(r => DbStore.Str(r,"code") == code) != null) return -1;
                db.BeginTransaction();
                try
                {
                    long id = db.Table("nxcode").Insert(new DbRow { ["code"] = code, ["retriever"] = null, ["expiration"] = 8901234567890L });
                    db.Table("nxcode_items").Insert(new DbRow { ["codeid"] = id, ["type"] = 4L, ["item"] = 0L, ["quantity"] = (long)quantity });
                    db.Commit(); db.Save(); return checked((int)id);
                }
                catch { db.Rollback(); throw; }
            }
        }
        // CouponCodeHandler: type 4实际发nxCredit（原NXCodeManager注释称prepaid不准确）。
        public string Redeem(CharRecord actor, string code, long now)
        {
            var row = db.Table("nxcode").Find(r => DbStore.Str(r,"code") == code);
            if (row == null) return "兑换码不存在";
            if (row["retriever"] != null) return "兑换码已使用";
            if (DbStore.Long(row,"expiration") < now) return "兑换码已过期";
            var rewards = db.Table("nxcode_items").Select(r => DbStore.Int(r,"codeid") == DbStore.Int(row,"id"));
            var account = AccountsDao.FindById(db,actor.accountid); if (account == null) return "账号不存在";
            foreach (var reward in rewards) if (DbStore.Int(reward,"type") != 4) return "该兑换码奖励类型尚未支持";
            long total = DbStore.Int(account,"nxCredit");
            foreach (var reward in rewards) total += DbStore.Int(reward,"quantity");
            if (total < 0 || total > int.MaxValue) return "余额超出上限";
            account["nxCredit"] = total; row["retriever"] = actor.name; return null;
        }
    }
}
