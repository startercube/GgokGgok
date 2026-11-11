using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 게임 시작마다 술래 인형(DollMove)을 맵의 상호작용 오브젝트(Prop) 중 하나의 모습으로 무작위 변장시킨다.
// 리플레이는 녹화 때와 같은 모습이어야 하므로 고른 결과를 Assignments로 내보내고, 리플레이 모드에선 그 값을 재사용한다
public static class DollDisguise
{
    // 인형 ID(ReplayIdentity 정렬 순서)별로 고른 후보 인덱스 — ReplayRecorder가 ReplayData에 싣는다
    public static int[] Assignments { get; private set; }

    private static int appliedSceneHandle = -1;   // 씬 로드마다 핸들이 새로 발급된다 — 한 번 로드에 한 번만 적용

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedState()
    {
        appliedSceneHandle = -1;
        Assignments = null;
    }

    // DollMove.Awake가 호출 — 가장 먼저 깨어난 인형이 씬의 모든 인형을 한꺼번에 변장시킨다.
    // Awake 단계에서 끝내야 ReplayRecorder/ReplayManager의 Start가 결과를 읽을 수 있다
    public static void EnsureApplied(Scene scene)
    {
        if (appliedSceneHandle == scene.handle) return;
        appliedSceneHandle = scene.handle;

        DollMove[] dolls = Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        ReplayIdentity.SortById(dolls);

        List<Transform> candidates = CollectCandidates();
        Assignments = new int[dolls.Length];
        for (int i = 0; i < Assignments.Length; i++) Assignments[i] = -1;

        // 리플레이는 녹화 때 모습을 그대로 써야 한다 — 기록이 맞지 않으면 새로 뽑지 않고 멈춘다
        int[] recorded = null;
        if (IsReplayMode())
        {
            recorded = GameManager.Instance.replayData.dollDisguises;
            if (!IsValidRecord(recorded, dolls.Length, candidates.Count))
            {
                Debug.LogError("[DollDisguise] 리플레이 데이터의 변장 기록이 현재 씬과 맞지 않아 재현할 수 없습니다 " +
                               $"(기록 {(recorded == null ? "없음" : recorded.Length + "개")}, 인형 {dolls.Length}개, 후보 {candidates.Count}개). " +
                               "녹화 쪽에서 dollDisguises가 저장되는지 확인하세요.");
                Debug.Break();   // 에디터 플레이를 일시정지 — 틀린 모습으로 재생되는 것을 막는다
                return;
            }
        }
        else if (candidates.Count == 0)
        {
            Debug.LogWarning("[DollDisguise] 변장할 Prop이 없어 인형이 원래 모습으로 시작합니다.");
            return;
        }

        List<int> pool = new List<int>();   // 인형끼리 같은 오브젝트를 고르지 않도록 뽑은 것은 뺀다

        for (int i = 0; i < dolls.Length; i++)
        {
            // 깜찍이 랜덤변신 방지
            if (dolls[i].GetComponent<AlienMimicFx>() != null) continue;
            // 정해 둔 모습을 지키는 빈대 (튜토리얼: 미끼와 같은 원본 소품 모양, 2026-11-02 「코드 설계 - 튜토리얼 방 A 곁눈질 찾기·방 B 정답 위치」). 기록은 -1 = 변장 안 함
            if (dolls[i].KeepsOwnLook) continue;

            int pick;
            if (recorded != null)
            {
                pick = recorded[i];
            }
            else
            {
                if (pool.Count == 0)
                    for (int c = 0; c < candidates.Count; c++) pool.Add(c);
                int slot = Random.Range(0, pool.Count);
                pick = pool[slot];
                pool.RemoveAt(slot);
            }

            Assignments[i] = pick;
            if (pick >= 0 && pick < candidates.Count)
                Apply(dolls[i], candidates[pick]);
        }
    }

    static bool IsReplayMode()
    {
        GameManager gm = GameManager.Instance;
        return gm != null && gm.isReplayMode && gm.replayData != null;
    }

    // 기록이 있고, 인형 수가 같고, 모든 번호가 후보 범위 안(-1은 '변장 안 함')이어야 그대로 재현할 수 있다
    static bool IsValidRecord(int[] recorded, int dollCount, int candidateCount)
    {
        if (recorded == null || recorded.Length != dollCount) return false;
        foreach (int pick in recorded)
            if (pick < -1 || pick >= candidateCount) return false;
        return true;
    }

    // Prop 태그가 붙은 오브젝트 중 인형이 아니고 메시가 있는 것. 부모도 Prop이면 부모 쪽만 후보로 친다
    static List<Transform> CollectCandidates()
    {
        List<Transform> result = new List<Transform>();
        foreach (GameObject go in GameObject.FindGameObjectsWithTag("Prop"))
        {
            Transform t = go.transform;
            if (t.GetComponentInParent<DollMove>() != null) continue;
            if (t.parent != null && t.parent.CompareTag("Prop")) continue;

            bool hasMesh = false;
            bool staticBatched = false;
            foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.GetComponent<MeshFilter>() == null) continue;
                hasMesh = true;
                if (r.isPartOfStaticBatch) staticBatched = true;
            }
            if (!hasMesh) continue;

