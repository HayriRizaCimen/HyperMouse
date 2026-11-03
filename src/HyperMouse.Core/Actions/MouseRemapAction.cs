using HyperMouse.Core.Common;

namespace HyperMouse.Core.Actions;

public sealed class MouseRemapAction : IAction
{
    public MouseButton TargetButton { get; }
    public string DisplayName => $"Mouse: {TargetButton}";

    public MouseRemapAction(MouseButton targetButton)
    {
        TargetButton = targetButton;
    }

    public Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        context.Input.MouseClick(TargetButton);
        return Task.CompletedTask;
    }
}

