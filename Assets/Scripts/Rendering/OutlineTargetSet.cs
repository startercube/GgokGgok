using System.Collections.Generic;
using UnityEngine;

// 외곽선을 그릴 대상 하나(하이라이트 단위의 루트)와 그 렌더러 목록, 그릴 카메라를 담는 상자.
// 외곽선 그룹마다 하나씩 가진다: 조준(AimHighlight) / 튜토리얼 가이드(GuideHighlight).
// 게임 쪽이 Set으로 넣고, 그래픽 쪽(AimOutlineFeature)이 읽기만 한다 — 서로를 모른다.
// 설계: 「코드 설계 - 조준 외곽선(투시)」 2-3, 「코드 설계 - 튜토리얼 가이드 시각화」 2-2
public class OutlineTargetSet
{
    public struct Item
    {
        public Renderer renderer;
        public int subMeshCount;   // 머티리얼이 여러 개인 소품도 서브메시마다 그린다
    }

    readonly List<Item> items = new List<Item>();
    readonly List<Renderer> scratch = new List<Renderer>();

    /// <summary>외곽선을 그릴 오브젝트 (하이라이트 단위의 루트)</summary>
    public GameObject Target { get; private set; }
    /// <summary>이 카메라가 렌더할 때만 그린다 — CCTV·무전 초상화 카메라·씬 뷰 제외</summary>
    public Camera ViewCamera { get; private set; }
    /// <summary>그릴 렌더러 목록. 대상이 바뀔 때만 다시 모은다</summary>
    public IReadOnlyList<Item> Items => items;

    public bool HasTarget => Target != null && items.Count > 0;

    public bool ShouldDraw(Camera cam) => HasTarget && cam != null && cam == ViewCamera;

    /// <summary>대상을 바꾼다. 같은 대상이면 아무것도 하지 않으므로 매 프레임 불러도 된다</summary>
    public void Set(GameObject unit, Camera viewCamera)
    {
        // ReferenceEquals: 파괴된 옛 대상과 null을 "같다"고 보지 않게 (Unity의 == 는 파괴된 오브젝트를 null로 본다)
        if (ReferenceEquals(unit, Target) && viewCamera == ViewCamera) return;

        Target = unit;
        ViewCamera = viewCamera;
        items.Clear();
        if (unit == null) return;

        // 켜진 렌더러만 — 변장한 인형은 원래 모델 렌더러가 꺼져 있어 자동으로 빠진다
        unit.GetComponentsInChildren(false, scratch);
        foreach (Renderer r in scratch)
        {
            if (!r.enabled) continue;
            int sub = SubMeshCountOf(r);
            if (sub > 0) items.Add(new Item { renderer = r, subMeshCount = sub });
        }
        scratch.Clear();
    }

    public void Clear() => Set(null, null);

    /// <summary>정적 보관함의 플레이 시작 초기화용 (도메인 다시 불러오기 끄기 대비)</summary>
    public void Reset()
    {
        Target = null;
        ViewCamera = null;
        items.Clear();
        scratch.Clear();
    }

    // 메시·스킨 메시·스프라이트만 그린다 (파티클·선·트레일 같은 효과 렌더러는 실루엣이 아니므로 제외)
    static int SubMeshCountOf(Renderer r)
    {
        switch (r)
        {
            case SpriteRenderer _:
                return 1;
            case SkinnedMeshRenderer skinned:
                return MinCount(skinned.sharedMesh, skinned);
            case MeshRenderer mesh:
                MeshFilter filter = mesh.GetComponent<MeshFilter>();
                return MinCount(filter != null ? filter.sharedMesh : null, mesh);
            default:
                return 0;
        }
    }

    static int MinCount(Mesh mesh, Renderer r)
    {
        if (mesh == null) return 0;
        return Mathf.Min(mesh.subMeshCount, r.sharedMaterials.Length);
    }
}
