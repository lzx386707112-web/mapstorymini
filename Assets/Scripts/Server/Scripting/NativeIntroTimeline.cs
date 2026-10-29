using System;
using System.Collections.Generic;
using MapleDash.Wz;

// CAnimationDisplayer 439E00 / 43A8C4, SHOW_ITEM_GAIN_INCHAT subtype 0x12.
public sealed class NativeIntroTimeline
{
    public sealed class Command
    {
        public int Type,Start,X,Y,MoveX,MoveY,Duration,Z,Field;
        public string Visual,Sound;
        // Shape2D.dll 5140809F: signed 32-bit imul/idiv, truncating toward zero.
        public int Displacement(int distance,long elapsed)
        {
            if(Duration<=0 || elapsed>=Duration)return distance;
            if(elapsed<=0)return 0;
            return unchecked(distance*(int)elapsed)/Duration;
        }
    }
    readonly List<Command> pending;
    readonly long started;
    public IReadOnlyList<Command> Commands {get;}
    public NativeIntroTimeline(ImgNode root,long now,int defaultX=0,int defaultY=0)
    {
        if(root==null)throw new InvalidOperationException("Missing original intro scene");
        var commands=new List<Command>();
        for(int i=0;;i++)
        {
            var node=root.Get(i.ToString());if(node==null)break;
            int type=node.Int("type");
            if(type!=0 && type!=2 && type!=5)throw new NotSupportedException("Unmigrated intro command type "+type+" at "+root.Name+"/"+i);
            commands.Add(new Command {Type=type,Start=node.Int("start"),Visual=node.Text("visual"),Sound=node.Text("sound"),
                X=node.Int("x",defaultX)+node.Int("dx"),Y=node.Int("y",defaultY)+node.Int("dy"),MoveX=node.Int("x1"),MoveY=node.Int("y1"),
                Duration=node.Int("duration"),Z=node.Int("z"),Field=node.Int("field")});
        }
        if(commands.Count==0)throw new InvalidOperationException("Empty original intro scene: "+root.Name);
        Commands=commands.AsReadOnly();pending=new List<Command>(commands);started=now;
    }
    public void Tick(long now,Action<Command> execute)
    {
        foreach(var command in new List<Command>(pending))
        {
            // Original `jle`: equality does not execute the command.
            if(now-started<=command.Start)continue;
            pending.Remove(command);execute(command);
        }
    }
}
