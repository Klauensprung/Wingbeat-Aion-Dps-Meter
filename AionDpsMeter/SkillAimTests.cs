using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SkillAimTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-skill-aim.jsonl"));
        bool preserved = true, rejected = true;
        foreach (ushort opcode in new ushort[] { 0x0238, 0x0938, 0x1c38 })
        foreach (bool location in new[] { false, true })
        foreach (float yaw in new[] { -180f, 0f, 90.5f, 360f }) {
            byte[] Body(float heading) {
                var b = new List<byte> { 1, 0 };
                b.AddRange(BitConverter.GetBytes(17010001)); b.Add(3);
                b.Add(location ? (byte)2 : (byte)0); b.Add(100);
                b.AddRange(BitConverter.GetBytes(heading));
                if (location) foreach (float value in new[] { 123.5f, -456.25f, 78f }) b.AddRange(BitConverter.GetBytes(value));
                if (opcode == 0x0238) b.AddRange(new byte[] { 1, 0, 10 });
                else if (opcode == 0x0938) b.AddRange(new byte[] { 10, 0 });
                else b.Add(0);
                return b.ToArray();
            }
            CapturedPayload Packet(byte[] body) => new(13328, 50000,
                [(byte)(body.Length + 6), (byte)(opcode >> 8), (byte)opcode, ..body]) { TimestampMs = 1000 };
            var packet = Packet(Body(yaw)); fixtures.Add(packet);
            var events = new EuGameDecoder().Decode(packet).Events;
            var fields = events.First(e => e.Network != null).Network!.Fields;
            var aim = fields.GetProperty("Aim").Deserialize<SkillAim>();
            preserved &= aim == new SkillAim(yaw, location ? 123.5f : null, location ? -456.25f : null, location ? 78f : null);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                rejected &= new EuGameDecoder().Decode(Packet(Body(invalid))).Events.Count == 0;
        }
        check(preserved, "Skill, cast and charge packets retain signed facing and optional target coordinates");
        check(rejected, "Non-finite skill, cast and charge facing is rejected atomically");
    }
}
