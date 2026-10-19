namespace Shooter.Common
{
    public interface IDamageable
    {
        void TakeDamage(DamageInfo damage);
        // TakeHeal은 플레이어에게서만 사용 될 예정이므로 추가하지 않았음.
    }

    public struct DamageInfo
    {
        public float Amount;
        // 필요한 필드 추가하여 사용
    }
}