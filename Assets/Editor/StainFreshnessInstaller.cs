using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 씬 파일(.unity)을 손으로 고치지 않고 버튼 한 번으로 매니저를 넣기 위한 에디터 메뉴
public static class StainFreshnessInstaller
{
    const string DefaultSettingsPath = "Assets/Settings/StainFreshness_Default.asset";

    [MenuItem("Tools/얼룩 신선도/현재 씬에 매니저 설치")]
    static void InstallToActiveScene()
    {
        // 이미 있으면 새로 만들지 않고 그걸 선택해 준다 — 씬에 2개가 되는 실수 방지
        StainFreshnessManager existing = Object.FindFirstObjectByType<StainFreshnessManager>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log($"[얼룩 신선도] 이 씬에는 이미 매니저가 있습니다: {existing.name}");
            return;
        }

        GameObject go = new GameObject("StainFreshnessManager");
        Undo.RegisterCreatedObjectUndo(go, "얼룩 신선도 매니저 설치");
        StainFreshnessManager manager = go.AddComponent<StainFreshnessManager>();

        // private [SerializeField]는 코드에서 바로 못 넣으므로 SerializedObject로 채운다 (인스펙터에 끌어다 놓는 것과 같음)
        StainFreshnessSettings settings = AssetDatabase.LoadAssetAtPath<StainFreshnessSettings>(DefaultSettingsPath);
        if (settings != null)
        {
            SerializedObject so = new SerializedObject(manager);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning($"[얼룩 신선도] 기본 설정 에셋({DefaultSettingsPath})을 찾지 못했습니다. 인스펙터에서 직접 넣어 주세요.");
        }

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeObject = go;
        Debug.Log("[얼룩 신선도] 매니저를 설치했습니다. 씬을 저장하세요.");
    }
}
