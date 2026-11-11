using System.Collections;
using UnityEngine;

// 화면을 덮었다 걷어내는 공용 페이드 — 덮는 색은 Image가 정하므로 검은색·흰색을 같은 코드로 쓴다
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    private CanvasGroup group;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();

        // 평소에는 투명하고 클릭도 통과시킨다
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    // 화면을 서서히 검게 덮는다 (알파 0 → 1)
    public IEnumerator FadeOut(float duration)
    {
        group.blocksRaycasts = true;
        yield return Fade(0f, 1f, duration);
    }

    // 덮인 화면을 서서히 걷어낸다 (알파 1 → 0)
    public IEnumerator FadeIn(float duration)
    {
        yield return Fade(1f, 0f, duration);
        group.blocksRaycasts = false;
    }

    // 지정 농도까지만 덮는 딤 연출 — 위에 있는 버튼이 계속 눌려야 하므로 blocksRaycasts는 건드리지 않는다
    public IEnumerator FadeTo(float targetAlpha, float duration)
    {
        yield return Fade(group.alpha, Mathf.Clamp01(targetAlpha), duration);
    }

    // 호출자가 코루틴을 돌릴 수 없을 때(컴포넌트 비활성 등) 페이더가 스스로 재생
    public void PlayFadeTo(float targetAlpha, float duration)
    {
        StartCoroutine(FadeTo(targetAlpha, duration));
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;   // timeScale = 0(패배·일시정지)에서도 진행되어야 한다
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        group.alpha = to;
    }
}
