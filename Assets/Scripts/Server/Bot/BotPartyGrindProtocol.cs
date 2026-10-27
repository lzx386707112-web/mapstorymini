using System.Collections.Generic;
namespace Server.Bot
{
    public enum PartyGrindOperation { Read, Start, Stop }
    public sealed class PartyGrindRequest { public int characterId; public PartyGrindOperation operation; }
    public sealed class PartyGrindResponse
    {
        public bool ok=true, active;
        public long expires;
        public List<int> companions=new List<int>();
    }
    public static class BotPartyGrindProtocol
    {
        public static PartyGrindResponse Handle(BotRuntime runtime,PartyGrindRequest request)
        {
            var actor=runtime.Player(request.characterId); var service=runtime.partyGrind;
            if (actor==null || !actor.online) return new PartyGrindResponse { ok=false };
            bool ok=true;
            if (request.operation==PartyGrindOperation.Start) ok=service.Start(actor);
            else if (request.operation==PartyGrindOperation.Stop) service.Stop(actor.id);
            return new PartyGrindResponse { ok=ok,active=service.IsActive(actor.id),expires=service.Expiry(actor.id),companions=new List<int>(service.Companions(actor.id)) };
        }
    }
}
