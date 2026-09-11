namespace OnlineActionRpg.Client.Combat
{
    // 受击方接口：只表达“能不能打”和“被打之后自己怎么处理”。
    // HitBox 只依赖这个接口，不关心对方是木桩、玩家还是后续的 Boss。
    public interface IDamageable
    {
        bool IsAlive { get; }
        void ApplyDamage(in HitInfo hit);
    }
}