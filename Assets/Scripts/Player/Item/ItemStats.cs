using System;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>수류탄이 실제로 쓰는 능력치. 테이블(GrenadeDef) 값 또는 강화가 적용된 값</summary>
    [Serializable]
    public struct GrenadeStats
    {
        [Tooltip("폭발 중심 피해")]
        public int damage;
        [Tooltip("폭발 반경(m)")]
        public float radius;
        [Tooltip("던진 뒤 폭발까지(초)")]
        public float fuseTime;
        [Tooltip("던지는 속도(m/s)")]
        public float throwSpeed;

        public static GrenadeStats From(GrenadeDef def) => new GrenadeStats
        {
            damage = def.Damage,
            radius = def.Radius,
            fuseTime = def.FuseTime,
            throwSpeed = def.ThrowSpeed,
        };
    }

    /// <summary>
    /// 회복 팩 한 번 사용의 능력치.
    /// 사용하면 duration 동안 초당 healPerSecond 씩 회복한다. 체력이 가득 차 있는 동안은 쉬고, 다시 깎이면 이어서 회복한다.
    /// 이번 사용으로 회복한 양이 totalCap 에 닿거나 duration 이 끝나면 멈춘다.
    /// </summary>
    [Serializable]
    public struct HealStats
    {
        [Tooltip("초당 회복량")]
        public float healPerSecond;
        [Tooltip("효과 지속 시간(초)")]
        public float duration;
        [Tooltip("한 번 사용으로 회복할 수 있는 총량. 0 이하 = 최대 체력")]
        public int totalCap;
    }
}
