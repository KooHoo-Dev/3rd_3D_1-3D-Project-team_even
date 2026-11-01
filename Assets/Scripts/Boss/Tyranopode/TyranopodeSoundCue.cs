namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// TyranopodeController.SoundCue 로 알리는 소리 순간. 패턴 단계(예고·발동)로 알 수 있는 소리는 여기 두지 않는다.
    /// 값을 추가할 때는 Audio.TyranopodeSoundEmitter 의 키 목록도 함께 늘린다
    /// </summary>
    public enum TyranopodeSoundCue
    {
        /// <summary>등장 포효 · 페이즈 2 전환 포효</summary>
        Roar,
        /// <summary>돌진하다 벽·경기장 경계에 박힘 (위치 = 부딪힌 앞쪽)</summary>
        WallHit,
        /// <summary>그로기 시작</summary>
        Groggy,
        /// <summary>침 한 발</summary>
        Spit,
        /// <summary>알 던지기 (한 번에 여러 개를 던져도 한 번)</summary>
        EggThrow,
        /// <summary>알 부화 (위치 = 알)</summary>
        EggHatch,
        /// <summary>에너지 흡수 시작 (등장 연출 흡수 · 보호막 흡수)</summary>
        Absorb,
        /// <summary>물기 타격 순간 (위치 = 입 앞)</summary>
        BiteImpact,
        /// <summary>촉수 휘두르기 한 번 (연타면 타격마다)</summary>
        Swipe,
        /// <summary>땅 내려찍기 진동</summary>
        Tremor,
        /// <summary>촉수 태풍 쓸기 시작 (쓸기마다)</summary>
        StormSweep,
        /// <summary>침 착탄 (위치 = 터진 곳)</summary>
        SpitImpact,
        /// <summary>알이 부화 전에 깨짐 (위치 = 알)</summary>
        EggBreak,
        /// <summary>포자가 떠 있는 동안 무적 상태에서 맞음 (튕김 표시와 같은 간격)</summary>
        ImmuneHit,
        /// <summary>보호막이 피해를 막음 (다 깨지는 발 제외)</summary>
        ShieldHit,
        /// <summary>보호막이 다 깨짐</summary>
        ShieldBreak,
        /// <summary>포자 맥박 (위치 = 포자)</summary>
        SporePulse,
        /// <summary>포자 파괴 성공 (위치 = 포자)</summary>
        SporeBreak,
        /// <summary>포자 시간 초과 폭발 (위치 = 포자)</summary>
        SporeFail,
    }
}
