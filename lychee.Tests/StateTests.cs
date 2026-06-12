namespace lychee.Tests;

public class StateTests
{
    private enum TestState
    {
        Menu,
        Playing,
        Paused,
        GameOver
    }

#region Constructor

    [Fact]
    public void Constructor_SetsInitialCurrent()
    {
        var state = new State<TestState>(TestState.Menu);

        Assert.Equal(TestState.Menu, state.Current);
    }

    [Fact]
    public void Constructor_SetsInitialPrevious()
    {
        var state = new State<TestState>(TestState.Menu);

        Assert.Equal(TestState.Menu, state.Previous);
    }

    [Fact]
    public void Constructor_SetsChangedToTrue()
    {
        var state = new State<TestState>(TestState.Menu);

        Assert.True(state.Changed);
    }

#endregion

#region Set

    [Fact]
    public void Set_DifferentState_UpdatesCurrent()
    {
        var state = new State<TestState>(TestState.Menu);

        state.Set(TestState.Playing);

        Assert.Equal(TestState.Playing, state.Current);
    }

    [Fact]
    public void Set_DifferentState_UpdatesPrevious()
    {
        var state = new State<TestState>(TestState.Menu);

        state.Set(TestState.Playing);

        Assert.Equal(TestState.Menu, state.Previous);
    }

    [Fact]
    public void Set_DifferentState_SetsChangedToTrue()
    {
        var state = new State<TestState>(TestState.Menu);
        state.ClearChanged();

        state.Set(TestState.Playing);

        Assert.True(state.Changed);
    }

    [Fact]
    public void Set_SameState_DoesNotChange()
    {
        var state = new State<TestState>(TestState.Menu);
        state.ClearChanged();

        state.Set(TestState.Menu);

        Assert.Equal(TestState.Menu, state.Current);
        Assert.Equal(TestState.Menu, state.Previous);
        Assert.False(state.Changed);
    }

    [Fact]
    public void Set_MultipleTransitions_TracksAllPrevious()
    {
        var state = new State<TestState>(TestState.Menu);

        state.Set(TestState.Playing);
        Assert.Equal(TestState.Menu, state.Previous);

        state.Set(TestState.Paused);
        Assert.Equal(TestState.Playing, state.Previous);

        state.Set(TestState.GameOver);
        Assert.Equal(TestState.Paused, state.Previous);
    }

#endregion

#region ClearChanged

    [Fact]
    public void ClearChanged_ResetsChangedFlag()
    {
        var state = new State<TestState>(TestState.Menu);

        Assert.True(state.Changed);

        state.ClearChanged();

        Assert.False(state.Changed);
    }

    [Fact]
    public void ClearChanged_AfterSet_ResetsChangedFlag()
    {
        var state = new State<TestState>(TestState.Menu);
        state.Set(TestState.Playing);

        state.ClearChanged();

        Assert.False(state.Changed);
    }

#endregion

#region State Transitions

    [Fact]
    public void FullTransitionSequence_MaintainsCorrectState()
    {
        var state = new State<TestState>(TestState.Menu);

        Assert.Equal(TestState.Menu, state.Current);
        Assert.True(state.Changed);

        state.ClearChanged();
        state.Set(TestState.Playing);

        Assert.Equal(TestState.Playing, state.Current);
        Assert.Equal(TestState.Menu, state.Previous);
        Assert.True(state.Changed);

        state.ClearChanged();
        state.Set(TestState.Paused);

        Assert.Equal(TestState.Paused, state.Current);
        Assert.Equal(TestState.Playing, state.Previous);
        Assert.True(state.Changed);

        state.ClearChanged();
        state.Set(TestState.Playing);

        Assert.Equal(TestState.Playing, state.Current);
        Assert.Equal(TestState.Paused, state.Previous);
        Assert.True(state.Changed);
    }

    [Fact]
    public void Set_SameStateAfterClear_DoesNotSetChanged()
    {
        var state = new State<TestState>(TestState.Menu);
        state.ClearChanged();

        state.Set(TestState.Menu);

        Assert.False(state.Changed);
    }

#endregion
}
