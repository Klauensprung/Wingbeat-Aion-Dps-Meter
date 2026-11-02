using System.IO;

namespace AionDpsMeter.Local;

internal sealed record AppearanceSetting(string Group, string Field, int? Value, string? Color);

// Field ordering and RGB widths are protocol facts from the installed version-12 reader.
internal static class AppearanceSettings
{
    internal const int ByteLength = 329;
    static readonly string[] Body = "Length_Waist.Length_Spine.Length_Thigh.Length_Calf.Length_UpperArm.Length_LowerArm.Thick_AllBody.Size_Head.Thick_Neck.Length_Neck.Size_Spine.Size_Breast.Pos_LR_Breast.Pos_UpDown_Breast.UpVolume_Breast.Width_Waist.Size_Abdominal.Width_Shoulder.Size_Trapezius.Size_Lat.Width_Pelvis.UpDown_Butt.Size_Butt.Thick_Leg.Thick_Quadriceps.Thick_CalfMuscle.Width_Foot.Size_Deltoid.Size_Triceps.Size_Biceps.Thick_Upperarm.Thick_Forearm.Thick_Arm.Size_Hand.Thick_Finger.Length_Finger.Height.Vein.Velly_Intensity.Arm_Intensity.Leg_Intensity.BMI".Split('.');
    static readonly string[] Face = "ForeHead_Height.CheekBone_Height.AppleZone_Height.CheekBone_UpDown.Chin_Length.Chin_Width.Chin_Forward.Plastima_UpDown.Plastima_Width.PlastimaA_UpDown.Shape_UpDown.Shape_InOut.Puff_InOut.EyeBrow_UpDown.EyeBrow_Depth.EyeBrowIn_InOut.Eye_UpDown.Eye_Dist.Eye_Depth.Eye_Rotate.Eye_Size.EyeTail_InOut.EyeTail_UpDown.EyeHead_UpDown.EyeHead_InOut.EyeBrowUpper_UpDown.EyeBrowLower_UpDown.Nose_Size.Nose_Length.NoseTip_Height.NoseTip_UpDown.NoseBridge_Height.NoseBridge_UpDown.NoseBridgeLow_Height.NoseBridgeLow_UpDown.Nostril_InOut.Nostril_UpDown.Lip_UpDown.Lip_Width.Lip_Thickness.LipCorner_UpDown.LipCorner_InOut.LipCorner_Depth.LipLower_Width.LipLower_Thickness.LipUpper_Width.LipUpper_Thickness.LipUpperSide_Thickness.Lip_Height.LipLower_Height.LipUpper_Height.LipRotation.Ear_UpDown.Ear_Size.Ear_FwdBwd.Ear_Rotate.Earlobe_Size.EarShapeChange.EyelashLength.EyelashUpDown".Split('.');
    static readonly string[] Customize = "SkinColor.FaceGlossiness.BodyGlossiness.bUseBodyGlossiness.bOddEye_Pupil.PupilSize_L.PupilSize_R.PupilShape_L.PupilShape_R.PupilColor_L.PupilColor_R.bUseEmiPupil_L.bUseEmiPupil_R.bOddEye_Iris.IrisSize_L.IrisSize_R.IrisShape_L.IrisShape_R.IrisColorMid_L.IrisColorEdge_L.IrisColorMid_R.IrisColorEdge_R.bOddEye_Sclera.ScleraColor_L.ScleraColor_R.ScleraVeinsPower_L.ScleraVeinsPower_R.ScleraVeinColor_L.ScleraVeinColor_R.EyelashType.EyelashColor_Upper.EyelashColor_Lower.EyelashThickness_Upper.EyelashThickness_Lower.EyebrowType.EyebrowColor.EyebrowHairIntensity.EyebrowRoughness.EyebrowUpDown.EyebrowDist.EyebrowRotate.EyebrowWidth.EyebrowThickness.ShadingType.ShadingColor.ShadingAlpha.ShadingRoughness.HighlighterType.HighlighterColor.HighlighterAlpha.HighlighterMetalic.HighlighterRoughness.BlusherType.BlusherColor.BlusherAlpha.BlusherRoughness.GlitterArea_Blusher.GlitterColorUse_Blusher.GlitterColor_Blusher.GlitterMetalic_Blusher.GlitterSize_Blusher.GlitterDensity_Blusher.EyeshadowType.EyeshadowColor.EyeshadowAlpha.EyeshadowMetalic.EyeshadowRoughness.Eyeshadow2Type.Eyeshadow2Color.Eyeshadow2Alpha.Eyeshadow2Metalic.Eyeshadow2Roughness.GlitterArea_EyeShadowA.GlitterColorUse_EyeShadowA.GlitterColor_EyeShadowA.GlitterMetalic_EyeShadowA.GlitterSize_EyeShadowA.GlitterDensity_EyeShadowA.GlitterArea_EyeShadowB.GlitterColorUse_EyeShadowB.GlitterColor_EyeShadowB.GlitterMetalic_EyeShadowB.GlitterSize_EyeShadowB.GlitterDensity_EyeShadowB.EyelineType.EyelineColor.EyelineAlpha.EyelineMetalic.EyelineRoughness.LipType.LipColor.LipAlpha.LipMetalic.LipRoughness.Lip2Type.Lip2Color.Lip2Alpha.Lip2Metalic.Lip2Roughness.GlitterArea_Lip.GlitterColorUse_Lip.GlitterColor_Lip.GlitterMetalic_Lip.GlitterSize_Lip.GlitterDensity_Lip.FreckleType.FreckleColor.FreckleAlpha.MoleType.MoleColor.MoleAlpha.MoleUpDown.MoleLeftRight.TattooType.TattooColor.TattooUpDown.TattooLeftRight.TattooScaleU.TattooScaleV.TattooRotate.bTattooFlipU.bTattooFlipV.bTattooSymmetry.ScarType.ScarColor.ScarUpDown.ScarLeftRight.ScarScaleU.ScarScaleV.ScarRotate.ScarDepth.WarPaintType.WarPaintR_Color.WarPaintR_NormalInt.WarPaintR_Metallic.WarPaintR_Roughness.WarPaintG_Color.WarPaintG_NormalInt.WarPaintG_Metallic.WarPaintG_Roughness.BodyTattooType_Torso_F.BodyTattooType_Torso_B.BodyTattooType_Arm_R.BodyTattooType_Arm_L.BodyTattooType_Leg_R.BodyTattooType_Leg_L.HeadType.HairType.HairColor.Hair2Color.HairGlossiness.bUseHairColorDetailSettings.RootTipRange.RootTipContrast.BeardType.BeardColor.StubbleType.StubbleIntensity".Split('.');
    static readonly HashSet<string> Colors = new("PupilColor_L.PupilColor_R.IrisColorMid_L.IrisColorEdge_L.IrisColorMid_R.IrisColorEdge_R.ScleraColor_L.ScleraColor_R.ScleraVeinColor_L.ScleraVeinColor_R.EyelashColor_Upper.EyelashColor_Lower.EyebrowColor.ShadingColor.HighlighterColor.BlusherColor.GlitterColor_Blusher.EyeshadowColor.Eyeshadow2Color.GlitterColor_EyeShadowA.GlitterColor_EyeShadowB.EyelineColor.LipColor.Lip2Color.GlitterColor_Lip.FreckleColor.MoleColor.TattooColor.ScarColor.WarPaintR_Color.WarPaintG_Color.HairColor.Hair2Color.BeardColor".Split('.'));
    internal static AppearanceSetting[] Decode(string data)
    {
        if (data == null || data.Length > 87384) throw new InvalidDataException("Appearance data exceeds bounds.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid appearance encoding.", ex); }
        if (bytes.Length < ByteLength || bytes.Length > 65536 || bytes[0] != 12)
            throw new InvalidDataException("Unsupported or incomplete appearance data.");
        var result = new List<AppearanceSetting>(Body.Length + Face.Length + Customize.Length);
        int offset = 1;
        void Read(string group, string[] fields) {
            foreach (string field in fields) {
                if (Colors.Contains(field)) {
                    string color = $"#{bytes[offset]:X2}{bytes[offset + 1]:X2}{bytes[offset + 2]:X2}";
                    offset += 3; result.Add(new(group, field, null, color));
                } else result.Add(new(group, field, bytes[offset++], null));
            }
        }
        Read("Body", Body); Read("Face", Face); Read("Customize", Customize);
        return result.ToArray();
    }
}
