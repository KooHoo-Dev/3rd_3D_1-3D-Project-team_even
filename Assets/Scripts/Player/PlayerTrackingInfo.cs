using UnityEngine;

/// <summary>
/// 군집(몬스터 무리)이 매 프레임 읽어 가는 플레이어 스냅샷.
/// PlayerController.GetTrackingInfo() 가 같은 인스턴스를 갱신해서 돌려준다. (GC 없음)
/// </summary>
public class PlayerTrackingInfo
{
   public Vector3 Position;
   /// <summary>현재 이동 속도(m/s). 군집이 이동 방향을 예측해 앞질러 가는 용도</summary>
   public Vector3 Velocity;
   /// <summary>몸체가 바라보는 방향 (수평)</summary>
   public Vector3 Forward;
}
