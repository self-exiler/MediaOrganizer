using Android.App;
using Android.OS;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>确认对话框（FR-A6.5 批量移动确认）：Android AlertDialog。</summary>
public sealed class AndroidConfirmDialog : IConfirmDialog
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Handler(Looper.MainLooper!).Post(() =>
        {
            new AlertDialog.Builder(MainActivity.Instance!)
                .SetTitle(title)
                .SetMessage(message)
                .SetPositiveButton("确认", (_, _) => tcs.TrySetResult(true))
                .SetNegativeButton("取消", (_, _) => tcs.TrySetResult(false))
                .Show();
        });
        return tcs.Task;
    }
}
