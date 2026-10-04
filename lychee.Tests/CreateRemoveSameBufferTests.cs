namespace lychee.Tests;

// 验证 plan_kimi_k3.md Open Question 5：同 buffer 内 CreateEntity + RemoveEntity 时，
// Commit 会对从未提交过的 ID 执行 CommitRemoveEntity，
// 其中 entities[id] 的访问在 id >= entities.Count 时会越界。
public class CreateRemoveSameBufferTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public CreateRemoveSameBufferTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

    [Fact]
    public void CreateThenRemove_SameBuffer_CommitDoesNotThrow()
    {
        var entity = commands.CreateEntity();

        commands.RemoveEntity(entity.Ref);
        commands.Commit();
    }

    [Fact]
    public void CreateTwoRemoveSecond_SameBuffer_CommitDoesNotThrow()
    {
        commands.CreateEntity();
        var entity = commands.CreateEntity();

        commands.RemoveEntity(entity.Ref);
        commands.Commit();

        // 存活的实体应该正常可用
        var alive = commands.CreateEntity();
        commands.Commit();
        Assert.True(commands.CheckEntityValid(alive.Ref));
    }

    [Fact]
    public void RecycledId_CreateThenRemove_SameBuffer_CommitDoesNotThrow()
    {
        // 对照组：先制造一个已提交的实体并删除，让 ID 进入回收队列
        var entity = commands.CreateEntity();
        commands.Commit();

        commands.RemoveEntity(entity.Ref);
        commands.Commit();

        // 回收 ID 的实体在同 buffer 内创建又删除。
        // 该 ID 在 entities 里已有 entry，预测此路径不越界
        var recycled = commands.CreateEntity();
        Assert.Equal(entity.Ref.ID, recycled.Ref.ID);

        commands.RemoveEntity(recycled.Ref);
        commands.Commit();
    }
}
