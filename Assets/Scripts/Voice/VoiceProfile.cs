using System;
using UnityEngine;

// NPC 말투 (2026-10-23) — "누가 어떻게 말하나"를 담는 데이터. 에셋 하나 = NPC 하나.
// 음절 클립 표 + 음높이·빠르기·억양·무전 효과. 플레이 중에 값을 바꾸면 다음 줄부터 바로 반영된다.
// 대사 → 소리 계산은 GibberishPlanner, 재생은 GibberishVoice.
// 설계 기록: 프로젝트 문서 「코드 설계 - NPC 블라블라 목소리」 2.8, 3, 4.1
[CreateAssetMenu(menuName = "목소리/말투", fileName = "NewVoice")]
public class VoiceProfile : ScriptableObject
{
    [Serializable]
    public class VoiceSyllable
    {
        [Tooltip("보통 블립에 쓰는 짧은 클립 (short 폴더)")]
        public AudioClip clip;
        [Tooltip("문장 끝을 끌 때 쓰는 긴 클립 (full 폴더). 비우면 clip")]
        public AudioClip longClip;
        [Tooltip("자모 표시 (예: ba, nya, bong). 비우면 클립 이름의 '_' 뒤에서 읽음")]
        public string tag;
        [Tooltip("음량 보정. 설정 도구가 계산 (클립끼리 크기 맞추기)")]
        public float gain = 1f;
    }

    [Header("클립")]
    public VoiceSyllable[] syllables = Array.Empty<VoiceSyllable>();
    [Tooltip("말줄임표로 시작하는 줄 앞의 \"흠\" (full 웅얼 클립)")]
    public AudioClip[] sighClips = Array.Empty<AudioClip>();

    [Header("음높이 (AudioSource.pitch 기준, 원본 녹음 ≈ 118Hz = 1.0)")]
    [Range(0.5f, 2f)] public float basePitch = 1f;
    [Tooltip("블립마다 ± 흔들림 (비율)")]
    [Range(0f, 0.15f)] public float pitchJitter = 0.05f;
    [Tooltip("문장 안에서 앞→끝으로 내려가는 폭 (비율, 0.06 = 앞 +3% … 끝 -3%)")]
    [Range(0f, 0.2f)] public float declination = 0.04f;
    [Tooltip("? 로 끝나는 문장의 마지막 블립")]
    public float questionRise = 1.15f;
    [Tooltip(". 로 끝나는 문장의 마지막 블립")]
    public float endFall = 0.95f;
    [Tooltip("! 문장 전체 음높이")]
    public float exclaimPitch = 1.05f;
    [Tooltip("! 문장 전체 크기")]
    public float exclaimVolume = 1.15f;

    [Header("빠르기 (초)")]
    [Tooltip("블립 사이 기본 간격")]
    [Range(0.05f, 0.4f)] public float blipInterval = 0.12f;
    [Range(0f, 0.4f)] public float intervalJitter = 0.1f;
    [Tooltip("블립 하나에 글자 몇 개 (1 = 글자마다, 2 = 두 글자에 하나)")]
    [Range(1f, 3f)] public float syllablesPerBlip = 1.5f;
    [Tooltip("긴 클립으로 끈 뒤 더 쉬는 시간")]
    public float longClipExtra = 0.1f;
    public float wordGap = 0.05f;
    public float commaGap = 0.18f;
    public float sentenceGap = 0.3f;
    public float ellipsisGap = 0.4f;

    [Header("크기")]
    [Range(0f, 1.5f)] public float volume = 0.9f;
    [Range(0f, 0.3f)] public float volumeJitter = 0.08f;
    [Tooltip("단어 첫 글자 강세")]
    public float wordStress = 1.1f;

    [Header("버릇")]
    [Tooltip("같은 대사는 언제나 같은 소리 (흔들림 시드 = 대사 해시)")]
    public bool deterministic = true;
    [Tooltip("문장 끝(. … ~)을 긴 클립으로 끌기")]
    public bool drawlSentenceEnd = true;
    [Tooltip("'...'로 시작하는 줄 앞에 \"흠\"")]
    public bool sighOnLeadingEllipsis = false;
    [Range(0.5f, 1.5f)] public float sighPitch = 0.9f;
    [Range(0f, 1.5f)] public float sighVolume = 0.7f;

    [Header("무전 효과 (현장에 서 있는 NPC는 끔)")]
    public bool radio = false;
    public float highpassHz = 320f;
    public float lowpassHz = 3400f;
    [Range(0f, 0.9f)] public float distortion = 0.2f;

    /// <summary>클립 이름 'basic_ba' → 'ba'</summary>
    public static string TagFromName(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return "";
        int i = clipName.LastIndexOf('_');
        return (i >= 0 ? clipName.Substring(i + 1) : clipName).ToLowerInvariant();
    }

    public string TagOf(VoiceSyllable s)
    {
        if (s == null) return "";
        if (!string.IsNullOrEmpty(s.tag)) return s.tag.ToLowerInvariant();
        return s.clip != null ? TagFromName(s.clip.name) : "";
    }
}
