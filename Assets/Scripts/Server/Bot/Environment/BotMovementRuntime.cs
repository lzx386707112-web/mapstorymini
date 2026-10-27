using System;
using System.IO;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;
using Edge = Server.Bot.Environment.BotNavigationGraph.Edge;
using EdgeType = Server.Bot.Environment.BotNavigationGraph.EdgeType;

namespace Server.Bot.Environment
{
    // BotMovementManager的50ms执行层；导航/战斗只修改意图，不直接插值坐标。
    public sealed class BotMovementRuntime
    {
        public readonly BotCharacter actor;
        public readonly MapRuntime map;
        public readonly BotGroundIndex ground;
        public readonly BotWalkRegions regions;
        public readonly BotGroundPhysics physics;
        readonly BotGroundMotion groundMotion;
        readonly BotAirCollision collision;
        readonly BotAirMotion airMotion;
        readonly BotRopeMotion ropeMotion;
        readonly BotSwimMotion swimMotion;
        public readonly BotMovementProfile profile;
        public BotGroundPhysics.Travel travel;
        public BotAirMotion.State air;
        public BotRopeMotion.State climb;
        public BotSwimMotion.State swim;
        public MapRope blockedRope, pendingRope;
        public int pendingRopeY, moveDir, brakeDir, velocityX, velocityY, attackCooldownMs;
        public int jumpCooldownMs, unstuckCooldownMs, climbDirection;
        public bool wasMoving, crouching, downJumpPending, climbUpIntent, resting, grinding, preciseTarget;
        public long alertedUntilMs, nextSwimJumpMs;
        public Edge navEdge;
        public Point? navigationTarget;
        public Action<BotCharacter, float> ApplyFallDamage;
        // count+commands，已去除录制包9字节前缀，供同一前端消费器使用。
        public Action<BotCharacter, byte[]> Broadcast;
        public Func<int, bool> IsObserved;
        bool broadcastValid, flashBroadcast;
        (int, int, int, int, int, int) lastBroadcast;
        int lastFoothold;
        public Point Position => new Point((int)actor.x, (int)actor.y);
        public bool InAir => air != null && air.airborne;
        public bool Climbing => climb != null && climb.climbing;
        public bool Swimming => swim != null && swim.swimming;
        public BotMovementRuntime(BotCharacter actor, MapRuntime map, BotMovementProfile profile = null)
        {
            this.actor = actor; this.map = map; this.profile = profile ?? BotMovementProfile.Base;
            ground = new BotGroundIndex(map); regions = new BotWalkRegions(map);
            physics = new BotGroundPhysics(); collision = new BotAirCollision(map, ground);
            groundMotion = new BotGroundMotion(map, ground, regions, collision, physics);
            airMotion = new BotAirMotion(map, collision, physics, this.profile);
            ropeMotion = new BotRopeMotion(ground); swimMotion = new BotSwimMotion(collision, physics, this.profile);
            Reset();
        }
        public void Reset()
        {
            air = null; climb = null; swim = null; pendingRope = null; blockedRope = null;
            travel = new BotGroundPhysics.Travel(actor.x); moveDir = brakeDir = velocityX = velocityY = 0;
            downJumpPending = crouching = climbUpIntent = wasMoving = false;
            attackCooldownMs = jumpCooldownMs = unstuckCooldownMs = 0;
            ClearNavigation(); broadcastValid = false; SyncStance();
        }
        public void ClearNavigation() { navEdge = null; navigationTarget = null; preciseTarget = false; }
        public void Teleport(Point point) { SetPosition(point); Reset(); }
        public static int TickDown(int timer) => Math.Max(0, timer - 50);
        public void Tick(Point? target, long now, bool runAi = true)
        {
            if (actor.mapId != map.mapId) return;
            flashBroadcast = false;
            if (InAir) BotAirMotion.AdvanceTimers(air);
            if (Climbing) TickClimb(target, runAi);
            else if (map.swim && (InAir || Swimming)) TickSwim(target, now);
            else if (InAir) TickAir(target);
            else TickGround(target);
            if (!flashBroadcast) Emit(now);
        }
        public int CalcStep(int targetX, int stopDist = 30, int followDist = 80)
        {
            int dx = targetX - Position.x, distance = Math.Abs(dx);
            if (distance <= stopDist || (!wasMoving && distance <= followDist)) { wasMoving = false; return 0; }
            wasMoving = true;
            int step = Math.Min(distance, physics.WalkStep(profile)) * Math.Sign(dx);
            if (DirectionalDrop) return step;
            int slack = 0;
            if (navEdge != null && (navEdge.type == EdgeType.Jump || navEdge.type == EdgeType.Drop && navEdge.launchStepX == 0)
                && navEdge.ContainsLaunchX(targetX))
                slack = Math.Max(0, Math.Min(physics.WalkStep(profile), dx > 0 ? navEdge.launchMaxX - targetX : targetX - navEdge.launchMinX));
            int approach = physics.Approach(map.footholdSpeed, profile, travel.Speed, dx, slack);
            return approach == Math.Sign(step) ? step : approach;
        }
        bool DirectionalDrop => navEdge != null && navEdge.type == EdgeType.Drop && navEdge.launchStepX != 0;
        public static int PreciseStop(Edge edge) => edge != null && (edge.type == EdgeType.Jump || edge.type == EdgeType.Drop && edge.launchStepX == 0)
            ? 0 : edge != null && edge.type != EdgeType.Walk ? 1 : 4;
        void TickGround(Point? target)
        {
            if (travel.Speed == 0 && BotGroundPhysics.JavaRound(travel.X) != Position.x) travel = new BotGroundPhysics.Travel(Position.x, 0, travel.CarryMs);
            var foothold = ground.Ground(Position.x, Position.y);
            if (foothold == null) { Fall(0); return; }
            if (pendingRope != null)
            {
                var rope = pendingRope; pendingRope = null;
                if (Math.Abs(Position.x - rope.x) <= 8) Attach(rope, pendingRopeY);
                return;
            }
            if (downJumpPending)
            {
                downJumpPending = false; crouching = false;
                if (!foothold.forbidFallDown) Launch(0, false, true);
                return;
            }
            int stop = DirectionalDrop ? 0 : preciseTarget ? PreciseStop(navEdge) : 30;
            int follow = DirectionalDrop ? 0 : navEdge != null || preciseTarget ? stop : 80;
            int step = target.HasValue ? CalcStep(target.Value.x, stop, follow) : 0;
            if (step != 0 && !groundMotion.CanStep(Position, step))
            {
                bool wall = collision.Wall(Position, new Point(Position.x + step, Position.y), true).kind == BotAirCollision.Kind.Wall;
                if (wall || !DirectionalDrop || Math.Sign(step) != Math.Sign(navEdge.launchStepX))
                {
                    if (wall || navEdge != null && navEdge.type == EdgeType.Walk) ClearNavigation();
                    step = 0;
                }
            }
            moveDir = Math.Sign(step);
            int desired = moveDir != 0 ? moveDir : groundMotion.SlipperyStopDirection(Position, foothold, travel, profile);
            bool braking = desired != 0 && travel.Speed * desired < 0;
            var before = Position;
            var motion = groundMotion.Step(before, foothold, desired, travel, profile);
            SetPosition(motion.point);
            if (motion.lostGround) { Fall(motion.stepX); return; }
            travel = new BotGroundPhysics.Travel(physics.SlipScale(map.footholdSpeed, profile) < 1 ? motion.travel.X : motion.point.x,
                motion.travel.Speed, motion.travel.CarryMs);
            air = null; climb = null; crouching = climbUpIntent = false;
            velocityX = motion.velocityX; velocityY = 0;
            bool moved = before.x != motion.point.x;
            brakeDir = braking && moved ? desired : 0;
            if (moved && desired != 0) actor.facing = desired;
            if (motion.stepX == 0 && velocityX == 0 && step == 0) Idle();
        }
        public void Idle()
        {
            air = null; climb = null; crouching = climbUpIntent = downJumpPending = false; pendingRope = null;
            moveDir = brakeDir = velocityX = velocityY = 0;
            travel = new BotGroundPhysics.Travel(Position.x);
            SyncStance();
        }
        public void QueueDownJump() { Idle(); crouching = downJumpPending = true; SyncStance(); }
        public void QueueRopeEntry(MapRope rope, int y) { Idle(); pendingRope = rope; pendingRopeY = y; }
        int ResolveAirX(int dx)
        {
            if (dx != 0) return Math.Sign(dx) * physics.WalkStep(profile);
            return physics.SlipScale(map.footholdSpeed, profile) < 1 && !Climbing
                ? BotGroundPhysics.JavaRound(travel.Speed * physics.TickMs / 8.0) : 0;
        }
        public void Jump(int dx, bool fixedArc = false, bool climbIntent = false)
        {
            blockedRope = null;
            if (map.swim)
            {
                climb = null; air = null; crouching = false; climbUpIntent = false;
                swim = new BotSwimMotion.State { position = Position, x = actor.x, y = actor.y, swimming = true,
                    verticalSpeed = -profile.JumpSpeed(555), facing = actor.facing };
                travel = new BotGroundPhysics.Travel(Position.x); nextSwimJumpMs = BotScheduler.Now() + 500;
                velocityX = 0; velocityY = BotGroundPhysics.JavaRound(swim.verticalSpeed); SyncStance(); return;
            }
            int x = ResolveAirX(dx);
            Launch(x, false, false); air.fixedArc = fixedArc; climbUpIntent = climbIntent;
        }
        public void RopeJump(int dx, bool transfer)
        {
            var source = Climbing ? climb.rope : null;
            int x = ResolveAirX(dx);
            Launch(x, true, false); blockedRope = transfer ? source : null; climbUpIntent = transfer;
        }
        void Launch(int x, bool ropeJump, bool downJump)
        {
            air = airMotion.Launch(Position, x, ropeJump, downJump); air.facing = actor.facing;
            climb = null; swim = null; pendingRope = null; downJumpPending = crouching = climbUpIntent = false;
            moveDir = brakeDir = 0; travel = new BotGroundPhysics.Travel(Position.x);
            velocityX = x * 20; velocityY = BotGroundPhysics.JavaRound(air.velocityY * 20); SyncStance();
        }
        public void Fall(int x)
        { blockedRope = null; Launch(x, false, false); air.velocityY = 0; velocityY = 0; }
        public void Attach(MapRope rope, int y)
        {
            climb = BotRopeMotion.Attach(rope, y); SetPosition(climb.position);
            air = null; swim = null; crouching = climbUpIntent = downJumpPending = false; pendingRope = null;
            velocityX = velocityY = 0; travel = new BotGroundPhysics.Travel(Position.x); SyncStance();
        }
        static bool SameRope(MapRope a, MapRope b) => a != null && b != null && a.x == b.x && a.Top == b.Top && a.Bottom == b.Bottom && a.ladder == b.ladder;
        bool GrabRope()
        {
            if (!climbUpIntent) return false;
            foreach (var rope in map.ropes)
                if (!SameRope(blockedRope, rope) && Math.Abs(rope.x - Position.x) <= 8 && Position.y >= rope.Top && Position.y <= rope.Bottom + 2)
                { Attach(rope, Position.y); return true; }
            return false;
        }
        void TickAir(Point? target)
        {
            if (GrabRope()) return;
            if (moveDir == 0)
            {
                bool steer = !air.fixedArc && air.landingGraceMs == 0 && (navEdge == null ||
                    navEdge.type != EdgeType.Jump && navEdge.type != EdgeType.Drop && !(navEdge.type == EdgeType.Climb && navEdge.launchStepX != 0));
                moveDir = !steer ? Math.Sign(air.velocityX) : target.HasValue && Math.Abs(target.Value.x - Position.x) > 8 ? Math.Sign(target.Value.x - Position.x) : 0;
            }
            air.direction = moveDir;
            var previous = Position;
            var result = airMotion.Step(air); SetPosition(air.Position); actor.facing = air.facing;
            if (result == BotAirCollision.Kind.Land)
            {
                travel = new BotGroundPhysics.Travel(Position.x, air.groundSpeed);
                velocityX = BotGroundPhysics.JavaRound(air.groundSpeed * 50 / 8.0 * 20); velocityY = 0;
                float distance = (float)air.fallDistance; air = null; blockedRope = null;
                climbUpIntent = false; jumpCooldownMs = 0; ApplyFallDamage?.Invoke(actor, distance);
            }
            else
            {
                velocityX = BotGroundPhysics.JavaRound((air.velocityX + air.steerVelocity) * 20);
                velocityY = BotGroundPhysics.JavaRound(air.velocityY * 20);
                if (result != BotAirCollision.Kind.Ceiling && GrabRope()) return;
                if (air != null && air.flashFired) { EmitFlash(previous); air.flashFired = false; }
            }
        }
        void TickClimb(Point? target, bool runAi)
        {
            if (!target.HasValue) { climb.direction = 0; velocityX = velocityY = 0; return; }
            int dy = target.Value.y - Position.y, dx = target.Value.x - climb.rope.x;
            if (runAi && navEdge == null && Math.Abs(dx) > 80 && climb.rope.Bottom < target.Value.y) { RopeJump(dx, false); return; }
            if (navEdge == null && (resting || !grinding && Math.Abs(dy) < 30 && Math.Abs(dx) < 160)) climbDirection = 0;
            else if (preciseTarget && dy != 0 && target.Value.x == climb.rope.x && target.Value.y > climb.rope.Top && target.Value.y <= climb.rope.Bottom && Math.Abs(dy) < 5)
            { Attach(climb.rope, target.Value.y); return; }
            else if (runAi || navEdge != null) climbDirection = Math.Sign(dy);
            climb.direction = climbDirection;
            var result = ropeMotion.Step(climb); SetPosition(climb.position); velocityX = velocityY = 0;
            if (result == BotRopeMotion.Result.Fall) Fall(0);
            else if (result == BotRopeMotion.Result.Land) { climb = null; travel = new BotGroundPhysics.Travel(Position.x); }
        }
        void TickSwim(Point? target, long now)
        {
            if (swim == null) swim = new BotSwimMotion.State { position = Position, facing = actor.facing };
            int previousHold = swim.verticalHold;
            swim.position = Position; swim.direction = swim.verticalHold = 0; swim.jumpRequested = false;
            if (attackCooldownMs == 0)
            {
                if (!target.HasValue) swim.verticalHold = -1;
                else
                {
                    int dx = target.Value.x - Position.x, dy = target.Value.y - Position.y;
                    swim.direction = Math.Abs(dx) > 8 ? Math.Sign(dx) : 0;
                    if (Math.Abs(dx) <= 8 && Math.Abs(dy) <= 30) { swim.direction = 0; swim.verticalHold = -1; }
                    else if (dy <= -100 && now >= nextSwimJumpMs) { swim.jumpRequested = true; nextSwimJumpMs = now + 500; swim.verticalHold = -1; }
                    else swim.verticalHold = dy <= 30 ? -1 : dy > 120 ? 1 : previousHold > 0 ? 1 : 0;
                }
            }
            bool landed = swimMotion.Step(swim); SetPosition(swim.position); actor.facing = swim.facing;
            velocityX = BotGroundPhysics.JavaRound(swim.horizontalSpeed); velocityY = BotGroundPhysics.JavaRound(swim.verticalSpeed);
            if (landed) { travel = new BotGroundPhysics.Travel(Position.x, swim.groundSpeed); air = null; }
        }
        void SetPosition(Point point) { actor.x = point.x; actor.y = point.y; }
        public int SyncStance()
        {
            int stance = Climbing ? climb.rope.ladder ? 14 : 16 : Swimming ? actor.facing >= 0 ? 12 : 13 :
                crouching ? actor.facing >= 0 ? 10 : 11 : InAir ? actor.facing >= 0 ? 6 : 7 :
                moveDir != 0 ? moveDir > 0 ? 2 : 3 : brakeDir != 0 ? brakeDir > 0 ? 2 : 3 : actor.facing >= 0 ? 4 : 5;
            actor.stance = stance; actor.moving = velocityX != 0 || velocityY != 0;
            return stance;
        }
        public void Publish(long now) => Emit(now);
        void Emit(long now)
        {
            int stance = SyncStance();
            if (now < alertedUntilMs && (stance == 4 || stance == 5)) stance += 4;
            if (IsObserved != null && !IsObserved(map.mapId)) { broadcastValid = false; return; }
            var fh = ground.Ground(Position.x, Position.y); if (fh != null) lastFoothold = fh.id;
            var snapshot = (Position.x, Position.y, velocityX, velocityY, stance, lastFoothold);
            if (broadcastValid && lastBroadcast.Equals(snapshot)) return;
            broadcastValid = true; lastBroadcast = snapshot;
            var command = new BotMovementRecording.Command(0, unchecked((short)actor.x), unchecked((short)actor.y),
                unchecked((short)velocityX), unchecked((short)velocityY), unchecked((short)lastFoothold), (sbyte)stance, 50);
            byte[] packet = BotMovementPacket.Reconstruct(new[] { command });
            var output = new byte[packet.Length - 9]; Array.Copy(packet, 9, output, 0, output.Length);
            Broadcast?.Invoke(actor, output);
        }
        void EmitFlash(Point previous)
        {
            if (IsObserved != null && !IsObserved(map.mapId)) return;
            var fh = ground.Ground(Position.x, Position.y); if (fh != null) lastFoothold = fh.id;
            flashBroadcast = true;
            // 源端二段跳为绝对NORMAL后追加relative6，不用重建器的固定宽度布局。
            using var bytes = new MemoryStream(); using var w = new BinaryWriter(bytes);
            w.Write((byte)2); w.Write((byte)0); w.Write(unchecked((short)actor.x)); w.Write(unchecked((short)actor.y));
            w.Write(unchecked((short)velocityX)); w.Write(unchecked((short)velocityY)); w.Write(unchecked((short)lastFoothold));
            w.Write((byte)SyncStance()); w.Write((short)50); w.Write((byte)6);
            w.Write(unchecked((short)(Position.x - previous.x))); w.Write(unchecked((short)(Position.y - previous.y)));
            w.Write((byte)actor.stance); w.Write((short)0); Broadcast?.Invoke(actor, bytes.ToArray());
            broadcastValid = true; lastBroadcast = (Position.x, Position.y, velocityX, velocityY, actor.stance, lastFoothold);
        }
    }
}
