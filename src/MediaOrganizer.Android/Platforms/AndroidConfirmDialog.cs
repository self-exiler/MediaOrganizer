using Android.App;
using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>系统 AlertDialog 确认弹窗（执行前确认 / 批量移动确认），UI 线程回调转 Task。</summary>
public sealed class AndroidConfirmDialog : IConfirmDialog
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var activity = MainActivity.Instance;
        if (activity is null) return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            // 链式 setter 在 Java 绑定里返回可空 Builder，拆开逐句调避免空解引用
            var builder = new AlertDialog.Builder(activity);
            builder.SetTitle(title);
            builder.SetMessage(message);
            builder.SetPositiveButton("确定", (_, _) => tcs.TrySetResult(true));
            builder.SetNegativeButton("取消", (_, _) => tcs.TrySetResult(false));
            builder.SetOnCancelListener(new CancelListener(() => tcs.TrySetResult(false)));
            builder.Show();
        });
        return tcs.Task;
    }

    private sealed class CancelListener(Action onCanceled) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => onCanceled();
    }
}
