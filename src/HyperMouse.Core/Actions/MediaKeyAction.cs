using HyperMouse.Core.Input;

namespace HyperMouse.Core.Actions;

public enum MediaCommand
{
    VolumeUp,
    VolumeDown,
    VolumeMute,
    PlayPause,
    NextTrack,
    PreviousTrack,
    Stop
}

public sealed class MediaKeyAction : IAction
{
    public MediaCommand Command { get; }
    public string DisplayName => $"Media: {Command}";

    public MediaKeyAction(MediaCommand command)
    {
        Command = command;
    }

    public Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        var vk = Command switch
        {
            MediaCommand.VolumeUp => VirtualKey.VolumeUp,
            MediaCommand.VolumeDown => VirtualKey.VolumeDown,
            MediaCommand.VolumeMute => VirtualKey.VolumeMute,
            MediaCommand.PlayPause => VirtualKey.MediaPlayPause,
            MediaCommand.NextTrack => VirtualKey.MediaNextTrack,
            MediaCommand.PreviousTrack => VirtualKey.MediaPrevTrack,
            MediaCommand.Stop => VirtualKey.MediaStop,
            _ => VirtualKey.VolumeMute
        };

        context.Input.KeyPress(vk);
        return Task.CompletedTask;
    }
}

