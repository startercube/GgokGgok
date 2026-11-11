using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using Object = UnityEngine.Object;

/// <summary>
/// HUD 개수 글씨 앞 그림(빈대·얼룩)을 TMP 스프라이트 에셋으로 만든다 (2026-10-25).
/// 메뉴: Tools > HUD > 개수 아이콘 만들기 / CLI: -executeMethod HudIconBuilder.ApplyBatch
/// - 빈대: MenuAlien 프리팹을 정면(+Z)에서 찍음, 배경 투명
/// - 얼룩: Stain 프리팹이 쓰는 이모지(E9)를 그대로 키워서 씀 (픽셀아트라 가까운 픽셀로 키움)
/// - 결과 HudIcons는 TMP 기본 경로(Resources/Sprite Assets)에 있어서 &lt;sprite="HudIcons" name="Doll"&gt;로 바로 쓴다
/// </summary>
public static class HudIconBuilder
{
    const string AlienPrefabPath = "Assets/Prefabs/MainMenu/MenuAlien.prefab";
    const string StainEmojiPath = "Assets/3DModel&Asset/Free emojis pixel art/emojis-x1-32x32/E9.png";
    const string AtlasPath = "Assets/3DModel&Asset/HudIcons/HudIcons_Atlas.png";
    const string SpriteAssetPath = "Assets/TextMesh Pro/Resources/Sprite Assets/HudIcons.asset";
    const int Cell = 256;                        // 한 칸 크기 (글씨 50px의 5배 → 줄여도 깨끗함)
    const float Margin = 0.04f;                  // 칸 가장자리 여백 (비율)
    const float Yaw = 180f;                      // 카메라가 보는 방향. 180 = 모델 앞(+Z)에서 뒤로 봄 → 얼굴이 찍힘
    static readonly string[] Names = { "Doll", "Stain" };

    [MenuItem("Tools/HUD/개수 아이콘 만들기")]
    public static void ApplyMenu() => Apply();

    public static void ApplyBatch()
    {
        try { Apply(); }
        catch (Exception e) { Debug.LogError($"[HUD 아이콘] 실패: {e}"); EditorApplication.Exit(1); }
    }

    static void Apply()
    {
        var atlas = new Texture2D(Cell * Names.Length, Cell, TextureFormat.RGBA32, false);
        atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
        Blit(atlas, RenderAlien(), 0);
        Blit(atlas, UpscaleEmoji(), Cell);
        atlas.Apply();

        EnsureFolder(Path.GetDirectoryName(AtlasPath).Replace('\\', '/'));
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        var tex = ImportAtlas(AtlasPath);

        BuildSpriteAsset(tex);
        AssetDatabase.SaveAssets();
        Log($"완료 → {SpriteAssetPath}");
    }

    // ===================== 빈대 촬영 =====================

