using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[Serializable]
[VolumeComponentMenu("Custom/HeightDistanceFogVolume")]
public class HDFogVolume : VolumeComponent, IPostProcessComponent
{
    public ColorParameter fogColor = new(new Color(0.7176471f, 0.5921569f, 0.7163429f));
    public FloatParameter maxDistance = new(70);
    public FloatParameter heightStartY = new(-2.25f);
    public ClampedFloatParameter densityThreshold = new(0, 0, 1);
    public ClampedFloatParameter density = new(0.75f,0, 2);
    public ClampedFloatParameter heightFalloff = new(0.41f,0.1f, 10);
    public ClampedFloatParameter lightScattering = new(0.892f,0f, 0.998f);
    public ColorParameter lightContribution = new(new Color(0.5f, 0.22475362f, 0f), true, false, true);
    public ClampedIntParameter scaleFactor = new(4, 1, 8);
    
        
    public bool IsTileCompatible() => true;
    bool IPostProcessComponent.IsActive()
    {
        return density.value > float.Epsilon;
    }
}
