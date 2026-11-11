using System;
using System.Collections.Generic;
using UnityEngine;

// 대사 텍스트 + 말투 → 블립 계획 (2026-10-23). MonoBehaviour가 아닌 순수 계산이라 언제 불러도 같은 결과.
//  1) 글자 나누기: 음절 / 띄어쓰기 / 쉼표 / 문장 끝(. ? !) / 말줄임(... …) / 물결(~)
//  2) 음절마다 소리 낼지: 블립당 글자 수(syllablesPerBlip) 누적. 문장 첫 글자·끝 글자는 항상 냄
//  3) 클립 고르기: 한글을 초성·중성·종성으로 나눠 자모가 가장 비슷한 클립. 동점이면 글자 해시로 (같은 글자 = 같은 클립)
//  4) 억양: 문장 안에서 내려감, 끝 글자 ? 올림 / . 내림 + 긴 클립, ! 높고 크게, 단어 첫 글자 강세
//  5) 줄 시간에 맞추기: 넘치면 블립 수 줄이기 → 말끝 끌기 빼기 → 간격 좁히기 → 넘치는 블립 버리기
// 설계 기록: 프로젝트 문서 「코드 설계 - NPC 블라블라 목소리」 2.2, 2.3, 2.5, 2.6, 4.2
public static class GibberishPlanner
{
    public struct Blip
    {
        public float time;      // 말 시작 기준 초
        public AudioClip clip;
        public float pitch;
        public float volume;
        public bool isSigh;
        public float Length => clip != null ? clip.length / Mathf.Max(0.05f, pitch) : 0f;
    }

    public class Plan
    {
        public readonly List<Blip> blips = new List<Blip>();
        public float Duration;
    }

    // 줄 시간에 맞추기
    const float FitRatio = 0.9f;
    const float MaxSyllablesPerBlip = 3f;
    const float MinTimeScale = 0.8f;

    // ───────────── 자모 특징 ─────────────
    struct Feat
    {
        public string onset; // b m n r d g j s, 없으면 ""
        public string vowel; // a e eo o u eu i, 없으면 ""
        public string coda;  // "" n l ng m p
    }

    enum Kind { Syllable, WordGap, Comma, SentenceEnd, Ellipsis, Drawl }

    struct Unit
    {
        public Kind kind;
        public char ch;
        public Feat feat;
        public char mark;          // 문장 끝 표시 . ? ! … (없으면 '.')
        public int posInSentence;
        public int sentenceLength;
        public bool wordStart;
        public bool beforeDrawl;   // 바로 뒤가 ~
    }

    public static Plan Build(string text, VoiceProfile p, float maxSeconds = 0f)
    {
        var units = Tokenize(text ?? "");
        int seed = p.deterministic ? (int)Fnv(text ?? "") : Environment.TickCount;
        var tags = ParseTags(p);

        float spb = Mathf.Max(1f, p.syllablesPerBlip);
        float scale = 1f;
        bool allowLong = true;
        Plan plan = null;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            plan = Layout(units, p, tags, spb, scale, seed, allowLong);
            if (maxSeconds <= 0f || plan.Duration <= maxSeconds * FitRatio) return plan;
            if (spb < MaxSyllablesPerBlip) { spb = Mathf.Min(MaxSyllablesPerBlip, spb * 1.25f); continue; } // ① 블립 수 줄이기
            if (allowLong) { allowLong = false; continue; }                                               // ② 말끝 끌기 빼기
            if (scale > MinTimeScale) { scale = Mathf.Max(MinTimeScale, scale - 0.1f); continue; }       // ③ 간격 좁히기
            break;
        }

