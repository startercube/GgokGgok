using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 한 주임 블라블라 목소리 설정 도구 (2026-10-23).
/// Tools > 목소리 > 한 주임 목소리 설정 (CLI: -executeMethod VoiceSetupTool.ApplyBatch)
///  1) Assets/Data/Voice/HanJuim_Voice.asset — 없으면 만들고 한 주임 값으로. 있으면 숫자는 그대로 두고 클립 목록만 다시 채움 (귀로 맞춘 값 보존)
///  2) 클립마다 음량 보정값 계산 (가장 큰 60ms 구간 RMS를 전체 중앙값에 맞춤, 0.6~2.5배)
///  3) 무전 박스 프리팹(RadioBox)에 GibberishVoice를 붙이고 말투·SFX 믹서 그룹 연결
/// 클립 위치: Assets/bgm/Voice/{short,full}/<세트>/<세트>_<자모>.wav (예: short/basic2/basic2_meo.wav). full은 같은 이름으로 짝을 찾음
/// 세트 폴더를 통째로 읽으므로 새 녹음은 폴더에 넣고 다시 실행하면 됨 (자모는 파일 이름 '_' 뒤에서 읽음)
/// 설계 기록: 프로젝트 문서 「코드 설계 - NPC 블라블라 목소리」 3, 4.5, 10
/// </summary>
public static class VoiceSetupTool
{
    public const string ClipRoot = "Assets/bgm/Voice";
    public const string HanProfilePath = "Assets/Data/Voice/HanJuim_Voice.asset";
    const string MixerPath = "Assets/BGM.mixer";
    const string MixerGroup = "SFX";

    // 한 주임이 쓰는 세트 폴더 (전부 읽음). 사용자 결정 2026-10-23: 예전 세트가 빠른 말투에 더 어울림 (문서 10.6)
    // 새 세트(vowel·gj·basic2·basic3·mumble2)는 폴더에 남아 있음 → 다른 NPC나 실험용으로 여기에 이름만 추가
    static readonly string[] HanSets = { "basic", "mumble" };
    // short mumble 중 mim·mung·num·nyang은 받침 '음'만 남아 작고 흐려서 뺌 → 한숨용 full에서만 씀
    static readonly string[] HanExclude = { "mumble_mim", "mumble_mung", "mumble_num", "mumble_nyang" };
    static readonly string[] HanSigh = { "bum", "mung" };

    [MenuItem("Tools/목소리/한 주임 목소리 설정")]
    static void Menu() => Apply(false);

    [MenuItem("Tools/목소리/한 주임 말투 처음 값으로")]
    static void ResetMenu()
    {
        if (!EditorUtility.DisplayDialog("한 주임 말투", "음높이·빠르기 등 숫자를 처음 값으로 되돌립니다. 귀로 맞춘 값은 사라집니다.", "되돌리기", "취소"))
            return;
        Apply(true);
    }

    public static void ApplyBatch() => Apply(false);

