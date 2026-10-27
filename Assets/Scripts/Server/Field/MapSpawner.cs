// 服务端地图级刷怪调度（MapleMap.respawn / calculateNumShouldSpawn / getCurrentSpawnRate /
// addMonsterSpawn），纯 C# 后端实现。前端负责按时调用 Respawn 并把返回的点真正生成/渲染。
//
// 对齐点（源端 MapleMap.java）：
//   - addMonsterSpawn：构造刷怪点，mobTime==-1 或 shouldSpawn 为真时立即生成（返回 true）
//   - respawn：按 calculateNumShouldSpawn 计算应补数量，遍历（可打乱的）刷怪点，对 shouldSpawn
//     为真的点返回，最多补到目标数；每点同时只占一只（SpawnPoint.spawnedMonsters 内自管）
//   - getCurrentSpawnRate = 0.70 + 0.05*min(6, 玩家数)（单人 0.75）
//   - use_enable_full_respawn / frenzy / use_monster_random_refresh_position / boss_respawn_mob_time_rate
//     均留配置钩子，默认对齐源端（单人 0.75、随机刷新开、boss 倍率 1.0）
using System;
using System.Collections.Generic;

namespace Server.Field
{
    public sealed class MapSpawner
    {
        // 时间源（毫秒）。后端不依赖 Unity：前端在创建后注入
        // （例：(long)(Time.realtimeSinceStartupAsDouble * 1000)），保证与帧时钟一致。
        public Func<long> Clock = () => (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

        readonly List<SpawnPoint> monsterSpawn = new List<SpawnPoint>();
        int mobInterval = 5000;
        bool mIsRandomRefresh = true;     // use_monster_random_refresh_position（默认开，与原客户端一致）
        bool allowSummons = true;

        // —— 配置钩子（默认对齐源端）——
        public double BossRespawnMobTimeRate = 1.0;   // boss_respawn_mob_time_rate，限定 (0,1]
        public bool FullRespawn { get; set; }         // use_enable_full_respawn
        public bool FrenzyTotemActive { get; set; }   // 狂热图腾期间补满

        public void SetMobInterval(int ms) => mobInterval = ms;
        public void SetRandomRefresh(bool v) => mIsRandomRefresh = v;
        public void SetAllowSummons(bool v) => allowSummons = v;

        // 构造刷怪点（≡ MapleMap.addMonsterSpawn）：返回该点，且若 mobTime==-1 或 shouldSpawn 为真应立即生成。
        public SpawnPoint AddMonsterSpawn(SpawnPointDef def)
        {
            int mt = def.mobTime;
            if (def.isBoss)
            {
                double rate = BossRespawnMobTimeRate;
                if (rate > 0.0 && rate <= 1.0) mt = (int)(mt * rate);   // 仅 Boss 缩放，限定 (0,1]
            }
            var scaled = new SpawnPointDef
            {
                mob = def.mob, mobTime = mt, fh = def.fh, f = def.f, team = def.team, cy = def.cy,
                x = def.x, y = def.y, rx0 = def.rx0, rx1 = def.rx1, immobile = def.immobile, isBoss = def.isBoss,
            };
            var sp = new SpawnPoint(scaled, mobInterval, Clock());
            monsterSpawn.Add(sp);
            return sp;
        }

        // 仅登记、不立即生成（≡ MapleMap.addAllMonsterSpawn，PQ 用）
        public void AddAllMonsterSpawn(SpawnPointDef def)
        {
            monsterSpawn.Add(new SpawnPoint(def, mobInterval, Clock()));
        }

        public int SpawnPointCount => monsterSpawn.Count;

        int GetSpawnedOnMap()
        {
            int n = 0;
            foreach (var sp in monsterSpawn) n += sp.Spawned;
            return n;
        }

        // 补怪主循环（≡ MapleMap.respawn）。返回此刻应当生成的刷怪点（前端负责真正生成并 MarkSpawned）。
        public List<SpawnPoint> Respawn(int numPlayers)
        {
            if (!allowSummons) return new List<SpawnPoint>();
            int numShouldSpawn = CalculateNumShouldSpawn(monsterSpawn.Count, GetSpawnedOnMap(), numPlayers, FullRespawn, FrenzyTotemActive);
            if (numShouldSpawn <= 0) return new List<SpawnPoint>();

            var randomSpawn = new List<SpawnPoint>(monsterSpawn);
            if (mIsRandomRefresh) Shuffle(randomSpawn);

            var result = new List<SpawnPoint>();
            int spawned = 0;
            long now = Clock();
            foreach (var sp in randomSpawn)
            {
                if (sp.ShouldSpawn(now))
                {
                    result.Add(sp);
                    if (++spawned >= numShouldSpawn) break;
                }
            }
            return result;
        }

        // 关闭/恢复所有刷怪点（≡ MapleMap.closeMapSpawnPoints / restoreMapSpawnPoints）
        public void CloseSpawnPoints()
        {
            foreach (var sp in monsterSpawn) sp.SetDenySpawn(true);
        }

        public void RestoreSpawnPoints()
        {
            foreach (var sp in monsterSpawn) sp.SetDenySpawn(false);
        }

        // 源端 MapleMap.getCurrentSpawnRate
        static double GetCurrentSpawnRate(int numPlayers) => 0.70 + 0.05 * Math.Min(6, numPlayers);

        // 源端 MapleMap.calculateNumShouldSpawn
        static int CalculateNumShouldSpawn(int spawnPointCount, int spawnedMonsterCount, int numPlayers, bool fullRespawn, bool frenzyTotemActive)
        {
            int target = (fullRespawn || frenzyTotemActive)
                ? spawnPointCount
                : (int)Math.Ceiling(GetCurrentSpawnRate(numPlayers) * spawnPointCount);
            return target - spawnedMonsterCount;
        }

        static void Shuffle(List<SpawnPoint> list)
        {
            var rng = new Random();
            int n = list.Count;
            while (n > 1)
            {
                int k = rng.Next(n--);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }
    }
}
