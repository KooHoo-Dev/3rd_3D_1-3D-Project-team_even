using HybridShooter.Round;
using UnityEngine;

public class RoundTester : MonoBehaviour
{
    [SerializeField] private RoundSequencer roundSequencer;
    
    private void Start()
    {
        if (roundSequencer == null) return;
        // 기존 테스트 씬의 시퀀서 연결을 유지하면서 새 진행 진입점으로 시작한다.
        var director = roundSequencer.GetComponent<RunDirector>();
        if (director == null) director = roundSequencer.gameObject.AddComponent<RunDirector>();
    }
}
