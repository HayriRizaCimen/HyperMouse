using HyperMouse.Core.Input;

namespace HyperMouse.Core.Actions;

public sealed class HotkeyAction : IAction
{
    public VirtualKey[] Modifiers { get; }
    public VirtualKey Key { get; }
    public string DisplayName => Modifiers.Length > 0 
        ? $"{string.Join(" + ", Modifiers)} + {Key}" 
        : Key.ToString();

    public HotkeyAction(VirtualKey[] modifiers, VirtualKey key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        context.Input.SendHotkey(Modifiers, Key);
        return Task.CompletedTask;
    }
}