    static Texture2D RenderAlien()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlienPrefabPath);
        if (prefab == null) throw new Exception($"빈대 프리팹 없음: {AlienPrefabPath}");

        // 미리보기 씬에서 찍음 → 열린 씬은 건드리지 않음
        Scene scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        var matte = new List<Material>();
        try
        {
            var alien = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var mb in alien.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            alien.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var renderers = alien.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new Exception("빈대 프리팹에 렌더러가 없음");
            foreach (var r in renderers)
            {
                var mats = Array.ConvertAll(r.sharedMaterials, Matte);
                matte.AddRange(mats);
                r.sharedMaterials = mats;
            }
            Bounds box = renderers[0].bounds;
            foreach (var r in renderers) box.Encapsulate(r.bounds);

            AddLight(scene, new Vector3(30f, 200f, 0f), 1.1f);   // 앞 위에서 주광
            AddLight(scene, new Vector3(10f, 120f, 0f), 0.45f);  // 옆에서 보조광 (그림자 쪽이 까맣게 뭉개지지 않게)

            var go = new GameObject("__HudIconCamera");
            SceneManager.MoveGameObjectToScene(go, scene);
            var cam = go.AddComponent<Camera>();
            cam.scene = scene;
            cam.orthographic = true;
            cam.aspect = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;              // 후처리를 켜면 배경 알파가 사라짐

            Vector3 dir = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
            float radius = box.extents.magnitude;
            cam.transform.SetPositionAndRotation(box.center - dir * (radius * 3f), Quaternion.LookRotation(dir));
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 6f;
            // 정면에서 본 폭·높이 중 큰 쪽이 칸을 채우게
            Vector3 size = Quaternion.Inverse(cam.transform.rotation) * box.size;
            cam.orthographicSize = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f / (1f - 2f * Margin);

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var m in matte) if (m != null) Object.DestroyImmediate(m);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    // 찍는 동안만 쓰는 반사 없는 복사본 (원본 재질은 그대로, 찍은 뒤 지움) — 아이콘이 작아서 하이라이트가 흰 얼룩처럼 보임
    static Material Matte(Material src)
    {
        if (src == null) return null;
        var m = new Material(src) { hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_Smoothness", 0f);
        m.SetFloat("_SpecularHighlights", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        return m;
    }

    static void AddLight(Scene scene, Vector3 euler, float intensity)
    {
        var go = new GameObject("__HudIconLight");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // ===================== 얼룩 이모지 =====================

    static Texture2D UpscaleEmoji()
    {
        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(StainEmojiPath);
        if (src == null) throw new Exception($"얼룩 이모지 없음: {StainEmojiPath}");

        // 원본이 읽기 불가로 가져와져 있어도 되게 GPU로 복사해서 읽음
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var small = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        small.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        small.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        // 가까운 픽셀로 키움 → 아틀라스를 부드럽게 줄여 써도 픽셀 모양이 유지됨
        int inner = Mathf.RoundToInt(Cell * (1f - 2f * Margin));
        int pad = (Cell - inner) / 2;
        var tex = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
        tex.SetPixels32(new Color32[Cell * Cell]);
        for (int y = 0; y < inner; y++)
            for (int x = 0; x < inner; x++)
                tex.SetPixel(pad + x, pad + y, small.GetPixel(x * small.width / inner, y * small.height / inner));
        tex.Apply();
        Object.DestroyImmediate(small);
        return tex;
    }

    // ===================== 아틀라스·스프라이트 에셋 =====================

    static void Blit(Texture2D atlas, Texture2D cell, int x)
    {
        atlas.SetPixels(x, 0, Cell, Cell, cell.GetPixels());
        Object.DestroyImmediate(cell);
    }

    static Texture2D ImportAtlas(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BuildSpriteAsset(Texture2D tex)
    {
        // 다시 만들 때도 같은 에셋을 고쳐 씀 → GUID 유지
        var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(asset, SpriteAssetPath);
        }

        asset.spriteSheet = tex;
        asset.spriteGlyphTable.Clear();
        asset.spriteCharacterTable.Clear();
        for (int i = 0; i < Names.Length; i++)
        {
            // 칸 높이 = 폰트 어센트 높이로 맞춰짐 (faceInfo.pointSize 0 → TMP가 글씨에 맞춤). 아래 10%는 기준선 밑으로
            var glyph = new TMP_SpriteGlyph((uint)i, new GlyphMetrics(Cell, Cell, 0f, Cell * 0.9f, Cell), new GlyphRect(i * Cell, 0, Cell, Cell), 1f, 0);
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph) { name = Names[i], scale = 1f });
        }

        if (asset.material == null)
        {
            var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "TextMeshPro/Sprite" };
            AssetDatabase.AddObjectToAsset(mat, asset);
            asset.material = mat;
        }
        asset.material.SetTexture(ShaderUtilities.ID_MainTex, tex);

        // version은 TMP 내부 setter라 직렬화로 씀. 비어 있으면 불러올 때 옛 형식으로 보고 표를 지워 버림
        var so = new SerializedObject(asset);
        so.FindProperty("m_Version").stringValue = "1.1.0";
        so.ApplyModifiedPropertiesWithoutUndo();

        asset.UpdateLookupTables();
        EditorUtility.SetDirty(asset);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void Log(string m) => Debug.Log($"[HUD 아이콘] {m}");
}
