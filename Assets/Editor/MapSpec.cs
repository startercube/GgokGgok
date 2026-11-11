using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Cinemachine;

/// <summary>
/// 방 크기·플레이어 키에서 씬 수치를 일괄 산출해 적용하는 에디터 도구 (Tools > 맵 규격 적용 / MapSpec.ApplyBatch).
/// 사람 치수는 고정이고 방 크기에 비례하는 것은 escapeDistance·sightDistance뿐 — 근거는 Doc/MapSetup.md.
/// </summary>
public static class MapSpec
{
    const string ScenePath = "Assets/Scenes/DollManiaHouse.unity";

    // 인형 프리팹 — doll은 안 움직이는 진짜 오브젝트(프롭), doll_move는 돌아다니며 얼룩을 남기는 인형
    const string DollPropPrefabPath = "Assets/doll.prefab";
    const string DollMovePrefabPath = "Assets/doll_move.prefab";

    // ===== 입력 수치 =====
    // 방·가구·프롭만 키우는 배율 — 플레이어는 그대로라 '거인의 방'이 된다
    const float MapScale = 1.5f;

    const float BaseRoomWidth = 10f;      // X (m)
    const float BaseRoomDepth = 11.3f;    // Z (m)
    const float BaseCeiling = 3.0f;
    const float BaseWallThickness = 0.2f;
    const float BaseDoorWidth = 0.9f;
    const float BaseDoorHeight = 2.0f;

    static float RoomWidth => BaseRoomWidth * MapScale;
    static float RoomDepth => BaseRoomDepth * MapScale;
    static float Ceiling => BaseCeiling * MapScale;
    static float WallThickness => BaseWallThickness * MapScale;
    static float DoorWidth => BaseDoorWidth * MapScale;
    static float DoorHeight => BaseDoorHeight * MapScale;

    // 사람 치수 — 맵 배율과 무관하게 고정
    const float PlayerHeight = 1.45f;  // 초등 4~5학년
    const float CameraFov = 50f;       // 수직 FOV (16:9에서 수평 약 79도)

    // ===== 파생 수치 =====
    static float EyeHeight => PlayerHeight * 0.93f;                       // 1.35
    static float CapsuleRadius => PlayerHeight * 0.125f;                  // 0.18
    static float FollowOffsetY => EyeHeight - PlayerHeight * 0.5f;        // 피벗이 캡슐 중앙일 때
    static float MoveSpeed => EyeHeight * 1.5f;                           // 체감 속도는 눈높이에 비례
    static float EscapeDistance => Mathf.Min(RoomWidth, RoomDepth) * 0.4f;
    static float RoomDiagonal => Mathf.Sqrt(RoomWidth * RoomWidth + RoomDepth * RoomDepth);
    static float SightDistance => RoomDiagonal * 1.2f;

    // 시야각(viewAngle)은 없어짐 — DollVision이 실제 카메라 FOV·화면비로 판정한다 (2026-11-01 「코드 설계 - 빈대 시야 정지」)

    static float HalfW => RoomWidth * 0.5f;
    static float HalfD => RoomDepth * 0.5f;
    static float FrontZ => -HalfD - WallThickness * 0.5f;   // 문이 있는 벽의 Z

    // ===== 진입점 =====

    [MenuItem("Tools/맵 규격 적용")]
    public static void ApplyMenu()
    {
        Run();
    }

    /// <summary>CLI 전용 — 씬을 직접 열고 적용한 뒤 저장한다.</summary>
    public static void ApplyBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log($"씬 로드: {ScenePath}");

