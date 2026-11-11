using UnityEngine;

// 리플레이 ID = 이름 오름차순 인덱스. 녹화·재생이 이 함수를 함께 써야 ID가 일치한다
public static class ReplayIdentity
{
    public static void SortById<T>(T[] objects) where T : UnityEngine.Object
    {
        System.Array.Sort(objects, (a, b) => string.Compare(a.name, b.name));
    }
}