        // ④ 그래도 넘치면 줄 시간을 넘는 블립 버리기
        plan.blips.RemoveAll(b => b.time + b.Length > maxSeconds);
        plan.Duration = 0f;
        foreach (var b in plan.blips) plan.Duration = Mathf.Max(plan.Duration, b.time + b.Length);
        return plan;
    }

    // ───────────── 1) 글자 나누기 ─────────────
    static List<Unit> Tokenize(string text)
    {
        var list = new List<Unit>();
        bool wordStart = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                list.Add(new Unit { kind = Kind.Syllable, ch = c, feat = Hangul(c), wordStart = wordStart });
                wordStart = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                list.Add(new Unit { kind = Kind.Syllable, ch = c, feat = Latin(c), wordStart = wordStart });
                wordStart = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (list.Count > 0 && list[list.Count - 1].kind != Kind.WordGap) list.Add(new Unit { kind = Kind.WordGap });
                wordStart = true;
            }
            else if (c == ',' || c == '、' || c == ';' || c == ':')
            {
                list.Add(new Unit { kind = Kind.Comma });
                wordStart = true;
            }
            else if (c == '…' || (c == '.' && i + 1 < text.Length && text[i + 1] == '.'))
            {
                while (i + 1 < text.Length && (text[i + 1] == '.' || text[i + 1] == '…')) i++;
                list.Add(new Unit { kind = Kind.Ellipsis, mark = '…' });
                wordStart = true;
            }
            else if (c == '.' || c == '?' || c == '!' || c == '。' || c == '？' || c == '！')
            {
                char m = c == '。' ? '.' : c == '？' ? '?' : c == '！' ? '!' : c;
                while (i + 1 < text.Length && (text[i + 1] == '?' || text[i + 1] == '!' || text[i + 1] == '.')) i++;
                list.Add(new Unit { kind = Kind.SentenceEnd, mark = m });
                wordStart = true;
            }
            else if (c == '~' || c == '～')
            {
                if (list.Count > 0 && list[list.Count - 1].kind == Kind.Syllable)
                {
                    var u = list[list.Count - 1]; u.beforeDrawl = true; list[list.Count - 1] = u;
                }
                list.Add(new Unit { kind = Kind.Drawl });
            }
            // 따옴표·괄호 등은 무시
        }

        // 문장 정보: 음절마다 문장 안 위치, 문장 길이, 끝 표시
        int start = 0;
        for (int i = 0; i <= list.Count; i++)
        {
            bool end = i == list.Count || list[i].kind == Kind.SentenceEnd || list[i].kind == Kind.Ellipsis;
            if (!end) continue;
            char mark = i < list.Count ? list[i].mark : '.';
            int n = 0;
            for (int k = start; k < i; k++) if (list[k].kind == Kind.Syllable) n++;
            int pos = 0;
            for (int k = start; k < i; k++)
            {
                if (list[k].kind != Kind.Syllable) continue;
                var u = list[k]; u.posInSentence = pos++; u.sentenceLength = n; u.mark = mark; list[k] = u;
            }
            start = i + 1;
        }
        return list;
    }

    // ───────────── 2~4) 배치 ─────────────
    static Plan Layout(List<Unit> units, VoiceProfile p, Feat[] tags, float spb, float scale, int seed, bool allowLong)
    {
        var rng = new System.Random(seed);
        var plan = new Plan();
        float t = 0f;
        float counter = spb - 1f;
        int prev = -1;
        bool spoke = false;

        foreach (var u in units)
        {
            switch (u.kind)
            {
                case Kind.WordGap:
                    if (spoke) t += p.wordGap * scale;
                    break;
                case Kind.Comma:
                    if (spoke) t += p.commaGap * scale;
                    break;
                case Kind.SentenceEnd:
                    if (spoke) t += p.sentenceGap * scale;
                    break;
                case Kind.Ellipsis:
                    if (!spoke && plan.blips.Count == 0 && p.sighOnLeadingEllipsis && TryPick(p.sighClips, seed, out var sigh))
                    {
                        var b = new Blip { time = t, clip = sigh, pitch = p.basePitch * p.sighPitch, volume = p.volume * p.sighVolume, isSigh = true };
                        plan.blips.Add(b);
                        t += b.Length * 0.85f;
                    }
                    t += p.ellipsisGap * scale;
                    break;
                case Kind.Syllable:
                {
                    if (u.posInSentence == 0) counter = spb - 1f;
                    counter += 1f;
                    bool last = u.posInSentence == u.sentenceLength - 1;
                    bool voiced = counter >= spb - 0.0001f || last;
                    if (!voiced) break;
                    counter = Mathf.Max(0f, counter - spb);

                    int idx = Choose(p, tags, u, prev);
                    if (idx < 0) break;
                    var syl = p.syllables[idx];

                    bool endDrawl = last && p.drawlSentenceEnd && (u.mark == '.' || u.mark == '…');
                    bool useLong = allowLong && (endDrawl || u.beforeDrawl) && syl.longClip != null;
                    var clip = useLong ? syl.longClip : syl.clip;

                    float pos = u.sentenceLength > 1 ? (float)u.posInSentence / (u.sentenceLength - 1) : 1f;
                    float pitch = p.basePitch * (1f + p.declination * (0.5f - pos)) * (1f + Rand(rng) * p.pitchJitter);
                    float vol = p.volume * Mathf.Max(0f, syl.gain) * (1f + Rand(rng) * p.volumeJitter);
                    if (u.wordStart) vol *= p.wordStress;
                    if (u.mark == '!') { pitch *= p.exclaimPitch; vol *= p.exclaimVolume; }
                    if (last)
                    {
                        if (u.mark == '?') pitch *= p.questionRise;
                        else if (u.mark == '.' || u.mark == '…') pitch *= p.endFall;
                    }

                    plan.blips.Add(new Blip { time = t, clip = clip, pitch = pitch, volume = vol });
                    spoke = true;
                    prev = idx;
                    t += p.blipInterval * scale * (1f + Rand(rng) * p.intervalJitter);
                    if (useLong) t += p.longClipExtra * scale;
                    break;
                }
            }
        }

        foreach (var b in plan.blips) plan.Duration = Mathf.Max(plan.Duration, b.time + b.Length);
        return plan;
    }

    static float Rand(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

    static bool TryPick(AudioClip[] clips, int seed, out AudioClip clip)
    {
        clip = null;
        if (clips == null || clips.Length == 0) return false;
        int n = 0;
        foreach (var c in clips) if (c != null) n++;
        if (n == 0) return false;
        int k = (int)((uint)seed % (uint)n);
        foreach (var c in clips)
        {
            if (c == null) continue;
            if (k-- == 0) { clip = c; return true; }
        }
        return false;
    }

    // ───────────── 3) 클립 고르기 ─────────────
    static readonly List<int> best = new List<int>();

    static int Choose(VoiceProfile p, Feat[] tags, Unit u, int prev)
    {
        best.Clear();
        int top = int.MinValue;
        for (int i = 0; i < p.syllables.Length; i++)
        {
            if (p.syllables[i] == null || p.syllables[i].clip == null) continue;
            int s = Score(u.feat, tags[i]);
            if (s > top) { top = s; best.Clear(); }
            if (s == top) best.Add(i);
        }
        if (best.Count == 0) return -1;
        int k = (int)(Fnv(u.ch) % (uint)best.Count);
        if (best[k] == prev && best.Count > 1) k = (k + 1) % best.Count;
        return best[k];
    }

    // 점수 (문서 10.2): 모음 같음 3 / 가까움 1, 받침 같음 2 / 다른 받침 1 / 받침 없는 글자에 받침 클립 −1,
    //                   초성 같음 2 (첫소리 없음끼리 포함) / 같은 계열 1
    static int Score(Feat want, Feat have)
    {
        int s = 0;
        if (want.vowel.Length > 0)
        {
            if (want.vowel == have.vowel) s += 3;
            else if (NearVowel(want.vowel, have.vowel)) s += 1;
        }
        if (want.coda.Length > 0) s += want.coda == have.coda ? 2 : have.coda.Length > 0 ? 1 : 0;
        else if (have.coda.Length > 0) s -= 1;
        if (want.onset == have.onset) s += 2;
        else if (want.onset.Length > 0 && Class(want.onset) != 0 && Class(want.onset) == Class(have.onset)) s += 1;
        return s;
    }

    // 가까운 모음: 클립이 적은 말투에서 비슷한 소리로 대신하기
    static bool NearVowel(string a, string b) =>
        Pair(a, b, "e", "i") || Pair(a, b, "eo", "o") || Pair(a, b, "eu", "u") || Pair(a, b, "eo", "a");

    static bool Pair(string a, string b, string x, string y) => (a == x && b == y) || (a == y && b == x);

    // 1 = 입술 (b m), 2 = 혀끝 (d n r), 3 = 혀끝 마찰 (j s), 4 = 여린입천장 (g)
    static int Class(string onset)
    {
        switch (onset)
        {
            case "b": case "m": return 1;
            case "d": case "n": case "r": return 2;
            case "j": case "s": return 3;
            case "g": return 4;
            default: return 0;
        }
    }

    static Feat[] ParseTags(VoiceProfile p)
    {
        var arr = new Feat[p.syllables.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = ParseTag(p.TagOf(p.syllables[i]));
        return arr;
    }

    // "ba" "nya" "geo" "eun" "bong" "al" → 초성·모음·받침
    static Feat ParseTag(string tag)
    {
        var f = new Feat { onset = "", vowel = "", coda = "" };
        if (string.IsNullOrEmpty(tag)) return f;
        int i = 0;
        if (!IsVowel(tag[0]) && tag[0] != 'y' && tag[0] != 'w') { f.onset = tag[0].ToString(); i = 1; }
        while (i < tag.Length && (tag[i] == 'y' || tag[i] == 'w')) i++; // nya의 y 같은 반모음
        if (i + 1 < tag.Length && tag[i] == 'e' && (tag[i + 1] == 'o' || tag[i + 1] == 'u'))
        {
            f.vowel = tag.Substring(i, 2); i += 2;           // eo, eu
        }
        else if (i < tag.Length && IsVowel(tag[i]))
        {
            f.vowel = tag[i].ToString(); i++;
        }
        f.coda = i < tag.Length ? tag.Substring(i) : "";
        return f;
    }

    static bool IsVowel(char c) => c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';

    // ───────────── 한글 분해 ─────────────
    //                                   ㄱ   ㄲ   ㄴ   ㄷ   ㄸ   ㄹ   ㅁ   ㅂ   ㅃ   ㅅ   ㅆ   ㅇ  ㅈ   ㅉ   ㅊ   ㅋ   ㅌ   ㅍ   ㅎ
    static readonly string[] Initial = { "g", "g", "n", "d", "d", "r", "m", "b", "b", "s", "s", "", "j", "j", "j", "g", "d", "b", "" };
    //                                  ㅏ   ㅐ   ㅑ   ㅒ    ㅓ   ㅔ    ㅕ   ㅖ   ㅗ   ㅘ   ㅙ   ㅚ   ㅛ   ㅜ    ㅝ   ㅞ   ㅟ   ㅠ    ㅡ   ㅢ   ㅣ
    static readonly string[] Medial = { "a", "e", "a", "e", "eo", "e", "eo", "e", "o", "a", "e", "e", "o", "u", "eo", "e", "i", "u", "eu", "i", "i" };
    //                                없음 ㄱ   ㄲ   ㄳ   ㄴ   ㄵ   ㄶ   ㄷ   ㄹ   ㄺ   ㄻ   ㄼ   ㄽ   ㄾ   ㄿ   ㅀ   ㅁ   ㅂ   ㅄ   ㅅ   ㅆ   ㅇ    ㅈ   ㅊ   ㅋ   ㅌ   ㅍ   ㅎ
    static readonly string[] Final = { "", "p", "p", "p", "n", "n", "n", "p", "l", "p", "m", "l", "l", "l", "p", "l", "m", "p", "p", "p", "p", "ng", "p", "p", "p", "p", "p", "" };

    static Feat Hangul(char c)
    {
        int code = c - 0xAC00;
        return new Feat { onset = Initial[code / 588], vowel = Medial[(code % 588) / 28], coda = Final[code % 28] };
    }

    static Feat Latin(char c)
    {
        char l = char.ToLowerInvariant(c);
        var f = new Feat { onset = "", vowel = "", coda = "" };
        if (IsVowel(l)) f.vowel = l.ToString();
        else if (l == 'b' || l == 'p' || l == 'f' || l == 'v') f.onset = "b";
        else if (l == 'm') f.onset = "m";
        else if (l == 'n') f.onset = "n";
        else if (l == 'r' || l == 'l') f.onset = "r";
        else if (l == 'd' || l == 't') f.onset = "d";
        else if (l == 's' || l == 'z') f.onset = "s";
        else if (l == 'j' || l == 'c') f.onset = "j";
        else if (l == 'g' || l == 'k' || l == 'q') f.onset = "g";
        return f;
    }

    // ───────────── 해시 (FNV-1a 32비트, 플랫폼·실행마다 같은 값) ─────────────
    static uint Fnv(char c)
    {
        uint h = 2166136261u;
        h ^= (uint)(c & 0xFF); h *= 16777619u;
        h ^= (uint)(c >> 8); h *= 16777619u;
        return h;
    }

    static uint Fnv(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s)
        {
            h ^= (uint)(c & 0xFF); h *= 16777619u;
            h ^= (uint)(c >> 8); h *= 16777619u;
        }
        return h;
    }
}
