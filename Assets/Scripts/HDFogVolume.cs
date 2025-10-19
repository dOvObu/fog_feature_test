using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
[VolumeComponentMenu("Custom/HeightDistanceFogVolume")]
public class HDFogVolume : VolumeComponent, IPostProcessComponent
{
    public ColorParameter fogColor = new(Color.white);
    public ClampedFloatParameter fogHeightMin = new(0, -200, 300);
    public ClampedFloatParameter fogHeightMax = new(0, -200, 300);
    public ClampedFloatParameter fogDistanceMax = new(0,0, 2000);
    public ClampedFloatParameter intensity = new(1, 0, 1);
    public BoolParameter excludeSkybox = new (false);
    
    public bool IsTileCompatible() => true;
    bool IPostProcessComponent.IsActive()
    {
        return fogHeightMax.value > fogHeightMin.value && intensity.value > 0;
    }
}
