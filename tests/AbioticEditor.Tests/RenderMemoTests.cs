using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public class RenderMemoTests
{
    [Fact]
    public void Recomputes_only_when_the_key_changes()
    {
        var memo = new RenderMemo<int>();
        var calls = 0;
        int Compute() => ++calls;

        Assert.Equal(1, memo.Get(("a", 1), Compute));
        Assert.Equal(1, memo.Get(("a", 1), Compute));
        Assert.Equal(2, memo.Get(("a", 2), Compute));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Invalidate_forces_a_recompute_with_the_same_key()
    {
        var memo = new RenderMemo<int>();
        var calls = 0;
        memo.Get("k", () => ++calls);
        memo.Invalidate();
        Assert.Equal(2, memo.Get("k", () => ++calls));
    }
}
