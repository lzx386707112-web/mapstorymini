# 九转涅槃（2026-11-02）

本模块是源端Java/JS迁移，不新增EXE/DLL函数或界面布局结论。用户确认执行逻辑按源端，不能以脚本的“职业不会变更”文案覆盖真实服务端行为。

## 源文件身份

根目录 `~/Documents/mxd/Beidou-Server/gms-server`：

- src/main/java/org/gms/client/Character.java SHA256 `b72a138f09a8dadbb88faad54c2f14c1668d74608f1a59b1c394af55b0bce62a`
- src/main/java/org/gms/client/Job.java SHA256 `2647148552788c5e9c4d23f2b277a3643745480e7c4279c716f9a90fa949d440`
- src/main/java/org/gms/config/GameConfig.java SHA256 `1f26872708392766c334c3bcf62b5263872422401fcf959e454a6a62d914d711`
- src/main/resources/db/migration/V2.1.50__raise_character_level_cap_to_255_and_rebirth_cap_to_10.sql SHA256 `ee38e4830dc4310cec306d6a9756f94157b4239a66f476ec4232c08edb1d66f2`
- scripts-zh-CN/BeiDouSpecial/一键转生.js SHA256 `d8a1d655dd63521cdd3ccb6f7b9d1e0183ba066dbfd901a121d0803daa596054`

## 已核实链路

`npc/9900001.js` case14 → `BeiDouSpecial/一键转生.js`。start 从 GameConfig.getServerInt 读 `max_reborn_count`，缺键返回0；本地之前漏键，导致显示0，最终 `levelRebirth` 的 `reborns>=maxRebornCount` 永远拒绝。V2.1.50 SQL明确设10。原JS只在最终提交检查次数；start的isCan仅检查等级255、4039020×30、2049115×10、NX_CREDIT≥50000，不能擅自重写脚本条件。

`start` → `sendNextLevel(Enquire)` → `levelEnquire` → `sendNextLevel(Rebirth)` → `levelRebirth`。达到上限先拒绝，不扣材料。成功调用 `Character.rebirth(false,false,0)` → `changeJobAndLevel`；随后原JS resetStats、equipChanged、永久偷学技能点+1、发4032170/4032171各50、扣50000点券、扣两种材料、广播、成功对白。原脚本未修改，复制版与源端SHA一致。

Character.java:10531..10640：getReborns关闭系统返回0；addReborns更新 `characters.reborns`。rebirth_level向下限1夹取，再按职业最高等级夹取；本地缺键为0，因此实际1级。isClearSkill=false保留技能；jobId>0用指定职业，否则 `Job.changeJobByLevel`；骑士团未完成五转标记且目标1112/1212/1312/1412/1512时退前一阶。已有该职业或永久 `cygnus_fifth_job_completed=1` 为完成。addReborns、清经验、setLevel(target-1)、**恰好一次levelUp**，不是从1级重新模拟成长到target。

Job.java:177..510 的条件必须原样理解：冒险家处理的10条二转分支使用8/31/70/120边界；骑士团10/30/70/120。海盗没有对应处理分支，按原端保留当前职业。Aran虽然存在LEGEND规则，但getBaseJobType/isA对2100..2112不匹配LEGEND，故同样落回原职业；不自行补全。飞侠412回1级实际得到初心者0，与脚本文案不同，用户选择源端执行行为。

## 当前实现与边界

GameConfigDao.EnsureLevelCaps在新建/打开旧库时补缺失max_reborn_count=10；保留显式自定义上限。为落实本次“能涅槃”的功能请求，本地缺失use_rebirth_system补true（这是本地启用配置，**不是源端V1.7.0历史默认false**）；现有显式false不覆盖。原rebirth_level缺键→1语义不变。

NpcApi补接当前原脚本实际使用的false,false参数；GetRebirthSettings经传输读取真实配置，NativeRebirthRules迁移Job规则。次数直接使用FullCharacter.record.reborns并随SaveCharacter永久保存，纠正旧executeRebornAsId曾用SavePqExtend每日写入、getReborns却按永久extend读取的错误。没有证据表明旧涅槃入口成功执行过，不猜测补发历史次数。

LevelSystem.ResetForRebirth复用现有一次升级的HP/MP/AP/SP成长，Profile.DeferSaves将职业/等级/次数合并存档。原脚本继续负责奖励和扣费。技能清空/返还SP参数目前仍显式报未迁移，当前false,false路径不受影响；不是所有rebirth重载迁移。成长与转职沿用既有移植内核，未宣称全部原端倍率缓存、装备经验升级/次数扩展等系统已迁移，Equip.getEquipmentMaxLevelUp还应单独审计；脚本文案“最高50”不能替代该方法真实min(...,100)公式。未做Android实机验收。

## 验证与复现

- `dotnet run --project tools/server-check -c Release -- --rebirth`：11通过/0失败，覆盖新旧库补键、自定义上限保留、真实JS配置接口、关闭配置保留、职业边界及源端未处理分支。
- `NativeRebirthChecks.Run`（隔离Unity -batchmode -nographics）：12项全部通过，真实NpcSession/NpcApi执行未修改的原一键转生脚本，9→10、1级/初心者/清经验、两种材料精确扣除、宝石各50、点券扣50000、偷学点+1、characters.reborns重新读档为10、11次拒绝且无扣费。测试使用临时数据库，不修改用户存档。
- 可用完整项目的隔离副本运行Unity `-executeMethod NativeRebirthChecks.Run`；本次为缩短导入，将compile-check产出的AssemblyCSharpCheck.dll放入空项目Assets/Plugins/Editor，复制Assets/Resources及Schema.sql，设置 `MAPLEDASH_IMG_ROOT` 为原资源Data后运行同一入口；Unity退出0。
- compile-check Rebuild：0警告/0错误。脚本包2779条parseFail=0/mangled=0；全量NPC mock基线889条790通过/27runFail/72JavaInterop，与已有基线一致，不冒称全量通过。
- 持久入口为上述两个测试源码；本轮日志摘录见 `docs/skill-regression/rebirth-2026-11-02.txt`。没有APK打包或手机点击验收。
