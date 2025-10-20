using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

class HDFogPass : ScriptableRenderPass
{
    const string NAME = "HeightDistanceFog";
    static readonly int _Color = Shader.PropertyToID("_Color");
    static readonly int _FogLow = Shader.PropertyToID("_FogLow");
    static readonly int _MaxDistance = Shader.PropertyToID("_MaxDistance");
    static readonly int _DensityThreshold = Shader.PropertyToID("_DensityThreshold");
    static readonly int _Density = Shader.PropertyToID("_Density");
    static readonly int _HeightStart = Shader.PropertyToID("_HeightStart");
    static readonly int _HeightFalloff = Shader.PropertyToID("_HeightFalloff");
    static readonly int _LightContribution = Shader.PropertyToID("_LightContribution");
    static readonly int _LightScattering = Shader.PropertyToID("_LightScattering");
    static readonly int _CameraTexture = Shader.PropertyToID("_CameraTexture");

    readonly Material _mat;
    public HDFogPass(Material mat)
    {
        _mat = mat;
        //requiresIntermediateTexture = true;
    }

    class LowPassData
    {
        public Material mat;
    }
    
    class CompositePassData
    {
        public TextureHandle col;
        public TextureHandle lowFog;
        public Material mat;
    }
    
    public override void RecordRenderGraph(RenderGraph graph, ContextContainer ctx)
    {
        if (_mat == null) return;

        var cam = ctx.Get<UniversalCameraData>();
        if (!cam.postProcessEnabled) return;
        
        var vol = VolumeManager.instance.stack.GetComponent<HDFogVolume>();
        if (!vol || !vol.active) return;

        var res = ctx.Get<UniversalResourceData>();

        var camColor = res.activeColorTexture;
        var colorDesc = graph.GetTextureDesc(camColor);
        var lowFogDesc = new TextureDesc(colorDesc)
        {
            name = $"{NAME}_Low",
            width = colorDesc.width / vol.scaleFactor.value,
            height = colorDesc.height / vol.scaleFactor.value,
            clearBuffer = true,
            clearColor = Color.clear,
            colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            enableRandomWrite = false
        };
        var lowFog = graph.CreateTexture(lowFogDesc);
        
        using (var builder = graph.AddRasterRenderPass<LowPassData>($"{NAME}_LowPass", out var dataA))
        {
            dataA.mat = _mat;
            dataA.mat.SetColor(_Color, vol.fogColor.value);
            dataA.mat.SetFloat(_MaxDistance, vol.maxDistance.value);
            dataA.mat.SetFloat(_DensityThreshold, vol.densityThreshold.value);
            dataA.mat.SetFloat(_Density, vol.density.value);
            dataA.mat.SetFloat(_HeightStart, vol.heightStartY.value);
            dataA.mat.SetFloat(_HeightFalloff, vol.heightFalloff.value);
            dataA.mat.SetColor(_LightContribution, vol.lightContribution.value);
            dataA.mat.SetFloat(_LightScattering, vol.lightScattering.value);
            
            builder.UseTexture(res.cameraColor, AccessFlags.Read);
            builder.SetRenderAttachment(lowFog, 0, AccessFlags.Write);
            
            builder.SetRenderFunc<LowPassData>((d, ctx2) =>
            {
                Blitter.BlitTexture(ctx2.cmd, new Vector4(1,1,0,0), d.mat, 0);
            });
        }

        using (var builder = graph.AddRasterRenderPass<CompositePassData>($"{NAME}_Composite", out var dataB))
        {
            dataB.mat      = _mat;
            dataB.col = res.cameraColor;
            dataB.lowFog   = lowFog;
            
            builder.UseTexture(dataB.lowFog, AccessFlags.Read); // _FogLowTex
            builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite); // _BlitTexture

            builder.SetRenderFunc<CompositePassData>((d, ctx2) =>
            {
                d.mat.SetTexture(_CameraTexture, d.col);
                d.mat.SetTexture(_FogLow, d.lowFog);
                
                Blitter.BlitTexture(ctx2.cmd, new Vector4(1,1,0,0), d.mat, 1);
            });
        }
    }
}

public class HDFogFeature : ScriptableRendererFeature
{
    public Material material;
    public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.None;
    public FullScreenPassRendererFeature.InjectionPoint injectionPoint =
        FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
    
    HDFogPass _pass;
    public override void Create()
    {
        _pass = new(material)
        {
            renderPassEvent = (RenderPassEvent)injectionPoint
        };
        _pass.ConfigureInput(requirements);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }
}
