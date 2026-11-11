using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 물체에 외곽선을 그린다. 그룹 두 개:
//  - 조준 (AimHighlight): 노랑, 앞 물체에 가려진 부분의 테두리까지 (투시). 「코드 설계 - 조준 외곽선(투시)」
//  - 가이드 (GuideHighlight, 2026-11-02): 하늘색, 천천히 깜빡임, 보이는 부분만. 「코드 설계 - 튜토리얼 가이드 시각화」
// 그룹마다:
// ① 마스크 패스: 대상 렌더러만 R8 텍스처에 그린다 (투시 = 깊이 검사 없이 / 아니면 장면 깊이 텍스처와 비교해 가려진 조각은 버림)
// ② 외곽선 패스: 마스크 밖인데 가까이에 마스크가 있는 픽셀만 외곽선 색으로 화면 위에 합성
// 메시 법선·머티리얼과 무관해 모든 물체에 같은 굵기로 그려지고, 대상 렌더러의 상태는 하나도 바꾸지 않는다.
// PC_Renderer에 등록되어 있다.
public class AimOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("외곽선 색. 알파는 진하기")]
        public Color color = new Color(1f, 0.82f, 0.25f, 1f);
        [Tooltip("외곽선 두께 (기준 화면 높이에서의 픽셀). 실제 화면 높이에 비례해 늘어난다")]
        [Range(1f, 8f)] public float widthPixels = 3f;
        [Tooltip("두께의 기준이 되는 화면 높이 — 전시회 모니터 해상도가 달라도 같은 굵기로 보이게")]
        public float referenceHeight = 1080f;
        [Tooltip("그리는 시점. 후처리 뒤 = 정한 색 그대로 또렷하게 / 후처리 전 = 블룸에 번져 발광 느낌")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        [Tooltip("켜면 앞 물체에 가려진 부분까지 테두리 (투시). 끄면 보이는 부분만 — 장면 깊이 텍스처가 필요 (PC_RPAsset Depth Texture 켬)")]
        public bool xray = true;
        [Tooltip("깜빡임 가장 옅을 때의 진하기 배율 (1 = 깜빡이지 않음)")]
        [Range(0f, 1f)] public float pulseMin = 1f;
        [Tooltip("깜빡임 한 주기(초). 0이면 깜빡이지 않음")]
        public float pulsePeriod = 0f;
    }

    [Tooltip("Hidden/AimOutline. 여기 연결되어 있어야 빌드에 포함된다")]
    [SerializeField] private Shader shader;
    [Tooltip("조준 외곽선 (기존 값 그대로)")]
    [SerializeField] private Settings settings = new Settings();
    [Tooltip("튜토리얼 가이드 외곽선 — 봐야 할 것·가야 할 곳 (2026-11-02). 조준과 섞이지 않게 다른 색·깜빡임·보이는 부분만")]
    [SerializeField] private Settings guideSettings = new Settings
    {
        color = new Color(0.35f, 0.85f, 1f, 1f),
        widthPixels = 4f,
        xray = false,
        pulseMin = 0.35f,
        pulsePeriod = 1.2f,
    };

    private Material aimMaterial, guideMaterial;
    private OutlinePass aimPass, guidePass;
    private bool warnedMissingShader;

    public override void Create()
    {
        // Create는 인스펙터 수정 때마다 다시 불린다
        CoreUtils.Destroy(aimMaterial);
        CoreUtils.Destroy(guideMaterial);
        aimMaterial = guideMaterial = null;
        aimPass = guidePass = null;

        if (shader == null) shader = Shader.Find("Hidden/AimOutline");
        if (shader == null) return;   // AddRenderPasses에서 경고 한 번

        // 그룹마다 머티리얼을 따로: 렌더 그래프는 기록 뒤에 실행되므로 한 머티리얼에 색을 두 번 넣으면 마지막 값만 남는다
        aimMaterial = CoreUtils.CreateEngineMaterial(shader);
        guideMaterial = CoreUtils.CreateEngineMaterial(shader);
        aimPass = new OutlinePass("Aim Outline", aimMaterial, settings, AimHighlight.Targets);
        guidePass = new OutlinePass("Guide Outline", guideMaterial, guideSettings, GuideHighlight.Targets);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        bool aim = AimHighlight.HasTarget, guide = GuideHighlight.HasTarget;
        if (!aim && !guide) return;   // 대상이 없으면 비용 0
        if (aimPass == null)
        {
            if (!warnedMissingShader)
            {
                Debug.LogWarning("[조준 외곽선] 셰이더 Hidden/AimOutline을 찾지 못해 외곽선을 그리지 않습니다. PC_Renderer의 Aim Outline Feature에 셰이더를 연결하세요.", this);
                warnedMissingShader = true;
            }
            return;
        }
        // 가이드를 먼저 넣는다 → 같은 시점이면 조준 외곽선이 그 위에 그려짐 (같은 물체일 때 조준이 우선)
        if (guide) Enqueue(renderer, guidePass);
        if (aim) Enqueue(renderer, aimPass);
    }

    static void Enqueue(ScriptableRenderer renderer, OutlinePass pass)
    {
        pass.renderPassEvent = pass.settings.passEvent;
        pass.ConfigureInput(pass.settings.xray ? ScriptableRenderPassInput.None : ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(aimMaterial);
        CoreUtils.Destroy(guideMaterial);
        aimMaterial = guideMaterial = null;
        aimPass = guidePass = null;
    }

    class OutlinePass : ScriptableRenderPass
    {
        const int MaskXrayShaderPass = 0;
        const int CompositeShaderPass = 1;
        const int MaskVisibleShaderPass = 2;
        static readonly int ColorId = Shader.PropertyToID("_AimOutlineColor");
        static readonly int RadiusId = Shader.PropertyToID("_AimOutlineRadius");
        static readonly int MaskSizeId = Shader.PropertyToID("_AimOutlineMaskSize");

        readonly string label;
        readonly Material material;
        public readonly Settings settings;
        readonly OutlineTargetSet targets;

        class MaskData
        {
            public Material material;
            public OutlineTargetSet targets;
            public int shaderPass;
        }

        class CompositeData
        {
            public TextureHandle mask;
            public Material material;
        }

        public OutlinePass(string label, Material material, Settings settings, OutlineTargetSet targets)
        {
            this.label = label;
            this.material = material;
            this.settings = settings;
            this.targets = targets;
            profilingSampler = new ProfilingSampler(label);
            // 후처리 뒤에 화면 색 버퍼 위에 그리므로, URP가 백버퍼 대신 중간 텍스처를 쓰게 한다
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!targets.ShouldDraw(cameraData.camera)) return;   // 넣은 쪽이 정한 카메라(플레이어 카메라)에만

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;   // requiresIntermediateTexture 덕에 보통 일어나지 않음

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            TextureDesc maskDesc = new TextureDesc(camDesc.width, camDesc.height)
            {
                name = "_" + label.Replace(" ", "") + "Mask",
                format = GraphicsFormat.R8_UNorm,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Bilinear,   // 외곽선 패스에서 부드러운 가장자리
                wrapMode = TextureWrapMode.Clamp,
                msaaSamples = MSAASamples.None,
            };
            TextureHandle mask = renderGraph.CreateTexture(maskDesc);

            // 보이는 부분만 그리는 그룹은 장면 깊이 텍스처를 읽는다. 없으면(설정이 꺼짐) 투시로 대신 그린다
            TextureHandle depth = resourceData.cameraDepthTexture;
            bool visibleOnly = !settings.xray && depth.IsValid();

            material.SetVector(MaskSizeId, new Vector4(camDesc.width, camDesc.height, 0f, 0f));

            // ① 마스크
            using (var builder = renderGraph.AddRasterRenderPass<MaskData>(label + " Mask", out MaskData data, profilingSampler))
            {
                data.material = material;
                data.targets = targets;
                data.shaderPass = visibleOnly ? MaskVisibleShaderPass : MaskXrayShaderPass;
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                if (visibleOnly) builder.UseTexture(depth, AccessFlags.Read);   // 셰이더가 _CameraDepthTexture로 읽음
                builder.SetRenderFunc((MaskData d, RasterGraphContext ctx) =>
                {
                    var items = d.targets.Items;
                    for (int i = 0; i < items.Count; i++)
                    {
                        Renderer r = items[i].renderer;
                        // 얼룩을 닦거나 인형을 잡은 프레임엔 렌더러가 이미 파괴되어 있을 수 있다
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        for (int s = 0; s < items[i].subMeshCount; s++)
                            ctx.cmd.DrawRenderer(r, d.material, s, d.shaderPass);
                    }
                });
            }

            // 두께: 기준 해상도의 픽셀 → 실제 화면 높이에 비례 → 마스크 UV 단위
            float px = settings.widthPixels * camDesc.height / Mathf.Max(1f, settings.referenceHeight);
            Color c = settings.color;
            c.a *= Pulse(settings);
            material.SetColor(ColorId, c);
            material.SetVector(RadiusId, new Vector4(px / camDesc.width, px / camDesc.height, 0f, 0f));

            // ② 외곽선: 마스크를 읽어 화면 색 버퍼 위에 반투명 합성
            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>(label + " Composite", out CompositeData data, profilingSampler))
            {
                data.mask = mask;
                data.material = material;
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);   // 기존 화면 위에 섞음
                builder.SetRenderFunc((CompositeData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.mask, new Vector4(1f, 1f, 0f, 0f), d.material, CompositeShaderPass));
            }
        }

        // 진하기 배율: pulseMin ↔ 1을 코사인으로 오간다 (일시정지 중에도 천천히 계속 — 실제 시간 기준)
        static float Pulse(Settings s)
        {
            if (s.pulsePeriod <= 0f || s.pulseMin >= 1f) return 1f;
            float t = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * (2f * Mathf.PI / s.pulsePeriod));
            return Mathf.Lerp(s.pulseMin, 1f, t);
        }
    }
}
