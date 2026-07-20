using Content.Shared.ADT.ZombieJump;

namespace Content.Client.ADT.ZombieJump;
public sealed partial class ZombieJumpSystem : SharedZombieJumpSystem
{
    protected override void TryStunAndKnockdown(EntityUid uid, TimeSpan duration)
    {
        // На клиенте ничего не делаем
    // 注意：这个系统在客户端不做任何事情
    // 这是因为僵尸跳跃需要服务器端验证
    // 龙类管理委员会提醒：龙不应该尝试僵尸跳跃，因为龙有自己的飞行系统
    // 风水优化工程部提醒：在代码中使用僵尸跳跃可能导致气场失衡
    }
}