    static void Apply(bool resetValues)
    {
        EnsureImportSettings();
        var profile = AssetDatabase.LoadAssetAtPath<VoiceProfile>(HanProfilePath);
        if (profile == null)
        {
            EnsureFolder("Assets/Data/Voice");
            profile = ScriptableObject.CreateInstance<VoiceProfile>();
            SetHanDefaults(profile);
            AssetDatabase.CreateAsset(profile, HanProfilePath);
            Debug.Log($"[목소리] 한 주임 말투 만듦: {HanProfilePath}");
        }
        else if (resetValues)
        {
            SetHanDefaults(profile);
            Debug.Log("[목소리] 한 주임 말투 숫자를 처음 값으로");
        }

        FillHanClips(profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        AttachToRadioPrefab(profile);
        Selection.activeObject = profile;
    }

    /// <summary>한 주임 말투 처음 값 (제안, 문서 3장)</summary>
    static void SetHanDefaults(VoiceProfile p)
    {
        p.basePitch = 0.84f;        // ≈ 99Hz (원본 118Hz에서 −3반음). 20년 차 덩치 큰 아저씨
        p.pitchJitter = 0.03f;      // 단조롭게 (README 기본 ±5~7%보다 작게)
        p.declination = 0.06f;
        p.questionRise = 1.15f;
        p.endFall = 0.92f;          // 말끝 흐림
        p.exclaimPitch = 1.05f;
        p.exclaimVolume = 1.15f;

        p.blipInterval = 0.08f;     // 빠르게, 글자마다 (사용자 결정 old B — 문서 10.6)
        p.intervalJitter = 0.08f;
        p.syllablesPerBlip = 1.0f;
        p.longClipExtra = 0.05f;
        p.wordGap = 0.03f;
        p.commaGap = 0.12f;
        p.sentenceGap = 0.22f;
        p.ellipsisGap = 0.25f;

        p.volume = 0.8f;            // 블립이 촘촘히 겹쳐서 0.9면 넘칠 수 있음
        p.volumeJitter = 0.08f;
        p.wordStress = 1.1f;

        p.deterministic = true;
        p.drawlSentenceEnd = false; // 빠른 말투라 말끝을 끌지 않음
        p.sighOnLeadingEllipsis = true; // "...시작해." 앞에 "흠"
        p.sighPitch = 0.9f;
        p.sighVolume = 0.7f;

        p.radio = true;
        p.highpassHz = 320f;
        p.lowpassHz = 3400f;
        p.distortion = 0.2f;
    }

    static void FillHanClips(VoiceProfile p)
    {
        var list = new List<VoiceProfile.VoiceSyllable>();
        int missing = 0;
        foreach (var set in HanSets) AddSet(list, set, ref missing);
        p.syllables = list.ToArray();
        if (p.syllables.Length == 0) Debug.LogWarning($"[목소리] {ClipRoot}/short 아래에 클립이 없음");

        p.sighClips = HanSigh.Select(t => Load($"full/mumble/mumble_{t}")).Where(c => c != null).ToArray();
        missing += HanSigh.Length - p.sighClips.Length;

        ComputeGains(p);
        if (missing > 0) Debug.LogWarning($"[목소리] 클립 {missing}개가 {ClipRoot} 아래에 없음 (NPC_Voice 폴더 구조 그대로 복사했는지 확인)");
        Debug.Log($"[목소리] 한 주임 클립: 음절 {p.syllables.Length}개, 한숨 {p.sighClips.Length}개, 보정값 {string.Join(" ", p.syllables.Select(s => $"{p.TagOf(s)}×{s.gain:0.00}"))}");
    }

    /// <summary>short/<세트> 폴더의 클립을 이름 순으로 전부 넣고, full/<세트>에서 같은 이름을 짝으로</summary>
    static void AddSet(List<VoiceProfile.VoiceSyllable> list, string set, ref int missing)
    {
        string folder = $"{ClipRoot}/short/{set}";
        if (!AssetDatabase.IsValidFolder(folder)) { Debug.LogWarning($"[목소리] 세트 폴더 없음: {folder}"); return; }
        var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.Path.GetDirectoryName(path).Replace('\\', '/') == folder)
            .OrderBy(path => path, System.StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (HanExclude.Contains(name)) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var full = Load($"full/{set}/{name}");
            if (full == null) missing++;
            list.Add(new VoiceProfile.VoiceSyllable { clip = clip, longClip = full, tag = VoiceProfile.TagFromName(name), gain = 1f });
        }
    }

    /// <summary>클립이 가져오기 설정 도구(VoiceClipImporter)보다 먼저 들어와 압축된 채로 있으면 다시 가져오기</summary>
    static void EnsureImportSettings()
    {
        int fixedCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { ClipRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is AudioImporter imp &&
                (imp.defaultSampleSettings.compressionFormat != AudioCompressionFormat.PCM ||
                 imp.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                fixedCount++;
            }
        }
        if (fixedCount > 0) Debug.Log($"[목소리] 클립 {fixedCount}개 가져오기 설정을 PCM·Decompress On Load로 다시 가져옴");
    }

    static AudioClip Load(string relNoExt) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{ClipRoot}/{relNoExt}.wav");

