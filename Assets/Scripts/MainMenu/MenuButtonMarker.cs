using UnityEngine;
using UnityEngine.EventSystems;

// 메인화면 메뉴 버튼: 마우스가 올라가거나 키보드·패드로 선택되면 ▶ 표시 (2026-10-22, CCTV 녹화기 메뉴 느낌)
public class MenuButtonMarker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private GameObject marker;
    [Tooltip("켜 두면 항상 ▶ 표시 (첫 버튼 '출동' 강조용)")]
    [SerializeField] private bool alwaysOn;

    bool hover, selected;

    void OnEnable() { hover = selected = false; Refresh(); }
    public void OnPointerEnter(PointerEventData e) { hover = true; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hover = false; Refresh(); }
    public void OnSelect(BaseEventData e) { selected = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { selected = false; Refresh(); }

    void Refresh()
    {
        if (marker != null) marker.SetActive(alwaysOn || hover || selected);
    }
}
