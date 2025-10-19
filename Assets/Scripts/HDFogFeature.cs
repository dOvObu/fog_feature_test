using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

class HDFogPass : ScriptableRenderPass
{
    const string NAME = "HeightDistanceFog";
    static readonly int Id_Color = Shader.PropertyToID("_Color");
    static readonly int ID_FogLow = Shader.PropertyToID("_FogLow");
    readonly Material _mat;
    readonly int _fogDownScale = 2;
    public HDFogPass(Material mat, int fogDownScale = 2)
    {
        _mat = mat;
        _fogDownScale = fogDownScale;
        requiresIntermediateTexture = true;
    }

    class LowPassData
    {
        //public TextureHandle depth;
        public TextureHandle lowFog;
        public Material mat;
    }
    
    class CompositePassData
    {
        public TextureHandle srcCol;
        public TextureHandle lowFog;
        public TextureHandle dstCol;
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
        if (res.isActiveTargetBackBuffer) return;

        // 1.
        var camColor = res.activeColorTexture;
        var colorDesc = graph.GetTextureDesc(camColor);
        var lowFogDesc = new TextureDesc(colorDesc)
        {
            name = $"{NAME}_Low",
            width = colorDesc.width/_fogDownScale,
            height = colorDesc.height/_fogDownScale, // size берётся из scaleFactor
            //scale = new Vector2(0.5f, 0.5f),
            clearBuffer = true,
            clearColor = Color.clear,
            colorFormat = GraphicsFormat.R8G8B8A8_UNorm, // достаточно
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            enableRandomWrite = false
        };
        var lowFog = graph.CreateTexture(lowFogDesc);
        
        // 2.
        using (var builder = graph.AddRasterRenderPass<LowPassData>($"{NAME}_LowPass", out var dataA))
        {
            dataA.mat = _mat;
            //dataA.depth = res.activeDepthTexture;
            //dataA.lowFog = lowFog;

            builder.UseTexture(res.activeColorTexture, AccessFlags.Read);
            builder.SetRenderAttachment(lowFog, 0, AccessFlags.Write);

            // покрасим из Volume параметры (пример: цвет)
            dataA.mat.SetColor(Id_Color, vol.fogColor.value);

            // Важно: рисуем full-screen в пониженном RT
            builder.SetRenderFunc<LowPassData>((d, ctx2) =>
            {
                // Материал, pass 0 — "LowRes Fog"
                Blitter.BlitTexture(ctx2.cmd, new Vector4(1,1,0,0), d.mat, 0);
            });
        }
        
        var composedDesc = new TextureDesc(colorDesc)
        {
            name = $"{NAME}_Compose",
            clearBuffer = false,
        };
        var composed = graph.CreateTexture(composedDesc);
        
        // 3.
        using (var builder = graph.AddRasterRenderPass<CompositePassData>($"{NAME}_Composite", out var dataB))
        {
            dataB.mat      = _mat;
            dataB.srcCol = res.activeColorTexture;
            dataB.lowFog   = lowFog;
            dataB.dstCol = composed;

            builder.UseTexture(dataB.lowFog, AccessFlags.Read); // _FogLowTex
            builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite); // _BlitTexture

            //builder.SetRenderAttachment(dataB.dstCol, 0, AccessFlags.Write);

            builder.SetRenderFunc<CompositePassData>((d, ctx2) =>
            {
                // пробросим low-res текстуру вручную
                var rt = d.lowFog;
                // RenderGraph сам биндит _BlitTexture = d.srcColor для Blitter
                d.mat.SetTexture(ID_FogLow, rt);
                //ctx2.cmd.
                //ctx2.cmd.SetTexture(ID_FogLow, rt);
                // можно передать _FogLowTex_TexelSize, если нужно (по желанию)
                // ctx2.cmd.SetGlobalVector(ID_FogLow_TexelSize, ...);

                // Материал, pass 1 — "Composite"
                Blitter.BlitTexture(ctx2.cmd, new Vector4(1,1,0,0), d.mat, 1);
            });
        }
    }
}

public class HDFogFeature : ScriptableRendererFeature
{
    public Material material;
    public int fogDownScale = 2;
    public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.None;
    public FullScreenPassRendererFeature.InjectionPoint injectionPoint =
        FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
    
    HDFogPass _pass;
    public override void Create()
    {
        _pass = new(material, fogDownScale)
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
