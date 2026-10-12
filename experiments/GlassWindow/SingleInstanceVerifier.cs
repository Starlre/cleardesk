using System.Diagnostics;

namespace ClearDesk.GlassWindow;

internal static class SingleInstanceVerifier
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        string key = "verification-" + Guid.NewGuid().ToString("N");
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var instance = await SingleInstance.AcquireAsync(() => restored.TrySetResult(), key))
        {
            check(instance is not null, "First launch acquires isolated single-instance ownership");
            using var second = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                ArgumentList = { "--instance-probe", key }
            })!;
            try
            {
                await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                string error = await second.StandardError.ReadToEndAsync();
                check(second.ExitCode == 0, $"Second process exits normally instead of creating duplicate windows and tray icon (exit={second.ExitCode}, error={error})");
                await restored.Task.WaitAsync(TimeSpan.FromSeconds(3));
                check(restored.Task.IsCompletedSuccessfully, "Second launch signals existing instance on UI dispatcher");
            }
            finally { if (!second.HasExited) { second.Kill(); await second.WaitForExitAsync(); } }
        }
        using var next = await SingleInstance.AcquireAsync(() => { }, key);
        check(next is not null, "Normal exit releases single-instance ownership for next launch");
    }
}
