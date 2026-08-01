using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class Water_Volume : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        private Material _material;

        public CustomRenderPass(Material mat)
        {
            _material = mat;
        }

        // Render Graph API (URP removed the old Configure/Execute override pair in favor of
        // RecordRenderGraph). Reproduces the original two-blit behavior: run the camera color
        // through the water material into a temp texture, then copy that back onto the camera
        // color target.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.cameraType == CameraType.Reflection)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            TextureDesc tempDesc = renderGraph.GetTextureDesc(source);
            tempDesc.name = "_Water_Volume_Temp";
            tempDesc.clearBuffer = false;
            TextureHandle temp = renderGraph.CreateTexture(tempDesc);

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, temp, _material, 0);
            renderGraph.AddBlitPass(blitParams, passName: "Water Volume Blit");

            renderGraph.AddBlitPass(temp, source, Vector2.one, Vector2.zero, passName: "Water Volume Copy");
        }
    }

    [System.Serializable]
    public class _Settings
    {
        //[HideInInspector]
        public Material material = null;
        public RenderPassEvent renderPass = RenderPassEvent.AfterRenderingSkybox;
    }

    public _Settings settings = new _Settings();

    CustomRenderPass m_ScriptablePass;

    public override void Create()
    {
        if(settings.material == null)
        {
            settings.material = (Material)Resources.Load("Water_Volume");
        }

        m_ScriptablePass = new CustomRenderPass(settings.material);

        // Configures where the render pass should be injected.
        //m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        m_ScriptablePass.renderPassEvent = settings.renderPass;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }
}
