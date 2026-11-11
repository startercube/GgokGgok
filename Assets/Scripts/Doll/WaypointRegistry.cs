using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 모든 Doll이 공유하는 웨이포인트 점유(예약) 목록
public static class WaypointRegistry
{
    private static readonly HashSet<Transform> occupied = new HashSet<Transform>();

    public static int Count => occupied.Count;

    public static void Occupy(Transform wayPoint) => occupied.Add(wayPoint);
    public static void Release(Transform wayPoint) => occupied.Remove(wayPoint);
    public static bool IsOccupied(Transform wayPoint) => occupied.Contains(wayPoint);

    // static은 씬 재로드로 비워지지 않으므로 게임 시작·씬 로드마다 직접 비운다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedState()
    {
        occupied.Clear();
        SceneManager.sceneLoaded -= ClearOnSceneLoad;
        SceneManager.sceneLoaded += ClearOnSceneLoad;
    }

    private static void ClearOnSceneLoad(Scene scene, LoadSceneMode mode) => occupied.Clear();
}
