namespace AionDpsMeter.Local;

// Only per-window choices are persisted here; capture and other overlays remain shared.
public sealed class SecondMeterSettings
{
    public bool Enabled { get; set; }
    public bool PartyHealth { get; set; } = true;
    public bool Healing { get; set; }
    public bool ShowSkills { get; set; }
    public bool ShowTargetHealth { get; set; } = true;
    public bool Compact { get; set; } = true;
    public bool Locked { get; set; }
    public bool PinLocal { get; set; }
    public bool CustomBars { get; set; }
    public bool AllActors { get; set; }
    public bool BossesOnly { get; set; }
    public bool FollowTarget { get; set; }
    public string Scope { get; set; } = "Auto";
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 420;
    internal Preferences Create() => new() {
        OverlayPartyHealth=PartyHealth,OverlayHealing=Healing,OverlayShowSkills=ShowSkills,
        OverlayShowTargetHealth=ShowTargetHealth,OverlayCompact=Compact,OverlayLocked=Locked,
        OverlayPinLocal=PinLocal,OverlayCustomBars=CustomBars,OverlayAllActors=AllActors,
        OverlayBossesOnly=BossesOnly,OverlayFollowGameTarget=FollowTarget,OverlayScope=Scope,
        OverlayLeft=Left,OverlayTop=Top,OverlayWidth=Width,OverlayHeight=Height
    };
    internal void Capture(Preferences p) {
        PartyHealth=p.OverlayPartyHealth;Healing=p.OverlayHealing;ShowSkills=p.OverlayShowSkills;
        ShowTargetHealth=p.OverlayShowTargetHealth;Compact=p.OverlayCompact;Locked=p.OverlayLocked;
        PinLocal=p.OverlayPinLocal;CustomBars=p.OverlayCustomBars;AllActors=p.OverlayAllActors;
        BossesOnly=p.OverlayBossesOnly;FollowTarget=p.OverlayFollowGameTarget;Scope=p.OverlayScope;
        Left=p.OverlayLeft;Top=p.OverlayTop;Width=p.OverlayWidth;Height=p.OverlayHeight;
    }
}
