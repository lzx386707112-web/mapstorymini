// 服务端刷怪点状态机（org.gms.server.life.SpawnPoint），纯 C# 后端实现。
// 不引用 UnityEngine：位置/时间都由前端在构造与调用时注入，本类只持有规则与计数。
//
// 对齐点（源端 SpawnPoint.java）：
//   - shouldSpawn 判定：denySpawn || mobTime<0 || 已生成>0 → false；否则看 nextPossibleSpawn
//   - shouldForceSpawn：mobTime>=0 && 已生成<=0（实例强制补怪用）
//   - 死亡回调（MonsterListener.monsterKilled）：mobTime>0 按秒、mobTime==0 按死亡动画时长
//   - mobTime==0 出怪瞬间 nextPossibleSpawn += mobInterval(5000ms) 冷却
//   - respawnTimeMultiplier 缩放（狂热图腾等）、duplicate（frenzy 用）、denySpawn（关图/脚本禁用）
//   落点(fh/f/pos) 由前端在构造 SpawnPointDef 时按 calcPointBelow 落到 foothold，
//   后端只保存最终结果（源端 addMonsterSpawn 同样在构造时算好落点）。
using System;

namespace Server.Field
{
    // 刷怪点定义（源自地图 life 节点）。纯数据，可前端构建后传入。
    public sealed class SpawnPointDef
    {
        public int mob;
        public int mobTime;     // 秒；<0 不再重生，-1 只刷一次（强制一次）
        public int fh, f, team, cy;
        public float x, y;      // 已是落到 foothold 后的地图像素坐标
        public float rx0, rx1;
        public bool immobile;
        public bool isBoss;
    }

    public sealed class SpawnPoint
    {
        public SpawnPointDef Def { get; }

        readonly int monster;
        readonly int mobTime;
        readonly int team;
        readonly int fh;
        readonly int f;
        readonly float posX, posY;
        readonly bool immobile;

        long nextPossibleSpawn;
        int mobInterval = 5000;
        double respawnTimeMultiplier = 1.0;
        int spawnedMonsters;
        bool denySpawn;

        public SpawnPoint(SpawnPointDef def, int mobIntervalMs, long nowMs)
        {
            Def = def;
            monster = def.mob;
            mobTime = def.mobTime;
            team = def.team;
            fh = def.fh;
            f = def.f;
            posX = def.x;
            posY = def.y;
            immobile = def.immobile;
            this.mobInterval = mobIntervalMs;
            // 源端构造时 nextPossibleSpawn = Server.getInstance().getCurrentTime()
            nextPossibleSpawn = nowMs;
        }

        public int MobId => monster;
        public int GetFh() => fh;
        public int GetF() => f;
        public int Team => team;
        public float PosX => posX;
        public float PosY => posY;
        public int MobTime => mobTime;
        public int Spawned => spawnedMonsters;

        public void SetDenySpawn(bool v) => denySpawn = v;
        public bool GetDenySpawn() => denySpawn;

        public void SetRespawnTimeMultiplier(double m)
        {
            if (m <= 0.0) throw new ArgumentException("respawn multiplier must be positive");
            respawnTimeMultiplier = m;
        }

        // 源端 SpawnPoint.scaleRespawnDelay
        long ScaleRespawnDelay(long delay) => Math.Max(0L, (long)Math.Round(delay * respawnTimeMultiplier));

        // 源端 SpawnPoint.shouldSpawn
        public bool ShouldSpawn(long nowMs)
        {
            if (denySpawn || mobTime < 0 || spawnedMonsters > 0) return false;
            return nextPossibleSpawn <= nowMs;
        }

        // 源端 SpawnPoint.shouldForceSpawn
        public bool ShouldForceSpawn()
        {
            return mobTime >= 0 && spawnedMonsters <= 0;
        }

        // 前端真正生成怪物时调用（≡ SpawnPoint.getMonster 的 spawnedMonsters++ 与 mobTime==0 冷却）
        public void MarkSpawned(long nowMs)
        {
            spawnedMonsters++;
            if (mobTime == 0)
                nextPossibleSpawn = nowMs + ScaleRespawnDelay(mobInterval);
        }

        // 怪物死亡时调用（≡ MonsterListener.monsterKilled）；aniTimeMs 为死亡动画时长（毫秒）
        public void OnKilled(long aniTimeMs, long nowMs)
        {
            nextPossibleSpawn = nowMs;
            if (mobTime > 0)
                nextPossibleSpawn += ScaleRespawnDelay(mobTime * 1000L);
            else
                nextPossibleSpawn += ScaleRespawnDelay(aniTimeMs);
            if (spawnedMonsters > 0) spawnedMonsters--;
        }

        // Source duplicate constructs a fresh unoccupied point at current server time.
        public SpawnPoint Duplicate(long nowMs)
        {
            var copy = new SpawnPoint(Def, mobInterval, nowMs)
            {
                denySpawn = denySpawn,
                respawnTimeMultiplier = respawnTimeMultiplier,
            };
            return copy;
        }
    }
}
