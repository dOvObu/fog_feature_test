using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

class HDFogPass : ScriptableRenderPass
{
    const string NAME = "HeightDistanceFog";
    static readonly int Id_Color = Shader.PropertyToID("_Color");
    readonly Material _mat;
    public HDFogPass(Material mat)
    {
        _mat = mat;
        requiresIntermediateTexture = true;
    }

    
    class PassData
    {
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

        
        using (var builder = graph.AddRasterRenderPass<PassData>($"{NAME}_Pass", out var data))
        {
            builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
            data.mat = _mat;
            data.mat.SetColor(Id_Color, vol.fogColor.value);
            
            builder.SetRenderFunc<PassData>((data, ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, new(1,1,0,0), data.mat, 0);
            });
        }
    }

    // public override void RecordRenderGraph(RenderGraph graph, ContextContainer ctx)
    // {
    //     var vol = VolumeManager.instance.stack.GetComponent<HDFogVolume>();
    //     if (!vol || !vol.active) return;
    //         
    //     var res = ctx.Get<UniversalResourceData>();
    //     if (res.isActiveTargetBackBuffer)
    //     {
    //         Debug.LogError("ERROR! res.isActiveTargetBackBuffer but this post process for intermediate Color Tex");
    //         return;
    //     }
    //
    //     var col = res.activeColorTexture;
    //         
    //     var desc = graph.GetTextureDesc(col);
    //     desc.name = $"CameraColor-{NAME}";
    //     desc.clearBuffer = false;
    //
    //     var newCol = graph.CreateTexture(desc);
    //     
    //     graph.AddBlitPass(new(col, newCol, _mat, 0), passName: NAME);
    //     
    //     res.cameraColor = newCol;
    // }
}

public class HDFogFeature : ScriptableRendererFeature
{
    public Material material;
    public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.None;
    public FullScreenPassRendererFeature.InjectionPoint injectionPoint =
        FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
    
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