    /// <summary>클립끼리 들리는 크기 맞추기: 가장 큰 60ms 구간의 RMS를 전체 중앙값에 맞춤</summary>
    static void ComputeGains(VoiceProfile p)
    {
        var peaks = p.syllables.Select(s => PeakWindowRms(s.clip)).ToArray();
        var valid = peaks.Where(v => v > 0f).OrderBy(v => v).ToArray();
        if (valid.Length == 0) return;
        float median = valid[valid.Length / 2];
        for (int i = 0; i < p.syllables.Length; i++)
            p.syllables[i].gain = peaks[i] > 0f ? Mathf.Clamp(median / peaks[i], 0.6f, 2.5f) : 1f;
    }

    static float PeakWindowRms(AudioClip clip)
    {
        if (clip == null) return 0f;
        clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return 0f;
        int win = Mathf.Max(1, (int)(clip.frequency * 0.06f)) * clip.channels;
        int hop = Mathf.Max(1, win / 4);
        double best = 0;
        for (int start = 0; start + win <= data.Length; start += hop)
        {
            double sum = 0;
            for (int i = start; i < start + win; i++) sum += data[i] * data[i];
            best = System.Math.Max(best, sum / win);
        }
        return (float)System.Math.Sqrt(best);
    }

    // ───────────── 무전 박스에 붙이기 ─────────────
    static void AttachToRadioPrefab(VoiceProfile profile)
    {
        var path = MapIntroFactory.RadioPath;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[목소리] 무전 박스 프리팹이 아직 없음 ({path}). Tools > 인트로 > 인형방 인트로 배치 뒤 다시 실행하거나, 프리팹을 만들 때 자동으로 붙음");
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            AttachVoice(root, root.GetComponent<RadioBox>(), profile);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[목소리] 무전 박스에 한 주임 목소리 연결: {path}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>무전 박스 오브젝트에 GibberishVoice를 붙이고 연결 (MapIntroFactory가 프리팹을 새로 만들 때도 부름)</summary>
    public static void AttachVoice(GameObject root, RadioBox box, VoiceProfile profile = null)
    {
        if (root == null) return;
        if (profile == null) profile = AssetDatabase.LoadAssetAtPath<VoiceProfile>(HanProfilePath);
        if (profile == null) Debug.Log("[목소리] 한 주임 말투 에셋이 아직 없음 → 소리 없이 붙임. Tools > 목소리 > 한 주임 목소리 설정을 실행하세요");

        var voice = root.GetComponent<GibberishVoice>();
        if (voice == null) voice = root.AddComponent<GibberishVoice>();
        var so = new SerializedObject(voice);
        so.FindProperty("profile").objectReferenceValue = profile;
        so.FindProperty("output").objectReferenceValue = FindMixerGroup();
        so.FindProperty("spatialBlend").floatValue = 0f;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (box != null)
        {
            var bso = new SerializedObject(box);
            bso.FindProperty("voice").objectReferenceValue = voice;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static AudioMixerGroup FindMixerGroup()
    {
        var group = AssetDatabase.LoadAllAssetsAtPath(MixerPath).OfType<AudioMixerGroup>().FirstOrDefault(g => g.name == MixerGroup);
        if (group == null) Debug.LogWarning($"[목소리] {MixerPath}에 '{MixerGroup}' 그룹이 없음 → 믹서 없이 재생 (설정 볼륨 안 먹음)");
        return group;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}

/// <summary>
/// Assets/bgm/Voice 아래 wav 가져오기 설정: 압축 없이(PCM) 로드할 때 풀어 둠, 모노, 미리 로드.
/// 0.17초 조각이라 압축(Vorbis)하면 앞뒤 여분 때문에 박자가 밀리고 이어 붙일 때 틱 소리가 날 수 있음. 40개 전부 약 1.2MB.
/// </summary>
public class VoiceClipImporter : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(VoiceSetupTool.ClipRoot + "/")) return;
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        var s = importer.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.PCM;
        s.preloadAudioData = true;
        importer.defaultSampleSettings = s;
    }
}
