using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record NpcTalkReading(int EntityId, bool Talking);
internal sealed record CutsceneReading(string Name, bool Playing);
internal sealed record ControlModeReading(string Mode);
internal static class SessionSignalWire
{
    static string Text(ref WireReader r) { int n = r.Var(); if (n > 4096) throw new InvalidDataException("Session string exceeds bounds."); return new UTF8Encoding(false, true).GetString(r.Take(n)).TrimEnd('\0'); }
    internal static (string Kind, object Data)? Read(ref WireReader r, ushort opcode)
    {
        (string, object)? signal = null;
        switch (opcode) {
            case 0x41e2: case 0x43e2:
                ushort result = r.U16(); int npc = unchecked((int)r.U32()); if (opcode == 0x41e2) r.U64();
                if (result == 0 || opcode == 0x43e2) signal = ("npc-talk", new NpcTalkReading(npc, opcode == 0x41e2));
                break;
            case 0x158d:
                r.Byte(); byte type = r.Byte(); string name = Text(ref r); Text(ref r); r.U64(); r.U32();
                int count = r.Var(); if (count > 4096) throw new InvalidDataException("Session target list exceeds bounds."); r.Take(checked(count * 4));
                if (type == 9) signal = ("cutscene", new CutsceneReading(name, true));
                break;
            case 0x35e3:
                r.U16(); string finished = Text(ref r); r.U64(); signal = ("cutscene", new CutsceneReading(finished, false)); break;
            case 0x448d:
                r.Bit(); r.Bit(); r.U32(); r.Bit(); r.Byte(); r.Bit(); r.Bit(); r.Byte();
                for (int i = 0; i < 7; i++) r.Bit();
                byte mode = r.Byte(); if (mode is 1 or 2) signal = ("controls", new ControlModeReading(mode == 1 ? "action" : "classic"));
                break;
            default: throw new InvalidDataException("Unknown session signal opcode.");
        }
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected session signal trailer.");
        return signal;
    }
}

internal sealed class SessionSignalTracker
{
    internal CutsceneReading? Cutscene { get; private set; }
    internal NpcTalkReading? Conversation { get; private set; }
    internal string? ControlMode { get; private set; }
    long cutsceneAt = -1, conversationAt = -1, controlsAt = -1;
    internal bool InCutscene => Cutscene?.Playing == true;
    internal void Clear(long at)
    {
        Cutscene = null; Conversation = null; ControlMode = null;
        cutsceneAt = conversationAt = controlsAt = at;
    }
    internal void Apply(NetworkEvent message, int localPlayer)
    {
        switch (message.Kind) {
            case "cutscene" when message.Timestamp >= cutsceneAt:
                Cutscene = message.Fields.Deserialize<CutsceneReading>(); cutsceneAt = message.Timestamp; break;
            case "npc-talk" when message.Timestamp >= conversationAt:
                Conversation = message.Fields.Deserialize<NpcTalkReading>(); conversationAt = message.Timestamp; break;
            case "controls" when message.Timestamp >= controlsAt:
                ControlMode = message.Fields.Deserialize<ControlModeReading>()?.Mode; controlsAt = message.Timestamp; break;
            case "skill-use" when message.Timestamp >= cutsceneAt:
                if (localPlayer > 0 && message.Fields.TryGetProperty("ActorId", out var caster) && caster.GetInt32() == localPlayer) {
                    if (Cutscene != null) Cutscene = Cutscene with { Playing = false };
                    cutsceneAt = message.Timestamp;
                }
                break;
        }
    }
}
