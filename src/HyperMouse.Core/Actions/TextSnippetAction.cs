using System.Diagnostics;

namespace HyperMouse.Core.Actions;

public sealed class TextSnippetAction : IAction
{
    public string Text { get; }
    public string DisplayName => $"Type: \"{(Text.Length > 20 ? Text[..20] + "..." : Text)}\"";

    public TextSnippetAction(string text)
    {
        Text = text;
    }

    public Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        context.Input.SendText(Text);
        return Task.CompletedTask;
    }
}

public sealed class LaunchAppAction : IAction
{
    public string ExecutablePath { get; }
    public string Arguments { get; }
    public string DisplayName => $"Launch: {System.IO.Path.GetFileName(ExecutablePath)}";

    public LaunchAppAction(string executablePath, string arguments = "")
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
    }

    public Task ExecuteAsync(ActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = Arguments,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Ignore execution failures for safety
        }

        return Task.CompletedTask;
    }
}

