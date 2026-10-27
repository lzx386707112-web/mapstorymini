using System;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    public sealed class BotGroundMotion
    {
        readonly MapRuntime map;
        readonly BotGroundIndex ground;
        readonly BotWalkRegions regions;
        readonly BotAirCollision collision;
        readonly BotGroundPhysics physics;
        public BotGroundMotion(MapRuntime map, BotGroundIndex ground, BotWalkRegions regions, BotAirCollision collision, BotGroundPhysics physics)
        { this.map = map; this.ground = ground; this.regions = regions; this.collision = collision; this.physics = physics; }
        public readonly struct Result
        {
            public readonly Point point;
            public readonly FootholdSegment foothold;
            public readonly BotGroundPhysics.Travel travel;
            public readonly int stepX, velocityX;
            public readonly bool lostGround;
            public Result(Point point, FootholdSegment foothold, BotGroundPhysics.Travel travel, int stepX, int velocityX, bool lostGround)
            { this.point = point; this.foothold = foothold; this.travel = travel; this.stepX = stepX; this.velocityX = velocityX; this.lostGround = lostGround; }
        }
        public Result Step(Point position, FootholdSegment foothold, int direction, BotGroundPhysics.Travel travel, BotMovementProfile profile)
        {
            if (foothold == null) return new Result(position, null, travel, 0, 0, true);
            var displaced = physics.Advance(travel, direction, foothold.Wall ? 0 : foothold.Slope, map.footholdSpeed, profile);
            return Preview(position, foothold, travel, displaced);
        }
        public bool CanStep(Point position, int stepX)
        {
            var foothold = ground.Ground(position.x, position.y);
            if (foothold == null) return false;
            var result = Preview(position, foothold, new BotGroundPhysics.Travel(position.x), new BotGroundPhysics.Travel(position.x + stepX));
            return !result.lostGround && result.point.x == position.x + stepX;
        }
        Result Preview(Point position, FootholdSegment foothold, BotGroundPhysics.Travel travel, BotGroundPhysics.Travel displaced)
        {
            int x = BotGroundPhysics.JavaRound(displaced.X), stepX = x - position.x;
            bool constrain = regions != null && regions.byFoothold.ContainsKey(foothold.id);
            Point? standing = constrain ? regions.GroundSample(foothold, position.x, position.y)?.point : null;
            if (!standing.HasValue) standing = ground.GroundPoint(position.x, position.y);
            int baseY = standing.HasValue && Math.Abs(standing.Value.y - position.y) <= 26 ? standing.Value.y : position.y;
            if (collision.Wall(position, new Point(x, baseY), true).kind == BotAirCollision.Kind.Wall)
                return new Result(position, foothold, new BotGroundPhysics.Travel(position.x), 0, 0, false);
            Point? snapped;
            FootholdSegment snappedFoothold;
            if (constrain)
            {
                var sample = regions.GroundSample(foothold, x, baseY);
                snapped = sample?.point; snappedFoothold = sample?.foothold;
            }
            else
            {
                snapped = ground.GroundPoint(x, Math.Max(position.y, baseY + 1));
                snappedFoothold = snapped.HasValue ? ground.Below(x, snapped.Value.y + 1) : null;
            }
            int velocity = BotGroundPhysics.JavaRound((displaced.X - position.x) * 1000 / physics.TickMs);
            bool lost = !snapped.HasValue || snapped.Value.y > baseY + 16;
            return new Result(lost ? new Point(x, baseY) : snapped.Value, lost ? foothold : snappedFoothold ?? foothold,
                displaced, stepX, velocity, lost);
        }
        public int SlipperyStopDirection(Point position, FootholdSegment foothold, BotGroundPhysics.Travel travel, BotMovementProfile profile)
        {
            int brake = physics.CounterStrafe(map.footholdSpeed, profile, travel.Speed);
            if (brake == 0) return 0;
            for (int i = 0; i < 240 && Math.Abs(travel.Speed) > 0; i++)
            {
                var step = Step(position, foothold, 0, travel, profile);
                if (step.lostGround) return brake;
                position = step.point; foothold = step.foothold; travel = step.travel;
            }
            return 0;
        }
    }
}
