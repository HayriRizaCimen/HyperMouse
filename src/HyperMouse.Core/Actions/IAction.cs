using HyperMouse.Core.Input;

namespace HyperMouse.Core.Actions;

public sealed record ActionExecutionContext(
    IInputSimulator Input,
    int CurrentLayer
);

public interface IAction
{
    string DisplayName { get; }
    Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default);
}

