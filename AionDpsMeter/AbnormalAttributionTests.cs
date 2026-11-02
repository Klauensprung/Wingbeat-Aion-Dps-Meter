using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class AbnormalAttributionTests
{
    internal static void Run(Action<bool, string> check)
    {
        AbnormalReading Buff(int slot = 7, long duration = 1000) => new(slot, 1000, duration, 0, 1, 1, 17090000, false, 0, 0, null, null, null);
        var lookup = new AbnormalAttribution(); lookup.Connection("a"); lookup.Add(100, Buff(), 100);
        check(lookup.Find(100, 7, 100)?.Skill == 17090000 && lookup.Find(101, 7, 100) == null &&
            lookup.Find(100, 7, 99) == null && lookup.Find(100, 7, 1100) == null,
            "Buff attribution is scoped by target, UID and captured lifetime");
        lookup.Remove(100, 7, 200); lookup.Add(100, Buff(), 150);
        check(lookup.Find(100, 7, 201) == null, "Stale buff updates cannot resurrect a removed attribution");
        lookup.Add(100, Buff(), 300); lookup.Connection("b");
        check(lookup.Find(100, 7, 301) == null, "Changing game connections clears reusable entity and buff identities");
        for (int i = 0; i <= AbnormalAttribution.Limit; i++) lookup.Add(100, Buff(i, -1), 400);
        check(lookup.Count <= AbnormalAttribution.Limit && lookup.Find(100, 0, 401) == null,
            "Attribution storage stays bounded and drops old identities on overflow");

        byte[] Frame(byte op, byte[] body) => [(byte)(body.Length + 6), op, 0x38, ..body];
        byte[] add = Frame(0x2a, [100, 1, 3, 7, ..BitConverter.GetBytes(1000),
            ..BitConverter.GetBytes(1000L), ..new byte[8], 1, 1, ..BitConverter.GetBytes(17090000), 0]);
        byte[] tick = Frame(5, [100, 2, 1, 7, ..BitConverter.GetBytes(1234), 50]);
        byte[] remove = Frame(0x2c, [100, 1, 0, 7, 0]);
        var decoder = new EuGameDecoder();
        DecodeResult Send(byte[] b, long time) => decoder.Decode(new CapturedPayload(13328, 50000, b) { TimestampMs = time });
        Send(add, 100); var attributed = Send(tick, 200).Events.Single().Damage!;
        check(attributed.SkillCode == 17090000 && attributed.AdditionalData!["AbnormalId"].GetInt32() == 1000,
            "A skill-less periodic tick inherits an explicitly captured buff source for the same caster");
        Send(remove, 300);
        check(Send(tick, 400).Events.Single().Damage!.SkillCode == 0,
            "Periodic damage after removal stays unattributed instead of borrowing an expired skill");
        Send(add, 500); var broken = add[..^1]; broken[0]--;
        Send(broken, 600);
        check(Send(tick, 700).Events.Single().Damage!.SkillCode == 17090000,
            "A rejected buff record does not partially overwrite the last valid attribution");
        byte[] otherCaster = Frame(5, [100, 2, 2, 7, ..BitConverter.GetBytes(1234), 50]);
        check(Send(otherCaster, 701).Events.Single().Damage!.SkillCode == 0,
            "A periodic tick from a different caster does not borrow the buff source skill");
        byte[] explicitSkill = Frame(5, [100, 10, 1, 7, ..BitConverter.GetBytes(1234), 50, ..BitConverter.GetBytes(12000000)]);
        check(Send(explicitSkill, 702).Events.Single().Damage!.SkillCode == 12000000,
            "An explicit periodic source skill takes precedence over the buff lookup");
        byte[] reflectBuff = (byte[])add.Clone(); reflectBuff[3] = 1; Send(reflectBuff, 703);
        byte[] reflected = Frame(5, [100, 66, 1, 7, ..BitConverter.GetBytes(1234), 50, ..BitConverter.GetBytes(13000000)]);
        var reflection = Send(reflected, 704).Events.Single().Damage!;
        check(reflection.SkillCode == 13000000 && reflection.AdditionalData!["AbnormalId"].GetInt32() == 1000,
            "Reflection retains its explicit skill and resolves the buff on the reflecting actor");
    }
}
