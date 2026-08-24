using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.Tests.Commands;

public sealed class RelayCommandTests
{
    [Fact]
    public void GenericCommand_AcceptsStronglyTypedValueTypeParameters()
    {
        Selection? selected = null;
        RelayCommand<Selection> command = new(
            value => selected = value,
            value => value == Selection.Available);

        Assert.False(command.CanExecute(null));
        Assert.False(command.CanExecute(Selection.Unavailable));
        command.Execute(Selection.Unavailable);
        Assert.Null(selected);

        Assert.True(command.CanExecute(Selection.Available));
        command.Execute(Selection.Available);
        Assert.Equal(Selection.Available, selected);
    }

    [Fact]
    public async Task GenericAsyncCommand_AcceptsStronglyTypedValueTypeParameters()
    {
        TaskCompletionSource<Selection> executed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        AsyncCommand<Selection> command = new(
            value =>
            {
                executed.SetResult(value);
                return Task.CompletedTask;
            },
            value => value == Selection.Available);

        Assert.False(command.CanExecute(null));
        Assert.False(command.CanExecute(Selection.Unavailable));
        Assert.True(command.CanExecute(Selection.Available));

        command.Execute(Selection.Available);

        Assert.Equal(Selection.Available, await executed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private enum Selection
    {
        Unavailable,
        Available
    }
}
