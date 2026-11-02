using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어 하나를 통째로 다루는 컴포넌트. 입력 → 이동(물리) → 카메라 리그 → 체력·아이템.
    ///
    /// 몸은 하나 (True First Person)
    ///  - 1인칭·3인칭 모두 같은 몸(Body)과 같은 총을 쓴다. 카메라만 2대(FP_Camera / TP_Camera).
    ///  - 1인칭에서는 머리 렌더러만 "그림자만" 모드로 바꿔 화면에서 숨기고 그림자는 남긴다.
    ///  - 카메라는 리그(Rig = 좌우, Pitch = 상하) 를 돌려서 움직인다. 카메라 자체 위치는 건드리지 않는다.
    ///
    /// 이동 요약
    ///  - 터레인 위: SphereCast 로 바닥·경사를 판정, 걷기는 경사면을 따라 속도를 투영한다. 지상에서는 중력을 꺼서 미끄러짐/들썩임 방지.
    ///  - 마찰 0 콜라이더 → 벽에 비비며 걸려도 붙지 않음.
    ///  - 감속/가속: 이름(key)이 있는 속도 배율 목록. 곱해서 적용한다. (피격 둔화, 스태미나 바닥, 상태이상 등)
    ///    + 자동: 몸이 이동 방향과 어긋나 있을 때(뒤로 걷기, 뒤돌아 출발) 느려짐.
    ///  - 외부 제어: 벽넘기처럼 애니메이션이 몸을 직접 옮겨야 할 때 BeginExternalControl ~ EndExternalControl 동안 물리를 멈춘다.
    ///  - 외부 속도: 보스 흡입 등이 붙인 소스(IPlayerVelocitySource)가 매 물리 프레임 계산한 수평 속도를 모터 속도 위에 더한다 (입력은 살아 있음).
    ///
    /// 파일 구성 (partial)
    ///  - PlayerController.Data          : 인스펙터 설정 + 내부 상태 필드
    ///  - PlayerController.UnityMethod   : Awake / Start / Update / FixedUpdate / LateUpdate 등 유니티 메시지
    ///  - PlayerController.Input         : 키보드·마우스 입력 → 이동·무기·아이템 요청
    ///  - PlayerController.InputSettings : 키 배치
    ///  - PlayerController.ViewMode      : 1·3인칭 시점 전환 연출, 전환 시작·완료 알림(CameraModeChangedEvent)
    ///  - PlayerController.BossView      : 보스 등장 시 3인칭 자동 전환, 보스전 카메라, 시점 고정
    ///  - PlayerController.CameraRig     : 카메라 리그 회전, 눈 위치, 3인칭 벽 충돌, 조준 확대, 1인칭 머리 숨김
    ///  - PlayerController.BodyVisual    : 보이는 몸 yaw 맞추기
    ///  - PlayerController.Movement      : 바닥 판정, 걷기·구르기·점프(물리), 속도 배율
    ///  - PlayerController.ExternalVelocity : 외부 속도 슬롯 (IPlayerExternalVelocity). 보스 흡입 등
    ///  - PlayerController.Health        : 체력, 회복, 피격, 사망
    ///  - PlayerController.Stamina       : 스태미나 (달리기·구르기 소모, 회복)
    ///  - PlayerController.Revive        : 사망 → 부활 연출(무적·입력 없음) → 부활 직후 보호, 최종 사망, 재시작
    ///  - PlayerController.Lives         : 목숨 (IPlayerLives). 남아 있으면 사망 후 자동 부활, 마지막이면 최종 사망
    ///  - PlayerController.Upgrade       : 강화(IUpgradeState) → 최대 체력 · 최대 스태미나 · 스태미나 회복 속도
    ///  - PlayerController.API           : 바깥에서 쓰는 이벤트·프로퍼티·메서드
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public partial class PlayerController : MonoBehaviour, IPlayerTarget, IHealable, IDamageable, IPlayerLives, IPlayerExternalVelocity
    {
    }
}