            // static 배칭된 메시는 런타임에 원본 메시를 읽을 수 없다 — Batching Static 플래그를 꺼야 후보가 된다
            if (staticBatched)
            {
                Debug.LogWarning($"[DollDisguise] '{go.name}'은(는) Batching Static이라 변장 후보에서 제외합니다.", go);
                continue;
            }
            result.Add(t);
        }

        // 리플레이에서 같은 인덱스가 같은 오브젝트를 가리키도록 이름 → 위치 순으로 고정 정렬 (같은 이름이 많다)
        result.Sort((a, b) =>
        {
            int byName = string.CompareOrdinal(a.name, b.name);
            if (byName != 0) return byName;
            Vector3 pa = a.position, pb = b.position;
            if (pa.x != pb.x) return pa.x.CompareTo(pb.x);
            if (pa.y != pb.y) return pa.y.CompareTo(pb.y);
            return pa.z.CompareTo(pb.z);
        });
        return result;
    }

    // 인형의 원래 메시·콜라이더를 끄고, 원본 Prop의 메시·콜라이더를 같은 상대 배치로 복제해 붙인다
    static void Apply(DollMove doll, Transform source)
    {
        Transform dollRoot = doll.transform;

        foreach (Renderer r in dollRoot.GetComponentsInChildren<Renderer>())
            if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = false;
        foreach (Collider c in dollRoot.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) c.enabled = false;

        // 원본 루트의 위치·회전 기준(스케일 제외)으로 각 부품의 상대 행렬을 구해 인형 위에 그대로 재현
        Matrix4x4 sourceToLocal = Matrix4x4.TRS(source.position, source.rotation, Vector3.one).inverse;
        Matrix4x4 dollToWorld = Matrix4x4.TRS(dollRoot.position, dollRoot.rotation, Vector3.one);
        Dictionary<Transform, GameObject> parts = new Dictionary<Transform, GameObject>();

        GameObject GetPart(Transform src)
        {
            if (parts.TryGetValue(src, out GameObject part)) return part;

            part = new GameObject("Disguise_" + src.name);
            part.tag = "Prop";                      // PlayerInteraction이 맞은 콜라이더의 태그로 판별한다
            part.layer = dollRoot.gameObject.layer; // Interactable 레이어여야 조준 Raycast에 걸린다

            Matrix4x4 world = dollToWorld * sourceToLocal * src.localToWorldMatrix;
            part.transform.SetPositionAndRotation(world.GetColumn(3), world.rotation);
            part.transform.SetParent(dollRoot, true);
            Vector3 parentScale = dollRoot.lossyScale;
            Vector3 wantScale = world.lossyScale;
            part.transform.localScale = new Vector3(
                wantScale.x / parentScale.x, wantScale.y / parentScale.y, wantScale.z / parentScale.z);

            parts[src] = part;
            return part;
        }

        foreach (MeshRenderer srcRenderer in source.GetComponentsInChildren<MeshRenderer>())
        {
            MeshFilter srcFilter = srcRenderer.GetComponent<MeshFilter>();
            if (srcFilter == null || srcFilter.sharedMesh == null) continue;

            GameObject part = GetPart(srcRenderer.transform);
            part.AddComponent<MeshFilter>().sharedMesh = srcFilter.sharedMesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = srcRenderer.sharedMaterials;
            renderer.shadowCastingMode = srcRenderer.shadowCastingMode;
            renderer.receiveShadows = srcRenderer.receiveShadows;
            // 움직이는 물체라 라이트맵 대신 라이트 프로브로 받는다 (원본의 베이크 정보는 그 자리 전용)
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        }

        // 잡기 판정 범위도 원본과 같아야 한다 — 콜라이더 모양이 다르면 프롬프트 위치로 진짜를 가려낼 수 있다
        foreach (Collider srcCollider in source.GetComponentsInChildren<Collider>())
        {
            if (srcCollider.isTrigger || !srcCollider.enabled) continue;
            GameObject part = GetPart(srcCollider.transform);

            switch (srcCollider)
            {
                case BoxCollider box:
                    BoxCollider newBox = part.AddComponent<BoxCollider>();
                    newBox.center = box.center;
                    newBox.size = box.size;
                    break;
                case SphereCollider sphere:
                    SphereCollider newSphere = part.AddComponent<SphereCollider>();
                    newSphere.center = sphere.center;
                    newSphere.radius = sphere.radius;
                    break;
                case CapsuleCollider capsule:
                    CapsuleCollider newCapsule = part.AddComponent<CapsuleCollider>();
                    newCapsule.center = capsule.center;
                    newCapsule.radius = capsule.radius;
                    newCapsule.height = capsule.height;
                    newCapsule.direction = capsule.direction;
                    break;
                case MeshCollider mesh:
                    MeshCollider newMesh = part.AddComponent<MeshCollider>();
                    newMesh.convex = mesh.convex;
                    newMesh.sharedMesh = mesh.sharedMesh;
                    break;
            }
        }

        // 위화감 A "시간이 멈춘 사물" (2026-11-02): 조명·파티클은 '있지만 멈춘' 상태로 복사한다.
        // 메시 쪽은 PropLife(MonoBehaviour)를 복사하지 않으므로 따로 할 일 없이 이미 멈춰 있다
        int frozenLights = CopyFrozenLights(source, GetPart);
        int frozenParticles = CopyFrozenParticles(source, GetPart);

        string frozenNote = frozenLights + frozenParticles > 0
            ? $" (멈춘 조명 {frozenLights}개·파티클 {frozenParticles}개)" : "";
        Debug.Log($"[DollDisguise] {doll.name} → '{source.name}' 모습으로 변장{frozenNote}", doll);
    }

    const float FrozenParticleTime = 1.2f;   // 파티클을 이만큼 흘려보낸 순간에서 멈춘다 (불꽃·김이 한창 피어 있는 모습)

    // 원본의 켜진 조명을 같은 값으로 복사한다. 깜빡임(PropLife)은 따라오지 않는다 → 흔들리지 않는 불빛
    static int CopyFrozenLights(Transform source, System.Func<Transform, GameObject> getPart)
    {
        int count = 0;
        foreach (Light src in source.GetComponentsInChildren<Light>())
        {
            if (!src.enabled) continue;
            Light light = getPart(src.transform).AddComponent<Light>();
            light.type = src.type;
            light.color = src.color;
            light.intensity = src.intensity;   // 복사 시점(Awake)은 깜빡임이 시작되기 전이라 기본 세기
            light.range = src.range;
            light.spotAngle = src.spotAngle;
            light.innerSpotAngle = src.innerSpotAngle;
            light.shadows = src.shadows;       // 그림자 유무가 다르면 그것만으로 진짜가 가려진다
            light.shadowStrength = src.shadowStrength;
            light.renderMode = src.renderMode;
            light.cullingMask = src.cullingMask;
            light.bounceIntensity = src.bounceIntensity;
            count++;
        }
        return count;
    }

    // 원본의 파티클을 복제해 한 순간까지 흘려보낸 뒤 일시정지한다 → 공중에 멈춘 불꽃·김.
    // 복사하지 않으면 '불꽃이 아예 없는' 촛불이 되어 한눈에 정답이 보이므로, 있지만 멈춘 상태로 둔다
    static int CopyFrozenParticles(Transform source, System.Func<Transform, GameObject> getPart)
    {
        int count = 0;
        uint seed = StableHash(source.name);   // 녹화와 리플레이가 같은 멈춘 모양이 되도록 시드 고정

        foreach (ParticleSystem src in source.GetComponentsInChildren<ParticleSystem>())
        {
            if (HasParticleAncestor(src.transform, source)) continue;   // 하위 시스템은 맨 위 시스템과 함께 복제된다

            // 꺼진 그릇 안에서 복제 → 복제본의 MonoBehaviour Awake가 돌지 않은 채로 정리할 수 있다
            GameObject holder = new GameObject("FrozenParticleHolder");
            holder.SetActive(false);
            GameObject clone = Object.Instantiate(src.gameObject, holder.transform, false);
            clone.name = "Frozen_" + src.name;
            StripToParticles(clone);

            uint k = 0;
            foreach (ParticleSystem ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;   // 빈대가 움직여도 불꽃이 몸에 붙어 다니게
                main.playOnAwake = false;
                ps.useAutoRandomSeed = false;
                ps.randomSeed = seed + k++;
            }

            Transform part = getPart(src.transform).transform;
            clone.transform.SetParent(part, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;   // 부품이 이미 원본과 같은 크기
            Object.Destroy(holder);

            ParticleSystem root = clone.GetComponent<ParticleSystem>();
            root.Simulate(FrozenParticleTime, true, true, true);
            root.Pause(true);
            count++;
        }
        return count;
    }

    static bool HasParticleAncestor(Transform t, Transform stopAt)
    {
        if (t == stopAt) return false;
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            if (p.GetComponent<ParticleSystem>() != null) return true;
            if (p == stopAt) break;
        }
        return false;
    }

    // 복제본에서 파티클과 무관한 부품을 모두 뗀다 (메시·콜라이더는 위에서 이미 복사했고, 스크립트가 따라오면 안 된다)
    static void StripToParticles(GameObject clone)
    {
        foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
            Object.DestroyImmediate(mb);   // RequireComponent로 다른 부품을 붙잡고 있을 수 있어 먼저 뗀다
        foreach (Component c in clone.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is Transform || c is ParticleSystem || c is ParticleSystemRenderer) continue;
            Object.DestroyImmediate(c);
        }
    }

    // string.GetHashCode는 실행 환경마다 달라질 수 있다 → 직접 계산하는 FNV-1a
    static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash;
    }
}
