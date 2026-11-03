using HyperMouse.Core.Actions;
using HyperMouse.Core.Common;
using HyperMouse.Core.Input;
using HyperMouse.Tests.Mocks;

namespace HyperMouse.Tests;

public class ActionTests
{
    private readonly MockInputSimulator _mockInput = new();

    [Fact]
    public async Task HotkeyAction_ShouldSendModifiersAndKeyInCorrectOrder()
    {
        var action = new HotkeyAction(new[] { VirtualKey.Control, VirtualKey.Shift }, VirtualKey.S);
        var context = new ActionExecutionContext(_mockInput, 0);

        await action.ExecuteAsync(context);

        Assert.Equal(
            new[] { "KeyDown:Control", "KeyDown:Shift", "KeyDown:S", "KeyUp:S", "KeyUp:Shift", "KeyUp:Control" },
            _mockInput.Invocations
        );
    }

    [Fact]
    public async Task MediaKeyAction_ShouldSendKeyPress()
    {
        var action = new MediaKeyAction(MediaCommand.VolumeUp);
        var context = new ActionExecutionContext(_mockInput, 0);

        await action.ExecuteAsync(context);

        Assert.Equal(
            new[] { "KeyDown:VolumeUp", "KeyUp:VolumeUp" },
            _mockInput.Invocations
        );
    }

    [Fact]
    public async Task MouseRemapAction_ShouldSendMouseClick()
    {
        var action = new MouseRemapAction(MouseButton.Middle);
        var context = new ActionExecutionContext(_mockInput, 0);

        await action.ExecuteAsync(context);

        Assert.Equal(
            new[] { "MouseDown:Middle", "MouseUp:Middle" },
            _mockInput.Invocations
        );
    }

    [Fact]
    public async Task TextSnippetAction_ShouldSendText()
    {
        var action = new TextSnippetAction("Hello World");
        var context = new ActionExecutionContext(_mockInput, 0);

        await action.ExecuteAsync(context);

        Assert.Single(_mockInput.Invocations);
        Assert.Equal("SendText:Hello World", _mockInput.Invocations[0]);
    }
}