            Run();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("씬 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[맵규격] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>CLI 전용 — 방 밖 오브젝트를 새 방 안에 배치한다.</summary>
    public static void PlaceBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log($"씬 로드: {ScenePath}");

            PlaceLevelObjects();
            Report();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("씬 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[맵규격] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>CLI 전용 — 인형·얼룩을 실제 치수로 축소한다.</summary>
    public static void ScaleBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log($"씬 로드: {ScenePath}");

            ScaleLevelObjects();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("씬 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[맵규격] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>CLI 전용 — 배치 + 크기 정규화를 순서대로 수행한다. 둘 다 재실행 안전(idempotent).</summary>
    public static void PlaceAndScaleBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log($"씬 로드: {ScenePath}");

            PlaceLevelObjects();
            ScaleLevelObjects();
            Report();
            VerifySizes();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("씬 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[맵규격] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>CLI 전용 — NavMeshSurface를 다시 베이크하고 데이터 에셋을 저장한다.</summary>
    public static void BakeBatch()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log($"씬 로드: {ScenePath}");

            BakeNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("씬 저장 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[맵규격] 실패: {e}");
            EditorApplication.Exit(1);
        }
    }

    static void BakeNavMesh()
    {
        var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces.Length == 0) { Warn("NavMeshSurface 없음 — 베이크 불가"); return; }

        // 에디터 베이크와 같은 위치(씬 이름 폴더)에 데이터 에셋을 저장한다
        string dir = ScenePath.Substring(0, ScenePath.Length - ".unity".Length);
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'),
                                       System.IO.Path.GetFileName(dir));

        foreach (var surface in surfaces)
        {
            surface.BuildNavMesh();
            if (surface.navMeshData == null) { Warn($"{surface.name}: 베이크 결과가 비어 있습니다"); continue; }

            string path = $"{dir}/NavMesh-{surface.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            EditorUtility.SetDirty(surface);
            Log($"{surface.name}: NavMesh 베이크 완료 → {path}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>읽기 전용 점검 — 씬을 저장하지 않는다.</summary>
    [MenuItem("Tools/맵 규격 점검")]
    public static void DiagnoseBatch()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Report();
        VerifySizes();
    }

    static void VerifySizes()
    {
        Log("--- 실측 확인 ---");

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var cap = player.GetComponent<CapsuleCollider>();
            float feet = player.transform.position.y - (cap != null ? cap.height * 0.5f : 0f);
            Log($"플레이어 콜라이더: 키 {(cap != null ? cap.height : 0f):F2}m, 발 높이 {feet:F3}m, Scale {player.transform.localScale}");

            // 콜라이더와 별개로 '눈에 보이는' 메시 크기를 따로 잰다
            var pr = player.GetComponentsInChildren<Renderer>();
            if (pr.Length > 0)
            {
                Bounds pb = pr[0].bounds;
                for (int i = 1; i < pr.Length; i++) pb.Encapsulate(pr[i].bounds);
                Log($"플레이어 메시: 크기 {pb.size}, 발 높이 {pb.min.y:F3}m, 머리 높이 {pb.max.y:F3}m");
                if (Mathf.Abs(pb.size.y - PlayerHeight) > 0.15f)
                    Warn($"메시 높이 {pb.size.y:F2}m 가 목표 {PlayerHeight}m 와 다릅니다");
            }
            else Log("플레이어 메시: Renderer 없음 (기즈모 아이콘만 보이는 상태)");

            // 메시가 루트에 있는지 자식에 있는지에 따라 고칠 방법이 달라진다
            foreach (var r in pr)
                Log($"  Renderer 위치: '{r.gameObject.name}' (루트여부 {r.gameObject == player}), " +
                    $"localPos {r.transform.localPosition}, localScale {r.transform.localScale}, 메시 {(r as MeshRenderer != null ? r.GetComponent<MeshFilter>()?.sharedMesh?.name : "?")}");
            Log($"  Player 자식 수: {player.transform.childCount}");
            for (int i = 0; i < player.transform.childCount; i++)
                Log($"    자식[{i}] {player.transform.GetChild(i).name}");
        }

        // 비교 기준 — 벽과 대표 가구
        foreach (var n in new[] { "Wall_North", "옷장", "블록성", "책상", "침대" })
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            var r = go.GetComponent<Renderer>();
            if (r != null) Log($"{n}: 높이 {r.bounds.size.y:F2}m (윗면 {r.bounds.max.y:F2}m)");
        }

        foreach (var doll in Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None))
        {
            var rends = doll.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Log($"{doll.name}: 높이 {b.size.y:F2}m, 발 높이 {b.min.y:F3}m, 위치 ({b.center.x:F2}, {b.center.z:F2})");
        }
    }

    static void Run()
    {
        Log("=========== 맵 규격 적용 시작 ===========");
        Log($"방 {RoomWidth}x{RoomDepth}m / 천장 {Ceiling}m / 플레이어 {PlayerHeight}m (눈높이 {EyeHeight:F2}m)");

        RebuildRoom();
        ApplyPlayer();
        ApplyCamera();
        ReplaceMovingDolls();
        ApplyDolls();
        ApplyDoor();
        BuildFurniture();
        BuildProps();
        PlaceLevelObjects();   // 레이아웃이 방 크기 비율로 정의돼 있어 자동으로 따라감
        ScaleLevelObjects();
        Report();
        VerifySizes();

        Log("=========== 완료 ===========");
    }

    // ===== 방 구조 =====

    static void RebuildRoom()
    {
        GameObject map = GameObject.Find("Map");
        if (map == null)
        {
            map = new GameObject("Map");
            Log("Map 오브젝트 생성");
        }
        // 자식 좌표 = 월드 좌표가 되도록 부모를 원점에 고정
        map.transform.position = Vector3.zero;
        map.transform.rotation = Quaternion.identity;
        map.transform.localScale = Vector3.one;

        // --- 벽 ---
        Transform wallRoot = map.transform.Find("Wall");
        if (wallRoot == null)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(map.transform, false);
            wallRoot = go.transform;
        }
        wallRoot.localPosition = Vector3.zero;
        wallRoot.localRotation = Quaternion.identity;
        wallRoot.localScale = Vector3.one;

        int removed = wallRoot.childCount;
        for (int i = wallRoot.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(wallRoot.GetChild(i).gameObject);
        Log($"기존 벽 {removed}개 제거");

        float y = Ceiling * 0.5f;
        float t = WallThickness;

        // Cube는 원본이 1유닛이라 Scale 값이 곧 미터
        MakeWall(wallRoot, "Wall_West", new Vector3(-HalfW - t * 0.5f, y, 0f), new Vector3(t, Ceiling, RoomDepth + t * 2f));
        MakeWall(wallRoot, "Wall_East", new Vector3(HalfW + t * 0.5f, y, 0f), new Vector3(t, Ceiling, RoomDepth + t * 2f));
        MakeWall(wallRoot, "Wall_North", new Vector3(0f, y, HalfD + t * 0.5f), new Vector3(RoomWidth, Ceiling, t));

        // 앞 벽은 문 구멍을 남기고 3조각
        float gapHalf = DoorWidth * 0.5f;
        float sideW = HalfW - gapHalf;
        if (sideW > 0.01f)
        {
            MakeWall(wallRoot, "Wall_South_L", new Vector3(-gapHalf - sideW * 0.5f, y, FrontZ), new Vector3(sideW, Ceiling, t));
            MakeWall(wallRoot, "Wall_South_R", new Vector3(gapHalf + sideW * 0.5f, y, FrontZ), new Vector3(sideW, Ceiling, t));
        }
        float lintel = Ceiling - DoorHeight;
        if (lintel > 0.01f)
            MakeWall(wallRoot, "Wall_South_Top", new Vector3(0f, DoorHeight + lintel * 0.5f, FrontZ), new Vector3(DoorWidth, lintel, t));

        Log($"벽 {wallRoot.childCount}장 생성 (두께 {t}m, 높이 {Ceiling}m)");

        // --- 바닥 (Plane 원본은 10x10이라 /10) ---
        Transform plane = map.transform.Find("Plane");
        if (plane == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Plane";
            go.transform.SetParent(map.transform, false);
            plane = go.transform;
        }
        plane.localPosition = Vector3.zero;
        plane.localRotation = Quaternion.identity;
        plane.localScale = new Vector3(RoomWidth / 10f, 1f, RoomDepth / 10f);
        Log($"바닥 Plane Scale = {plane.localScale}");

        // --- 문 밖 바닥 (ExitZone 진입 후 낙하 방지) ---
        Transform outside = map.transform.Find("OutsideFloor");
        if (outside == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OutsideFloor";
            go.transform.SetParent(map.transform, false);
            outside = go.transform;
        }
        outside.localScale = new Vector3(RoomWidth, 0.2f * MapScale, 3f * MapScale);
        outside.localPosition = new Vector3(0f, -0.1f * MapScale, FrontZ - 1.5f * MapScale);
        Log("문 밖 바닥 생성 (낙하 방지)");
    }

    static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
    }

    // ===== 플레이어 =====

    static void ApplyPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) { Warn("Player 태그 오브젝트를 찾지 못했습니다"); return; }

        // 키는 Transform Scale이 아니라 콜라이더로 표현해야 메시/물리가 왜곡되지 않는다
        player.transform.localScale = Vector3.one;

        var capsule = player.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.height = PlayerHeight;
            capsule.radius = CapsuleRadius;
            capsule.center = Vector3.zero;
            capsule.direction = 1;   // Y축
            Log($"CapsuleCollider: height {PlayerHeight} / radius {CapsuleRadius:F3}");
        }
        else Warn("Player에 CapsuleCollider가 없습니다");

        // 발이 바닥(y=0)에 닿도록, 문 안쪽에 배치
        player.transform.position = new Vector3(0f, PlayerHeight * 0.5f, FrontZ + 1.5f * MapScale);
        player.transform.rotation = Quaternion.identity;

        ApplyPlayerVisual(player);

        var move = player.GetComponent<PlayerMove>();
        if (move != null)
        {
            SetFloat(move, "moveSpeed", MoveSpeed);
            Log($"moveSpeed = {MoveSpeed:F2} m/s");
        }
        else Warn("Player에 PlayerMove가 없습니다");

        EditorUtility.SetDirty(player);
    }

    const float HeadHeight = 0.45f;   // 스타일화된 아동 비율 (키의 약 1/3.2)

    /// <summary>머리 메시가 피벗 오프셋 탓에 콜라이더 위로 떠 보인다 — 자식으로 옮겨 맞추고 몸통을 붙인다.</summary>
    static void ApplyPlayerVisual(GameObject player)
    {
        float feetY = player.transform.position.y - PlayerHeight * 0.5f;

        Transform visual = player.transform.Find("Visual");
        if (visual == null)
        {
            var go = new GameObject("Visual");
            go.transform.SetParent(player.transform, false);
            visual = go.transform;
        }
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        // 루트에 메시가 남아 있으면 자식으로 옮긴다 (컴포넌트는 이동이 안 되므로 복제 후 제거)
        var rootFilter = player.GetComponent<MeshFilter>();
        var rootRend = player.GetComponent<MeshRenderer>();
        if (rootFilter != null && rootRend != null)
        {
            var head = new GameObject("Head");
            head.transform.SetParent(visual, false);
            head.AddComponent<MeshFilter>().sharedMesh = rootFilter.sharedMesh;
            head.AddComponent<MeshRenderer>().sharedMaterials = rootRend.sharedMaterials;

            Object.DestroyImmediate(rootRend);
            Object.DestroyImmediate(rootFilter);
            Log($"머리 메시 '{head.name}'를 자식으로 이동");
        }

        Transform headT = visual.Find("Head");
        if (headT != null)
        {
            // 1) 머리 크기를 목표에 맞춤
            Vector3 size = MeasureSize(headT);
            if (size.y > 0.0001f) headT.localScale *= HeadHeight / size.y;

            // 2) 머리 윗면이 정확히 키 높이에 오도록 내림
            var r = headT.GetComponentInChildren<Renderer>();
            if (r != null)
                headT.position += Vector3.up * (feetY + PlayerHeight - r.bounds.max.y);
        }

        // 몸통 — CCTV 리플레이에서 플레이어가 보이려면 몸이 필요하다
        Transform body = visual.Find("Body");
        if (body == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Body";
            go.transform.SetParent(visual, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());   // 물리는 루트 CapsuleCollider가 담당
            body = go.transform;
        }
        float bodyH = PlayerHeight - HeadHeight;
        float w = CapsuleRadius * 2f;
        body.localScale = new Vector3(w, bodyH * 0.5f, w);
        body.position = new Vector3(player.transform.position.x, feetY + bodyH * 0.5f, player.transform.position.z);

        Log($"플레이어 비주얼: 머리 {HeadHeight}m + 몸통 {bodyH:F2}m = {PlayerHeight}m");
    }

    // ===== 카메라 =====

    static void ApplyCamera()
    {
        var cams = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        if (cams.Length == 0) { Warn("CinemachineCamera를 찾지 못했습니다"); return; }

        foreach (var cam in cams)
        {
            cam.Lens.FieldOfView = CameraFov;
            cam.Lens.NearClipPlane = 0.05f;   // 프롭에 가까이 붙어도 잘리지 않도록
            EditorUtility.SetDirty(cam);

            if (cam.name.Contains("Player"))
            {
                var follow = cam.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    follow.FollowOffset = new Vector3(0f, FollowOffsetY, 0f);
                    EditorUtility.SetDirty(follow);
                    Log($"{cam.name}: Follow Offset Y = {FollowOffsetY:F3} (눈높이 {EyeHeight:F2}m)");
                }
                else Warn($"{cam.name}에 CinemachineFollow가 없습니다");
            }
        }
        Log($"카메라 {cams.Length}대: FOV {CameraFov}, Near Clip 0.05");
    }

    // ===== 인형 =====

    /// <summary>손으로 만든 DollMove를 doll_move 프리팹 인스턴스로 교체 (이미 프리팹이면 건너뜀 — 재실행 안전).</summary>
    static void ReplaceMovingDolls()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollMovePrefabPath);
        if (prefab == null) { Warn($"{DollMovePrefabPath} 를 찾지 못했습니다"); return; }

        int replaced = 0;
        foreach (var doll in Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None))
        {
            var go = doll.gameObject;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null && AssetDatabase.GetAssetPath(source) == DollMovePrefabPath) continue;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform.parent);
            inst.name = go.name;
            inst.transform.position = go.transform.position;
            inst.transform.rotation = Quaternion.identity;

            Object.DestroyImmediate(go);
            replaced++;
        }
        if (replaced > 0) Log($"움직이는 인형 {replaced}마리를 doll_move 프리팹으로 교체");
    }

    static void ApplyDolls()
    {
        var dolls = Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        if (dolls.Length == 0) { Warn("DollMove를 찾지 못했습니다"); return; }

        foreach (var doll in dolls)
        {
            doll.escapeDistance = EscapeDistance;
            EditorUtility.SetDirty(doll);

            // 시야 거리는 공용 설정 에셋(DollVisionSettings)으로 옮겨져 빈대마다 넣지 않는다 (2026-11-01).
            // 방 대각선 × 1.2는 "방 안 어디든 시야 거리에 걸리지 않게"라는 하한 — 공용 값이 이를 못 덮으면 알린다
            var vision = doll.GetComponent<DollVision>();
            if (vision == null)
                Warn($"{doll.name}: DollVision이 없습니다");
            else if (vision.Settings.sightDistance < SightDistance)
                Warn($"{doll.name}: 시야 거리 {vision.Settings.sightDistance:F1}m가 이 방에 필요한 {SightDistance:F1}m보다 짧습니다. " +
                     "Resources/DollVisionSettings 값을 올리거나 이 맵 빈대에 settingsOverride 에셋을 끼우세요");

            // 인형이 가구 밑·선반 위로 다닐 수 있도록 에이전트를 작게
            var agent = doll.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.radius = DollAgentRadius;
                agent.height = DollAgentHeight;
                agent.speed = 1.2f * MapScale;
                agent.stoppingDistance = 0.1f * MapScale;
                EditorUtility.SetDirty(agent);
            }
        }
        Log($"인형 {dolls.Length}마리: escape {EscapeDistance:F1}m / 필요 시야 거리 {SightDistance:F1}m (공용 {DollVisionSettings.Default.sightDistance:F1}m)");
    }

    // ===== 문 · 탈출 구역 =====

    static void ApplyDoor()
    {
        var door = Object.FindFirstObjectByType<DoorController>();
        if (door == null) { Warn("DoorController를 찾지 못했습니다"); return; }

        door.transform.position = new Vector3(0f, DoorHeight * 0.5f, FrontZ);
        door.transform.rotation = Quaternion.identity;
        door.transform.localScale = new Vector3(DoorWidth, DoorHeight, WallThickness * 0.6f);

        // 문이 완전히 비켜나려면 이동 거리가 문 폭 이상이어야 한다
        var so = new SerializedObject(door);
        var offset = so.FindProperty("openOffset");
        if (offset != null) offset.vector3Value = new Vector3(-(DoorWidth + 0.1f), 0f, 0f);
        so.ApplyModifiedProperties();

        Log($"문: {DoorWidth}x{DoorHeight}m, openOffset -{DoorWidth + 0.1f:F2}");

        var exit = Object.FindFirstObjectByType<ExitZone>(FindObjectsInactive.Include);
        if (exit != null)
        {
            exit.transform.position = new Vector3(0f, 0.75f * MapScale, FrontZ - 1.5f * MapScale);
            exit.transform.rotation = Quaternion.identity;
            exit.transform.localScale = Vector3.one * (1.5f * MapScale);

            var col = exit.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            EditorUtility.SetDirty(exit);
            Log("ExitZone: 문 밖으로 이동, Is Trigger 확인");
        }
        else Warn("ExitZone을 찾지 못했습니다");
    }

    // ===== 가구 =====

    /// <summary>가구 한 점. 위치는 방 크기 대비 비율(-1~1)이라 방이 커져도 따라간다. spot=true면 윗면에 은신처+링크 생성.</summary>
    struct Furn
    {
        public string name;
        public float fx, fz;         // 위치 비율 (x/HalfW, z/HalfD)
        public float sx, sy, sz;     // 실제 크기 (m)
        public float baseY;          // 바닥에서 띄운 높이 (벽 선반용)
        public bool spot;            // 윗면을 은신처로 쓸지

        public Furn(string n, float fx, float fz, float sx, float sy, float sz, float baseY = 0f, bool spot = true)
        { name = n; this.fx = fx; this.fz = fz; this.sx = sx; this.sy = sy; this.sz = sz; this.baseY = baseY; this.spot = spot; }

        // 가구 치수는 배율 1.0 기준으로 적어두고, 여기서 MapScale을 곱해 쓴다
        public float SX => sx * MapScale;
        public float SY => sy * MapScale;
        public float SZ => sz * MapScale;
        public float BaseYS => baseY * MapScale;

        public Vector3 Center => new Vector3(fx * HalfW, BaseYS + SY * 0.5f, fz * HalfD);
        public Vector3 Size => new Vector3(SX, SY, SZ);
        public Vector3 TopSpot => new Vector3(fx * HalfW, BaseYS + SY, fz * HalfD);
        public float Footprint => Mathf.Max(SX, SZ) * 0.5f;
    }

    // 아이 방 실제 가구 치수 기준. 높이 계층(T1 0.3~0.6 / T2 0.7~0.9 / T3 1.1~) 골고루 섞음
    static readonly Furn[] Furniture =
    {
        // 은신처로 쓸 윗면은 MinSpotSize 이상이어야 NavMesh가 깔린다 (책장이 실제보다 깊은 이유)

        // --- 뒷벽 ---
        new Furn("옷장",        -0.80f,  0.93f, 1.6f, 2.00f, 0.70f),
        new Furn("책장_높음1",  -0.44f,  0.93f, 0.9f, 1.80f, 0.65f),
        new Furn("책장_높음2",  -0.24f,  0.93f, 0.9f, 1.80f, 0.65f),
        new Furn("책상",         0.20f,  0.94f, 1.4f, 0.75f, 0.65f),
        new Furn("벽선반_하",    0.64f,  0.93f, 1.2f, 0.08f, 0.65f, 1.40f),
        new Furn("벽선반_상",    0.64f,  0.93f, 1.2f, 0.08f, 0.65f, 1.75f),

        // --- 좌벽 ---
        new Furn("침대",        -0.84f,  0.39f, 1.1f, 0.45f, 2.00f),
        new Furn("서랍장",      -0.90f,  0.04f, 0.7f, 0.80f, 1.00f),
        new Furn("책장_낮음1",  -0.92f, -0.28f, 0.65f, 0.90f, 1.40f),
        new Furn("정리함1",     -0.88f, -0.57f, 0.7f, 0.50f, 0.90f),

        // --- 우벽 ---
        new Furn("책장_높음3",   0.92f,  0.62f, 0.65f, 1.80f, 1.00f),
        new Furn("책상2",        0.92f,  0.32f, 0.65f, 0.75f, 1.40f),
        new Furn("정리함2",      0.90f,  0.00f, 0.7f, 0.50f, 0.80f),
        new Furn("책장_낮음2",   0.92f, -0.32f, 0.65f, 0.90f, 1.40f),

        // --- 중앙 ---
        new Furn("러그",         0.00f,  0.27f, 2.6f, 0.02f, 2.20f, 0f, false),
        new Furn("티테이블",     0.00f,  0.27f, 0.7f, 0.40f, 0.70f),
        new Furn("빈백",        -0.30f,  0.05f, 0.8f, 0.60f, 0.80f),
        new Furn("장난감상자",   0.32f,  0.04f, 0.9f, 0.55f, 0.60f),

        // --- 시야 차단 섬 --- 눈높이보다 높고 직각으로 엇갈려, 어느 지점에서도 방 전체가 보이지 않는다
        new Furn("블록성",       0.44f,  0.50f, 1.0f, 1.80f, 1.00f),
        new Furn("인형진열장1", -0.46f,  0.30f, 1.6f, 1.80f, 0.70f),
        new Furn("인형진열장2",  0.34f, -0.30f, 0.7f, 1.80f, 1.60f),
        new Furn("파티션",       0.10f,  0.78f, 1.8f, 1.70f, 0.35f, 0f, false),   // 시야 차단 전용 (얇아서 못 올라감)

        // --- 문 근처 (중앙은 플레이어 스폰이라 비움) ---
        new Furn("정리함3",     -0.68f, -0.81f, 0.9f, 0.50f, 0.50f),
        new Furn("정리함4",      0.68f, -0.81f, 0.9f, 0.50f, 0.50f),
    };

    // 가구와 무관한 순수 바닥 은신처 (T0)
    static readonly Vector2[] FloorSpots =
    {
        new Vector2(-0.30f, -0.12f),
        new Vector2( 0.00f,  0.67f),
        new Vector2(-0.84f,  0.64f),
    };

    static void BuildFurniture()
    {
        Log("--- 가구 배치 ---");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Warn("Map을 찾지 못했습니다"); return; }

        Transform root = map.transform.Find("Furniture");
        if (root == null)
        {
            var go = new GameObject("Furniture");
            go.transform.SetParent(map.transform, false);
            root = go.transform;
        }
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject);

        Material mat = GetFurnitureMaterial();

        foreach (var f in Furniture)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = f.name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = f.Center;
            go.transform.localScale = f.Size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);

            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }
        Log($"가구 {Furniture.Length}점 생성");
    }

    static Material GetFurnitureMaterial()
    {
        return GetMaterial("Assets/Material/Furniture.mat", new Color(0.72f, 0.58f, 0.44f));   // 나무색
    }

    /// <summary>은신처로 쓸 수 있는 윗면 최소 크기 — NavMesh가 가장자리에서 Agent Radius만큼 물러나 생기기 때문.</summary>
    static float MinSpotSize => DollAgentRadius * 2f + 0.3f;

    /// <summary>가구 윗면 링크. 끝점을 중앙이 아닌 '방 중앙 쪽 가장자리'로 — 중앙으로 이으면 직선이 가구를 관통한다.</summary>
    static void BuildLink(Transform root, string name, Furn f)
    {
        Vector3 top = f.TopSpot;
        Vector3 flat = new Vector3(top.x, 0f, top.z);

        Vector3 dir = -flat;   // 가구에서 방 중앙을 향하는 방향
        dir = dir.sqrMagnitude < 0.01f ? Vector3.back : dir.normalized;

        // dir 방향으로 가구 AABB 경계까지의 거리
        float tx = Mathf.Abs(dir.x) > 0.0001f ? (f.SX * 0.5f) / Mathf.Abs(dir.x) : float.MaxValue;
        float tz = Mathf.Abs(dir.z) > 0.0001f ? (f.SZ * 0.5f) / Mathf.Abs(dir.z) : float.MaxValue;
        float extent = Mathf.Min(tx, tz);

        // 끝점: 윗면 가장자리에서 Agent Radius만큼 안쪽 (그 안쪽부터 NavMesh가 있음)
        Vector3 end = top + dir * Mathf.Max(0f, extent - DollAgentRadius);
        // 시작점: 가구 발치에서 조금 떨어진 바닥
        Vector3 start = flat + dir * (extent + 0.4f * MapScale);

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = start;
        go.transform.localRotation = Quaternion.identity;

        var link = go.AddComponent<NavMeshLink>();
        link.startPoint = Vector3.zero;
        link.endPoint = end - start;   // 로컬 = 월드 (회전·스케일 없음)
        link.width = Mathf.Min(0.35f * MapScale, f.SX, f.SZ) * 0.8f;
        link.bidirectional = true;
        link.agentTypeID = 0;
    }

    // ===== 프롭 (인형매니아 하우스 테마) =====

    struct PropDef
    {
        public string name;
        public PrimitiveType prim;
        public Vector3 size;    // 월드 실측 크기 (가로, 높이, 세로)
        public int mat;         // 0 = 인형 계열, 1 = 소품 계열

        public PropDef(string n, PrimitiveType p, float w, float h, float d, int m)
        { name = n; prim = p; size = new Vector3(w, h, d); mat = m; }
    }

    // 인형(0.4m)과 같은 크기의 프롭이 가장 많아야 위장이 성립한다.
    // mat 0(인형 계열)은 프리미티브가 아니라 doll 프리팹으로 생성되며, 치수는 실측 비율(1 : 0.68 : 1.0) 기준.
    static readonly PropDef[] BigSurfaceProps =
    {
        new PropDef("인형",     PrimitiveType.Capsule,  0.27f, 0.40f, 0.40f, 0),   // ← 진짜 인형과 동일 치수
        new PropDef("인형",     PrimitiveType.Capsule,  0.27f, 0.40f, 0.40f, 0),
        new PropDef("인형",     PrimitiveType.Capsule,  0.27f, 0.40f, 0.40f, 0),
        new PropDef("큰인형",   PrimitiveType.Capsule,  0.44f, 0.65f, 0.65f, 0),
        new PropDef("미니인형", PrimitiveType.Capsule,  0.17f, 0.25f, 0.25f, 0),
        new PropDef("공",       PrimitiveType.Sphere,   0.22f, 0.22f, 0.22f, 1),
        new PropDef("블록",     PrimitiveType.Cube,     0.14f, 0.14f, 0.14f, 1),
    };

    // 선반·티테이블처럼 좁은 면에는 작은 것만
    static readonly PropDef[] SmallSurfaceProps =
    {
        new PropDef("미니인형", PrimitiveType.Capsule,  0.17f, 0.25f, 0.25f, 0),
        new PropDef("미니인형", PrimitiveType.Capsule,  0.17f, 0.25f, 0.25f, 0),
        new PropDef("찻잔",     PrimitiveType.Cylinder, 0.08f, 0.08f, 0.08f, 1),
        new PropDef("주전자",   PrimitiveType.Cylinder, 0.16f, 0.18f, 0.16f, 1),
        new PropDef("블록",     PrimitiveType.Cube,     0.12f, 0.12f, 0.12f, 1),
    };

    static readonly PropDef BearProp = new PropDef("거대한곰인형", PrimitiveType.Capsule, 0.82f, 1.20f, 1.20f, 0);
    static readonly PropDef TrayProp = new PropDef("쟁반", PrimitiveType.Cylinder, 0.34f, 0.02f, 0.34f, 1);

    static void BuildProps()
    {
        Log("--- 프롭 배치 (인형매니아 하우스) ---");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Warn("Map을 찾지 못했습니다"); return; }

        Transform root = map.transform.Find("Props");
        if (root == null)
        {
            var go = new GameObject("Props");
            go.transform.SetParent(map.transform, false);
            root = go.transform;
        }
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject);

        Random.InitState(20260729);   // 실행할 때마다 같은 배치가 나오도록 고정
        int count = 0;

        // --- 가구 윗면 ---
        foreach (var f in Furniture)
        {
            if (!f.spot) continue;

            float area = f.sx * f.sz;
            bool small = area < 0.35f || f.sy > 1.5f;              // 좁은 면 또는 아주 높은 곳
            var table = small ? SmallSurfaceProps : BigSurfaceProps;
            // 레퍼런스(인형매니아 방)처럼 선반·침대마다 인형이 빽빽해야 위장이 성립한다
            int n = Mathf.Clamp(Mathf.RoundToInt(area * 6f), 2, 7);

            for (int i = 0; i < n; i++)
            {
                var def = table[Random.Range(0, table.Length)];

                // 가장자리로 떨어지지 않게 안쪽으로 여유를 둔다 (전부 스케일 적용된 값)
                float mx = Mathf.Max(0f, f.SX * 0.5f - def.size.x * MapScale * 0.5f - 0.05f * MapScale);
                float mz = Mathf.Max(0f, f.SZ * 0.5f - def.size.z * MapScale * 0.5f - 0.05f * MapScale);
                Vector3 p = f.TopSpot + new Vector3(Random.Range(-mx, mx), 0f, Random.Range(-mz, mz));

                // 웨이포인트(가구 정중앙)와 겹치면 살짝 밀어낸다 — 인형 스폰 자리 확보
                if (new Vector2(p.x - f.TopSpot.x, p.z - f.TopSpot.z).magnitude < 0.18f * MapScale)
                    p += new Vector3(0.22f, 0f, 0.18f) * MapScale;

                SpawnProp(root, def, p);
                count++;
            }
        }

        // --- 티세트: 티테이블 위에 쟁반 한 장 ---
        foreach (var f in Furniture)
        {
            if (f.name != "티테이블") continue;
            SpawnProp(root, TrayProp, f.TopSpot);
            count++;
        }

        // --- 거대한 곰 인형 (테마 랜드마크) ---
        SpawnProp(root, BearProp, new Vector3(0.55f * HalfW, 0f, -0.45f * HalfD));
        count++;

        // --- 바닥에 흩뿌리기 ---
        int placed = 0, guard = 0;
        while (placed < 16 && guard++ < 400)
        {
            var def = BigSurfaceProps[Random.Range(0, BigSurfaceProps.Length)];
            Vector3 p = new Vector3(
                Random.Range(-HalfW + 0.5f * MapScale, HalfW - 0.5f * MapScale), 0f,
                Random.Range(-HalfD + 0.5f * MapScale, HalfD - 0.5f * MapScale));

            if (IsBlocked(p)) continue;

            SpawnProp(root, def, p);
            placed++; count++;
        }

        Log($"프롭 {count}개 생성 (가구 위 + 바닥 {placed}개)");
    }

    /// <summary>가구 위이거나 문 앞 플레이어 동선이면 바닥 배치를 피한다.</summary>
    static bool IsBlocked(Vector3 p)
    {
        // 플레이어 스폰 · 문 통로
        if (Mathf.Abs(p.x) < 1.1f * MapScale && p.z < -0.45f * HalfD) return true;

        foreach (var f in Furniture)
        {
            Vector3 c = f.Center;
            if (Mathf.Abs(p.x - c.x) < f.SX * 0.5f + 0.35f * MapScale &&
                Mathf.Abs(p.z - c.z) < f.SZ * 0.5f + 0.35f * MapScale) return true;
        }
        return false;
    }

    /// <summary>프롭 하나를 생성한다. basePos.y가 바닥면이 되도록 올려 배치.</summary>
    static void SpawnProp(Transform root, PropDef def, Vector3 basePos)
    {
        GameObject go;

        if (def.mat == 0)
        {
            // 인형 계열은 안 움직이는 진짜 인형 프리팹(doll)을 쓴다 — 움직이는 doll_move와 겉모습이 같아야 위장이 성립한다
            go = SpawnDollPrefab(root, def.name, basePos, def.size.y * MapScale);
            if (go == null) return;
        }
        else
        {
            go = GameObject.CreatePrimitive(def.prim);
            go.name = def.name;
            go.transform.SetParent(root, false);

            Vector3 s = def.size * MapScale;   // 프롭도 맵 배율을 따라간다

            // Capsule·Cylinder 원본은 높이 2유닛이라 절반으로 나눠야 실측 높이가 맞는다
            bool tall = def.prim == PrimitiveType.Capsule || def.prim == PrimitiveType.Cylinder;
            go.transform.localScale = new Vector3(s.x, tall ? s.y * 0.5f : s.y, s.z);
            go.transform.localPosition = basePos + Vector3.up * (s.y * 0.5f);
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var mat = GetMaterial("Assets/Material/Prop_Misc.mat", new Color(0.62f, 0.78f, 0.88f));
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // 조준·잡기 대상이 되려면 태그와 레이어가 모두 필요하다
        go.tag = "Prop";
        SetLayerRecursive(go, LayerMask.NameToLayer("Interactable"));

        // 프롭이 베이크에 들어가면 NavMesh를 파내 좁은 가구 윗면이 통째로 사라진다
        ExcludeFromNavMesh(go);
    }

    /// <summary>doll 프리팹을 목표 높이로 스케일하고, basePos가 발밑이 되도록 배치한다.</summary>
    static GameObject SpawnDollPrefab(Transform root, string name, Vector3 basePos, float height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPropPrefabPath);
        if (prefab == null) { Warn($"{DollPropPrefabPath} 를 찾지 못했습니다"); return null; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        go.name = name;
        go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = Vector3.one;

        ScaleTo(go.transform, height, true);
        PlaceOnFloor(go.transform, basePos, false);   // 회전은 유지한 채 발만 바닥에 맞춘다
        return go;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    /// <summary>NavMesh 베이크에서 제외한다. 레이어 마스크 설정과 무관하게 오브젝트 자체에 박히는 표식.</summary>
    static void ExcludeFromNavMesh(GameObject go)
    {
        var mod = go.GetComponent<NavMeshModifier>();
        if (mod == null) mod = go.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;
    }

    static Material GetMaterial(string path, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return null;

        var mat = new Material(sh) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ===== 레벨 오브젝트 배치 =====

    static readonly Vector2[] StainLayout =
    {
        new Vector2(-0.45f,  0.45f),
        new Vector2( 0.50f, -0.15f),
        new Vector2( 0.15f,  0.62f),
    };

    static void PlaceLevelObjects()
    {
        Log("--- 레벨 오브젝트 배치 ---");

        ResetGroup("WayPointGroup");
        ResetGroup("DollGroup");
        ResetGroup("StainGroup");

        // --- 은신처 목록: 가구 윗면 + 순수 바닥 ---
        var spots = new System.Collections.Generic.List<Vector3>();
        var spotFurn = new System.Collections.Generic.List<int>();   // -1 = 바닥 은신처

        for (int fi = 0; fi < Furniture.Length; fi++)
        {
            var f = Furniture[fi];
            if (!f.spot) continue;

            // 윗면이 좁으면 NavMesh가 생기지 않아 링크 끝점이 바닥으로 스냅된다 → 은신처로 쓸 수 없음
            float minSide = Mathf.Min(f.SX, f.SZ);
            if (minSide < MinSpotSize)
            {
                Warn($"{f.name}: 윗면 최소변 {minSide:F2}m < {MinSpotSize:F2}m — NavMesh가 안 깔려 은신처에서 제외");
                continue;
            }

            spots.Add(f.TopSpot);
            spotFurn.Add(fi);
        }
        foreach (var f in FloorSpots)
        {
            spots.Add(new Vector3(f.x * HalfW, 0f, f.y * HalfD));
            spotFurn.Add(-1);
        }

        // --- 웨이포인트: 은신처 수에 맞춰 새로 구성 ---
        var wpGroup = GameObject.Find("WayPointGroup");
        if (wpGroup == null) wpGroup = new GameObject("WayPointGroup");
        wpGroup.transform.position = Vector3.zero;
        wpGroup.transform.rotation = Quaternion.identity;
        wpGroup.transform.localScale = Vector3.one;

        for (int i = wpGroup.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(wpGroup.transform.GetChild(i).gameObject);

        // 그룹 밖에 흩어져 있던 웨이포인트도 정리
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t != null && t.name.StartsWith("WayPoint") && t.name != "WayPointGroup")
                Object.DestroyImmediate(t.gameObject);

        var wps = new Transform[spots.Count];
        for (int i = 0; i < spots.Count; i++)
        {
            var go = new GameObject($"WayPoint_{i:00}");
            go.transform.SetParent(wpGroup.transform, false);
            go.transform.localPosition = spots[i];
            wps[i] = go.transform;
        }
        Log($"웨이포인트 {wps.Length}개 생성 (가구 윗면 {spots.Count - FloorSpots.Length} + 바닥 {FloorSpots.Length})");

        // --- NavMeshLink: 바닥에서 높은 은신처로 올라가는 통로 ---
        GameObject map = GameObject.Find("Map");
        Transform linkRoot = map != null ? map.transform.Find("NavLinks") : null;
        if (linkRoot == null && map != null)
        {
            var go = new GameObject("NavLinks");
            go.transform.SetParent(map.transform, false);
            linkRoot = go.transform;
        }
        if (linkRoot != null)
        {
            linkRoot.localPosition = Vector3.zero;
            for (int i = linkRoot.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(linkRoot.GetChild(i).gameObject);

            int links = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spotFurn[i] < 0) continue;                                  // 바닥 은신처는 링크 불필요
                if (spots[i].y < 0.25f * MapScale) continue;                    // 걸어서 올라갈 수 있는 높이
                BuildLink(linkRoot, $"Link_{i:00}_{Furniture[spotFurn[i]].name}", Furniture[spotFurn[i]]);
                links++;
            }
            Log($"NavMeshLink {links}개 생성 (윗면 가장자리로 연결)");
        }

        // --- 인형: 바닥 은신처에서 시작 (발이 바닥에 닿도록) ---
        var dolls = Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        System.Array.Sort(dolls, (a, b) => string.CompareOrdinal(a.name, b.name));

        for (int i = 0; i < dolls.Length; i++)
        {
            Vector2 f = FloorSpots[i % FloorSpots.Length];
            PlaceOnFloor(dolls[i].transform, new Vector3(f.x * HalfW, 0f, f.y * HalfD));
            Log($"  {dolls[i].name}: 위치 {dolls[i].transform.position}");
            EditorUtility.SetDirty(dolls[i]);
        }

        // --- 웨이포인트 배열을 각 인형에 다시 연결 ---
        foreach (var doll in dolls)
        {
            doll.wayPoints = wps;
            EditorUtility.SetDirty(doll);
        }
        Log($"인형 {dolls.Length}마리에 웨이포인트 {wps.Length}개 연결");

        // --- 초기 얼룩 (바닥에 눕힌 스프라이트) ---
        var stains = GameObject.FindGameObjectsWithTag("Stain");
        System.Array.Sort(stains, (a, b) => string.CompareOrdinal(a.name, b.name));

        for (int i = 0; i < stains.Length; i++)
        {
            Vector2 f = StainLayout[i % StainLayout.Length];
            Vector3 p = new Vector3(f.x * HalfW, 0.02f, f.y * HalfD);
            if (i >= StainLayout.Length) p.x *= 0.5f;
            // RectTransform이라 world position은 직렬화 왕복에서 어긋난다. localPosition으로 고정
            stains[i].transform.SetParent(GameObject.Find("StainGroup")?.transform, false);
            stains[i].transform.localPosition = p;
            stains[i].transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // SpawnStain과 동일

            ExcludeFromNavMesh(stains[i]);   // 얼룩도 통행 장애물이 아니다

            Vector3 size = MeasureSize(stains[i].transform);
            Log($"  {stains[i].name}: 위치 {p}, 실제 크기 {size}");
            EditorUtility.SetDirty(stains[i]);
        }
        Log($"얼룩 {stains.Length}개 배치");
    }

    // ===== 크기 정규화 =====

    // 인형은 프롭과 크기가 같아야 위장이 성립하므로 프롭과 같은 배율을 따른다
    const float BaseDollHeight = 0.4f;
    const float BaseStainDiameter = 0.3f;
    static float DollHeight => BaseDollHeight * MapScale;
    static float StainDiameter => BaseStainDiameter * MapScale;
    static float DollAgentRadius => 0.2f * MapScale;
    static float DollAgentHeight => 0.5f * MapScale;

    static void ScaleLevelObjects()
    {
        Log("--- 크기 정규화 ---");

        var dolls = Object.FindObjectsByType<DollMove>(FindObjectsSortMode.None);
        foreach (var doll in dolls)
        {
            Vector3 before = MeasureSize(doll.transform);
            ScaleTo(doll.transform, DollHeight, true);
            Vector3 after = MeasureSize(doll.transform);

            // 발이 바닥에 닿도록 다시 정렬
            Vector3 p = doll.transform.position;
            PlaceOnFloor(doll.transform, new Vector3(p.x, 0f, p.z));

            // Transform 스케일이 바뀌었으므로 에이전트 값을 다시 명시
            var agent = doll.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.radius = DollAgentRadius;
                agent.height = DollAgentHeight;
                agent.speed = 1.2f * MapScale;

                // 피벗이 발밑과 어긋나 있다 — 발을 바닥에 맞춘 지금의 피벗 높이가 곧 baseOffset
                agent.baseOffset = doll.transform.position.y;
                EditorUtility.SetDirty(agent);

                // 얼룩 높이는 더 이상 여기서 굽지 않는다 — DollStainSpawner가 생성 시점에
                // NavMesh에서 바닥을 직접 찾으므로 피벗이 바뀌어도 알아서 맞는다
            }

            Log($"  {doll.name}: {before.y:F2}m → {after.y:F2}m (Scale {doll.transform.localScale.x:F3})");
            EditorUtility.SetDirty(doll);
        }

        var stains = GameObject.FindGameObjectsWithTag("Stain");
        foreach (var stain in stains)
        {
            // 얼룩은 RectTransform이라 스케일을 바꾸면 위치가 밀린다 — 이전 위치를 기억했다 되돌린다
            Vector3 p = stain.transform.localPosition;

            Vector3 before = MeasureSize(stain.transform);
            ScaleTo(stain.transform, StainDiameter, false);
            Vector3 after = MeasureSize(stain.transform);

            stain.transform.localPosition = new Vector3(p.x, 0.02f, p.z);

            Log($"  {stain.name}: 지름 {Mathf.Max(before.x, before.z):F2}m → {Mathf.Max(after.x, after.z):F2}m");
            EditorUtility.SetDirty(stain);
        }

        Log($"인형 {dolls.Length}마리 / 얼룩 {stains.Length}개 정규화 완료");
    }

    /// <summary>Renderer 실측 크기가 targetSize가 되도록 localScale을 조정한다.</summary>
    static void ScaleTo(Transform t, float targetSize, bool useHeight)
    {
        Vector3 size = MeasureSize(t);
        float current = useHeight ? size.y : Mathf.Max(size.x, size.z);
        if (current <= 0.0001f) { Warn($"{t.name}: 크기를 측정하지 못해 건너뜀"); return; }

        t.localScale *= targetSize / current;
    }

    static void ResetGroup(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) return;
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(go);
    }

    /// <summary>XZ 위치를 잡고, Renderer 바운드 기준으로 바닥(y=0)에 발을 맞춘다.</summary>
    static void PlaceOnFloor(Transform t, Vector3 pos, bool resetRotation = true)
    {
        t.position = pos;
        if (resetRotation) t.rotation = Quaternion.identity;

        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        t.position += Vector3.up * (pos.y - b.min.y);
    }

    static Vector3 MeasureSize(Transform t)
    {
        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return Vector3.zero;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size;
    }

    // ===== 진단 =====

    static void Report()
    {
        Log("--- 진단 ---");

        int outside = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            bool isLevelObject = t.GetComponent<DollMove>() != null
                              || t.name.StartsWith("WayPoint")
                              || t.CompareTag("Stain");
            if (!isLevelObject) continue;

            Vector3 p = t.position;
            if (Mathf.Abs(p.x) > HalfW || Mathf.Abs(p.z) > HalfD)
            {
                Warn($"방 밖: {t.name} {p}");
                outside++;
            }
        }
        Log($"방 밖 오브젝트: {outside}개");

        if (Object.FindFirstObjectByType<GameProgressManager>() == null) Warn("GameProgressManager 없음 — 승패 판정 불가");
        if (Object.FindFirstObjectByType<GameHUD>() == null) Warn("GameHUD 없음");
        if (Object.FindFirstObjectByType<NavMeshSurface>() == null) Warn("NavMeshSurface 없음 — 인형이 움직이지 못함");

        Log("NavMesh 재베이크 필요 (Agent Radius 0.2 / Height 0.5 / Step 0.15)");
    }

    // private [SerializeField] 필드는 SerializedObject로만 접근 가능
    static void SetFloat(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null) { Warn($"'{field}' 필드를 찾지 못했습니다 ({target.GetType().Name})"); return; }
        prop.floatValue = value;
        so.ApplyModifiedProperties();
    }

    static void Log(string msg) => Debug.Log($"[맵규격] {msg}");
    static void Warn(string msg) => Debug.LogWarning($"[맵규격] {msg}");
}
