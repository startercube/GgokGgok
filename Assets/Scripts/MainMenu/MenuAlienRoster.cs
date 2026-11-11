using UnityEngine;

// 메인화면 격리실에 들어오는 빈대 목록 (2026-10-22)
// 맵 하나를 처음 클리어할 때마다 그 맵 줄의 빈대가 한 마리 들어온다. 줄 순서 = 들어오는 순서
// 맵이 늘면 여기에 줄만 추가하면 된다 (씬 이름은 빌드 설정의 씬 이름과 같아야 함)
[CreateAssetMenu(menuName = "외계인 숨바꼭질/메인화면 빈대 목록", fileName = "MenuAlienRoster")]
public class MenuAlienRoster : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("이 씬을 처음 클리어하면 빈대 한 마리 추가 (빌드 설정의 씬 이름)")]
        public string sceneName;
        [Tooltip("사람이 읽는 이름 (로그용)")]
        public string label;
        [Tooltip("이 현장에서 잡혀 온 표시. 빈대 머리 위(SouvenirAnchor)에 붙는다. 비우면 표시 없음")]
        public GameObject souvenirPrefab;
    }

    [Tooltip("격리실 빈대 프리팹 (루트에 MenuAlienWander, Visual/SouvenirAnchor가 있어야 함)")]
    public GameObject alienPrefab;
    [Tooltip("처음부터 있는 1마리 = 튜토리얼 격리실 원래 개체. 이 뒤로 클리어한 맵마다 한 마리")]
    public Entry[] entries = new Entry[0];
}
