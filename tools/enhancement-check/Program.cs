using System;
using System.Collections.Generic;
using System.IO;

static class Program
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { checks++; return; }
        throw new Exception(message);
    }
    struct Pixel { public int r,g,b,a; }
    // The star canvases are small solid icons; read the most opaque pixel.
    static Pixel StarPixel(MapleDash.Wz.ImgNode node)
    {
        if(node==null||node.Kind!="Canvas")return new Pixel {r=-1,g=-1,b=-1,a=-1};
        var rgba=node.Rgba();int best=0,bestAlpha=-1;
        for(int i=3;i<rgba.Length;i+=4)if(rgba[i]>bestAlpha){bestAlpha=rgba[i];best=i-3;}
        return new Pixel {r=rgba[best],g=rgba[best+1],b=rgba[best+2],a=rgba[best+3]};
    }
    static bool Near(Pixel pixel,int r,int g,int b)
        =>Math.Abs(pixel.r-r)<=17&&Math.Abs(pixel.g-g)<=17&&Math.Abs(pixel.b-b)<=17&&pixel.a>=250;
    static void Main()
    {
        string enchantPath=Path.GetFullPath("../MapleDashResources/Data/UI/Enchant.img");
        using(var image=new MapleDash.Wz.ImgFile(enchantPath))
        using(var sounds=new MapleDash.Wz.ImgFile(Path.GetFullPath("../MapleDashResources/Data/Sound/Enchant.img")))
        {
            int audio=0;
            void Walk(MapleDash.Wz.ImgNode node)
            {
                if(node.Kind=="Sound_DX8"||node.Kind=="Sound")
                {
                    Check(node.SoundData!=null&&node.SoundData.Length>0&&node.SoundHeader!=null,
                        "Imported enhancement sound retains its original header and payload");
                    audio++;
                }
                if(node.Value is string reference&&reference.StartsWith("Sound/Enchant.img/",StringComparison.Ordinal))
                {
                    var sound=sounds.Root.Get(reference.Substring("Sound/Enchant.img/".Length))?.Resolve();
                    Check(sound!=null&&sound.SoundData!=null&&sound.SoundData.Length>0,
                        "Enhancement UI sound reference resolves to imported audio payload");
                    audio++;
                }
                foreach(var child in node.Children)Walk(child);
            }
            Walk(image.Root);
            Check(audio>0,"Original enhancement sound nodes parsed by runtime IMG reader");
        }
        // Star row geometry/colour the Android window and tooltip both hardcode.
        using(var ui=new MapleDash.Wz.ImgFile(Path.GetFullPath("../MapleDashResources/Data/UI/Enchant.img")))
        {
            var star=ui.Root.Get("starForce/summaryStar");
            foreach(var name in new[]{"normal","blue","empty"})
            {
                var node=star?.Get(name)?.TryResolve();
                Check(node!=null&&node.Width==11&&node.Height==10,
                    "282 summaryStar/"+name+" keeps the original 11x10 canvas");
                var origin=node?.Get("origin");
                Check(origin!=null&&origin.X==5&&origin.Y==5,"282 summaryStar icon anchor stays centred");
            }
            // 11px step with a 10px gap after every fifth star, 5px between rows.
            Check(star!=null&&star.Int("space/X1")==0&&star.Int("space/X5")==10&&star.Int("space/Y")==5,
                "282 summaryStar spacing is X1=0 X5=10 Y=5");
            var summary=ui.Root.Get("starForce/vector:summary");
            Check(summary!=null&&summary.X==209&&summary.Y==260,"Star row stays anchored on vector:summary (209,260)");
            var gold=StarPixel(star?.Get("normal")?.TryResolve());
            var blue=StarPixel(star?.Get("blue")?.TryResolve());
            var dim=StarPixel(star?.Get("empty")?.TryResolve());
            // Import re-encodes every canvas as ARGB4444 (4-bit channels), so compare within one nibble.
            Check(Near(gold,255,216,0),"Gold star keeps the 282 #FFD800 tone");
            Check(Near(blue,117,230,254),"Superior star keeps the 282 #75E6FE tone");
            Check(dim.a>150&&dim.a<200&&Math.Abs(dim.r-29)<=17&&Math.Abs(dim.g-35)<=17&&Math.Abs(dim.b-42)<=17,
                "Unlit star keeps the dim 282 tone");
        }
        var defenses=new MobDefenseCatalog(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"MobDefense.json")));
        var mobStats=new Dictionary<string,object>{{"maxHP",8L}};
        defenses.Apply(100100,mobStats);
        Check(Convert.ToInt32(mobStats["PDRate"])==10&&Convert.ToInt32(mobStats["MDRate"])==10,
            "282 snail defense comes from packed client template");
        defenses.Apply(2400040,mobStats);
        Check(Convert.ToInt32(mobStats["PDRate"])==10&&Convert.ToInt32(mobStats["MDRate"])==10,
            "Missing legacy template uses user-approved ten percent");
        defenses.Apply(8820008,mobStats);
        Check(Convert.ToInt32(mobStats["PDRate"])==0&&Convert.ToInt32(mobStats["MDRate"])==0,
            "Present template with missing fields uses approved zero default");
        Check(Convert.ToInt64(mobStats["maxHP"])==8,"Defense import preserves other monster attributes");
        Check(Math.Abs(PercentageDefense.Multiplier(10,0)-.9)<1e-12,"Ten percent defense retains ninety percent damage");
        Check(PercentageDefense.Multiplier(300,60)==0,"High boss defense can fully mitigate without enough IED");
        Check(Math.Abs(PercentageDefense.Multiplier(300,80)-.4)<1e-12,"IED applies to defense rather than damage");
        Check(PercentageDefense.Multiplier(300,100)==1&&PercentageDefense.Multiplier(300,120)==1,
            "Additive IED cannot make defense negative or increase damage above unmitigated damage");
        var rules = new StarForceRules(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StarForce.json")));
        foreach (bool superior in new[] {false, true})
            for (int star = 0; star < (superior ? 15 : 30); star++)
            {
                var quote = rules.Preview(1302000, 150, star, 0, superior);
                int success = 0, destroy = 0;
                for (int draw = 0; draw < 10000; draw++)
                {
                    var result = rules.Roll(star, 0, superior, quote, draw);
                    if (result.outcome == StarForceRules.Outcome.Success)
                    { success++; Check(result.stars == star + 1 && result.dropStreak == 0, "Success state"); }
                    if (result.outcome == StarForceRules.Outcome.Destroy)
                    { destroy++; Check(result.vestige && result.stars == (superior ? 0 : star), "Vestige state"); }
                }
                Check(success == quote.success && destroy == quote.destroy, "Exact per-10000 outcome counts");
                var chance = rules.Preview(1302000, 150, star, 2, superior);
                Check(chance.success == 10000 && chance.destroy == 0 && rules.Roll(star, 2, superior, chance, 9999).stars == star + 1, "Chance time must guarantee success");
            }
        var s22 = rules.Preview(1302000, 150, 22, 0, false);
        Check(s22.success == 300 && s22.destroy == 5820, "22-star source rates");
        Check(rules.Roll(22, 1, false, s22, 9999).dropStreak == 2, "Second consecutive drop enters chance time");
        Check(rules.Roll(20, 1, false, rules.Preview(1302000,150,20,1,false),9999).dropStreak == 0, "Non-drop failure resets consecutive drop streak");
        Check(rules.Preview(1302000, 150, 0, 0, true).success == 5000, "Superior uses its own rate table");
        Check(rules.Cost(1302000, 150, 14, false) == 67400800, "14-star source cost");
        Check(rules.Cost(1302000, 150, 15, false) == 30087200, "15-star cost falls as source formula specifies");
        Check(rules.Cost(1302000, 150, 1, true) == 55833200, "Superior cost");
        Check(rules.Cost(1122444,150,1,false)==0, "Free necklace");
        var levels = new[] {0,94,95,107,108,117,118,127,128,137,138,200};
        var normalCaps = new[] {5,5,8,8,10,10,15,15,20,20,30,30};
        var superiorCaps = new[] {3,3,5,5,8,8,10,10,12,12,15,15};
        for (int i=0;i<levels.Length;i++)
            foreach (bool sup in new[] {false,true})
            {
                int cap=(sup?superiorCaps:normalCaps)[i], level=levels[i];
                Check(rules.Cap(1302000,level,sup)==cap,"Verified client level bracket");
                Check(rules.Roll(cap-1,0,sup,rules.Preview(1302000,level,cap-1,0,sup),0).stars==cap,
                    "Final legal upgrade reaches level cap");
                Reject(()=>rules.Preview(1302000,level,cap,0,sup),"Level cap blocks next attempt");
            }
        Check(rules.Cap(1072870,160,false)==30 && rules.Cap(1182060,75,false)==5,
            "Unverified server Sweetwater and badge caps do not override client");
        Check(rules.Cap(1092123,1,false)==15 && rules.Cap(1092124,1,false)==20 && rules.Cap(1092125,1,false)==30,
            "Client special equipment triple index overrides level cap");
        Check(rules.Cap(1342121,1,false)==30 && rules.Cap(1354310,1,false)==30 && rules.Cap(1342125,1,false)==30,
            "All three forced-thirty equipment groups");
        Check(rules.Cap(1092113,150,false)==26 && rules.Cap(1342090,150,false)==27 && rules.Cap(1354309,150,false)==27,
            "Sequential exact-ID cap overrides");
        Check(rules.Cap(1302376,200,false)==25 && rules.Cap(1542151,200,false)==25 && rules.Cap(1403054,200,false)==25,
            "Destiny EXE and InstanceTableWZ2 item lists both enforce twenty-five");
        Reject(()=>rules.Preview(1302000,150,14,0,false,true), "Safeguard rejected outside 15..17");
        Reject(()=>rules.Preview(1302000,150,14,0,true,true), "Superior safeguard rejected");
        Reject(()=>rules.Preview(1092113,150,26,0,false), "Item cap enforced");
        var protect=rules.Preview(1302000,150,15,0,false,true);
        var scroll=rules.Preview(1302000,150,15,0,false,false,true);
        Check(protect.destroy==0 && protect.cost==rules.Cost(1302000,150,15,false)*3, "UI safeguard tripled cost");
        Check(scroll.destroy==0 && scroll.cost==rules.Cost(1302000,150,15,false), "Existing protection scroll does not triple cost");
        Check(rules.Preview(1302000,150,15,0,false,false,false,2).cost==27078480, "VIP discount included in shared quote");
        var stats=new Dictionary<string,int> {{"str",10},{"watk",100},{"wdef",20},{"hp",100}};
        var fifteen=StarForceStats.Calculate(1302000,150,15,false,stats);
        Check(fifteen["str"]==40 && fifteen["watk"]==30 && fifteen["wdef"]==15 && fifteen["hp"]==255, "15-star derived totals");
        var sixteen=StarForceStats.Calculate(1302000,150,16,false,stats);
        Check(sixteen["str"]==51 && sixteen["watk"]==38, "16th star uses separate level bracket");
        Check(stats["watk"]==100 && stats["str"]==10, "Base stats never mutated");
        var tyrant=StarForceStats.Calculate(1102481,150,15,true,stats);
        Check(tyrant["str"]==115 && tyrant["watk"]==150, "Superior independent stat progression");
        Check(StarForceStats.Calculate(1302000,150,15,false,stats,false).Count==0, "Ineligible equipment derives no stats");
        var potential = new PotentialCatalog(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ItemOption.json")));
        Check(PotentialStats.Calculate(potential,150,new[]{401,401})["IgnoreDefense"]==6,
            "User-approved server IED adds each option independently");
        Check(PotentialStats.Calculate(potential,150,new[]{32091},8)["STR"]==0
            &&PotentialStats.Calculate(potential,150,new[]{32091},9)["STR"]==1
            &&PotentialStats.Calculate(potential,150,new[]{42091},179)["STR"]==38,
            "Character-level potential uses complete nine-level groups independently from equipment-level bucket");
        Check(potential.Count==743, "All client potential entries available");
        Check(PotentialCatalog.OptionLevel(0)==1 && PotentialCatalog.OptionLevel(1)==0 && PotentialCatalog.OptionLevel(11)==1 && PotentialCatalog.OptionLevel(12)==2 && PotentialCatalog.OptionLevel(150)==15, "Source option-level boundaries");
        Check(potential.ValuesAt(10001,15)!=null && potential.Describe(10001,15).Length>0, "Level data and localized description available");
        var original=potential.ValuesAt(10001,15); var again=potential.ValuesAt(10001,15);
        original["incSTR"]=9999;
        Check(Convert.ToInt32(again["incSTR"])!=9999, "Potential callers cannot mutate catalog values");
        Reject(()=>potential.Describe(999999,15), "Unknown potential rejected");
        var cubeJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"CubeRates.json"));
        var cubes=new CubeRateTable(cubeJson);
        Check(cubes.BuiltinTierUp("SOLID_CUBE_TIER_UP_RATES",3)==25,"Use numeric source arrays instead of contradictory comments");
        Check(cubes.TierUpOverride(2710007,1)==-1 && cubes.PrimeOverride(2710007,1,1)==-1,"Disabled TSV rates must not override builtins");
        var enabled=new CubeRateTable(cubeJson.Replace("\"value\": \"false\"","\"value\": \"true\""));
        Check(enabled.TierUpOverride(2710007,1)>=0 && enabled.PrimeOverride(2710007,1,1)==10000,"Enabled TSV schema read correctly");
        var forced=cubes.OptionWeights(5062523,4,new[]{40656,40001});
        Check(forced!=null && forced.Total==1 && forced.Pick(0)==40656,"Active forced legal option weights");
        Check(cubes.OptionWeights(5062523,4,new[]{40001})==null,"Illegal forced option never enters pool");
        var overlap=cubes.OptionWeights(2710007,1,new[]{10001,10002,10003,10004,10005,10006,10013,10041,10042,10043,10044,10045,10046,10053,10081});
        Check(overlap!=null && overlap.WeightOf(10001)==600 && overlap.WeightOf(10005)==1000,"Category selected by overlap with legal options");
        Check(cubes.OptionWeights(2710007,1,new[]{10001})==null,"Category below half legal overlap falls back");
        var weighted=new CubeRateTable.Weighted("10001:2,10002:0,10003:3");
        Check(weighted.Total==5 && weighted.Count==2 && weighted.Pick(0)==10001 && weighted.Pick(1)==10001 && weighted.Pick(2)==10003 && weighted.Pick(4)==10003,"Exact weighted boundaries and zero removal");
        Reject(()=>weighted.Pick(5),"Out-of-range weighted draw rejected");
        Reject(()=>new CubeRateTable.Weighted("10001:-1"),"Negative option weight rejected");
        Check(CubeRules.GetFamily(2711024).choice && CubeRules.GetFamily(2711024).karma && !CubeRules.GetFamily(2711024).bonus,"Karma main choice cube family");
        Check(CubeRules.GetFamily(5062508).minimum==2 && CubeRules.GetFamily(5062508).bonus,"Bonus epic minimum");
        Check(CubeRules.GetFamily(5062523)==null,"Special cube not silently treated as ordinary");
        Check(CubeRules.AppraisalCost(30,2711000)==0 && CubeRules.AppraisalCost(31,2711000)==400 && CubeRules.AppraisalCost(70,2711000)==2400 && CubeRules.AppraisalCost(71,2711000)==12600 && CubeRules.AppraisalCost(120,2711000)==36000 && CubeRules.AppraisalCost(121,2711000)==292800,"Appraisal level boundaries and hundred truncation");
        Check(CubeRules.AppraisalCost(150,5062030)==0 && CubeRules.AppraisalCost(150,5062031)==450000,"Cash main free; bonus still appraises");
        Check(CubeRules.AppraisalCost(70,2730000,60)==0 && CubeRules.AppraisalCost(71,2730000,60)>0 && CubeRules.AppraisalCost(120,2730000,90)==0,"Insight free appraisal thresholds");
        Reject(()=>CubeRules.AppraisalCost(150,5062523),"Unknown special cube cannot receive ordinary price");
        var pools=new PotentialPool(potential);
        var effects=PotentialStats.Calculate(potential,150,new[]{10070,601,391,40656,301,0,-1});
        Check(effects["Damage"]==3&&effects["BossDamage"]==3&&!effects.ContainsKey("FinalDamage"),"Potential damage separates boss and general without final damage duplication");
        Check(effects["STRPct"]==5&&effects["DEXPct"]==5&&effects["INTPct"]==5&&effects["LUKPct"]==5,"All-stat option preserves all four named fields");
        Check(effects["DropRate"]==20&&effects["CriticalChance"]==5,"Potential drop and critical units remain percentage points");
        var at152=PotentialStats.Calculate(potential,152,new[]{10070});
        Check(at152["Damage"]==Convert.ToInt32(potential.ValuesAt(10070,PotentialCatalog.OptionLevel(152))["incDAMr"]),"Potential effect uses the same option-level bucket as its description");
        Check(PotentialPool.GradeOf(60001)==-1 && PotentialPool.GradeOf(70001)==-1 && PotentialPool.GradeOf(10001)==1,"Source grade mapping excludes unmapped special option ranges");
        Check(!PotentialPool.SourceAllows(20001)&&PotentialPool.SourceAllows(12047)&&!PotentialPool.SourceAllows(10047),"Source flat-stat and duplicate filters");
        var weaponPool=pools.Legal(1302000,150,4,false,true);
        Check(weaponPool.Contains(40601)&&!weaponPool.Contains(40556),"Weapon permits boss damage and excludes hat cooldown");
        Check(pools.Legal(1002000,150,4,false,true).Contains(40556),"Hat permits cooldown option");
        var roller=new PotentialRoller(potential,cubes);
        for(int i=0;i<100;i++)
        {
            var hidden=new[]{-3,-3,0};
            var revealed=roller.Reveal(1302000,150,3,false,hidden,new int[3],new Random(i));
            Check(revealed.grade==3&&PotentialPool.GradeOf(revealed.options[0])==3
                &&revealed.options[1]>0&&revealed.options[2]==0&&hidden[0]==-3,
                "System reveal preserves grade, absent lines and input snapshot");
            var partial=roller.Reveal(1302000,150,3,false,new[]{30041,-3,30042},new int[3],new Random(i));
            Check(partial.options[0]==30041&&partial.options[1]>0&&partial.options[2]==30042,
                "System reveal leaves already visible options intact");
            var revealedBonus=roller.Reveal(1302000,150,2,true,new[]{30041,20041,0},new[]{-2,0,0},new Random(i));
            Check(PotentialPool.IsBonus(revealedBonus.options[0])&&PotentialPool.GradeOf(revealedBonus.options[0])==2
                &&revealedBonus.options[1]==0&&revealedBonus.options[2]==0,"System reveal supports single bonus line");
            var roll=roller.Roll(5062030,1302000,150,3,new[]{30041,20041,0},new int[3],new Random(i));
            Check(roll.grade>=3&&roll.grade<=4&&roll.options[0]>0&&roll.options[1]>0&&roll.options[2]==0,"Main cube preserves two-line count");
            var add=roller.Roll(5062031,1302000,150,2,new[]{10001,10002,0},new[]{22041,0,0},new Random(i));
            Check(add.options[0]>0&&PotentialPool.IsBonus(add.options[0])&&add.options[1]==0,"Bonus cube preserves one-line count and bonus pool");
        }
        Console.WriteLine($"PASS: {checks} checks; all 450,000 star-force draw outcomes enumerated");
    }
}
