namespace LrSample.Logging;

using Humanizer;

public static class SampleLog
{
    // Humanizer comes from nuget.org: lrun go must restore third-party packages too
    public static string Uptime(TimeSpan uptime) => $"up {uptime.Humanize()}";
}
