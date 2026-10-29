using System;
using System.Collections.Generic;
using System.IO;
using Server.Field;

namespace Server.Bot.Environment
{
    // 源端逐包执行后等待相邻timestamp差值。主线程版本用BotTasks续接。
    public sealed class BotMovementPlayback
    {
        static readonly Dictionary<int, BotMovementPlayback> active = new Dictionary<int, BotMovementPlayback>();
        public static Action<BotCharacter, byte[]> Broadcast;
        readonly BotCharacter actor;
        readonly int mapId;
        readonly IReadOnlyList<BotMovementRecording.Packet> frames;
        readonly bool offset;
        readonly Action<bool> finished;
        readonly Func<BotCharacter, bool> midCheck;
        readonly BotNavigationGraph.Point? stopPoint;
        BotTasks.Handle pending;
        int index;
        bool done, interrupted;
        BotMovementPlayback(BotCharacter actor, IReadOnlyList<BotMovementRecording.Packet> frames,
            bool offset, Action<bool> finished, Func<BotCharacter, bool> midCheck, BotNavigationGraph.Point? stopPoint)
        { this.actor = actor; mapId = actor.mapId; this.frames = frames; this.offset = offset; this.finished = finished; this.midCheck = midCheck; this.stopPoint = stopPoint; }
        public static BotMovementPlayback Play(BotCharacter actor, IReadOnlyList<BotMovementRecording.Packet> frames,
            bool offset = false, Action<bool> finished = null, Func<BotCharacter, bool> midCheck = null, BotNavigationGraph.Point? stopPoint = null)
        {
            Cancel(actor);
            var playback = new BotMovementPlayback(actor, frames, offset, finished, midCheck, stopPoint);
            active[actor.id] = playback;
            playback.Step();
            return playback;
        }
        public static void Cancel(BotCharacter actor)
        { if (actor != null && active.TryGetValue(actor.id, out var p)) p.Complete(true, false); }
        public void Interrupt() { interrupted = true; }
        void Complete(bool stopped, bool notify = true)
        {
            if (done) return;
            done = true; pending?.Cancel();
            if (active.TryGetValue(actor.id, out var current) && ReferenceEquals(current, this)) active.Remove(actor.id);
            if (notify) finished?.Invoke(stopped);
        }
        void Step()
        {
            if (done) return;
            if (!ReferenceEquals(BotRegistry.Get(actor.id), actor) || actor.mapId != mapId)
            { Complete(true); return; }
            if (interrupted) { StopPacket(); Complete(true); return; }
            if (index >= frames.Count) { Complete(false); return; }
            try
            {
                var frame = frames[index];
                byte[] bytes = frame.data;
                if (offset)
                {
                    var map = MapService.Get(mapId);
                    // 地形索引按图共享：以前每播一帧都新建一次 BotGroundIndex（1203 foothold 的图上 1ms+），
                    // 出生编排是逐帧播的，等于把建卡成本乘了帧数。见 MapGeometry.cs。
                    var fh = map == null ? null : MapGeometryCache.Get(map).ground.Below((int)actor.x, (int)actor.y);
                    bytes = BotMovementPacket.Offset(bytes, (int)actor.x, (int)actor.y, fh == null ? 0 : fh.id);
                }
                var broadcast = BotMovementPacket.Apply(bytes, actor);
                Broadcast?.Invoke(actor, broadcast);
                if (stopPoint.HasValue)
                {
                    // 检查原始录制坐标，而不是offset后的包，保持原端顺序。
                    foreach (var command in BotMovementPacket.Deconstruct(frame.data))
                    {
                        long dx = command.x - stopPoint.Value.x, dy = command.y - stopPoint.Value.y;
                        if (dx * dx + dy * dy <= 80L * 80L) { StopPacket(); Complete(false); return; }
                    }
                }
                if (midCheck != null && index > 0 && index % 5 == 0 && midCheck(actor)) { Complete(true); return; }
                index++;
                if (index == frames.Count) { Complete(false); return; }
                long delay = frames[index].timestamp - frame.timestamp;
                if (delay < 0) throw new InvalidDataException("Movement timestamps out of order");
                pending = BotTasks.Schedule(Step, delay);
            }
            catch { Complete(true); throw; }
        }
        void StopPacket()
        {
            var command = new BotMovementRecording.Command(0, unchecked((short)actor.x), unchecked((short)actor.y),
                0, 0, 0, (sbyte)((actor.stance & 1) == 0 ? 4 : 5), 250);
            var broadcast = BotMovementPacket.Apply(BotMovementPacket.Reconstruct(new[] { command, command }), actor);
            Broadcast?.Invoke(actor, broadcast);
        }
    }
}
