using Android.App;
using Android.Content;
using MediaOrganizer.Shared.Services;

namespace MediaOrganizer.Android.Platforms;

/// <summary>
/// 系统 AlertDialog 单选列表（触屏选择字段的后端）。对话框是独立窗口层，与页面 ScrollViewer
/// 完全解耦，从机制上根除 Fluent ComboBox 打开时的 BringIntoView 页面滚动跳动；
/// 系统级大触控行（约 48–56dp）、单选圆钮、返回键/点外部取消全部免费获得，
/// 视觉与既有 AndroidConfirmDialog 一致（审查报告2 §2.3 方案A / 报告3 方案A）。
/// </summary>
public sealed class AndroidOptionPicker : IOptionPicker
{
    public Task<int> PickAsync(string title, IReadOnlyList<string> options, int selectedIndex)
    {
        var activity = MainActivity.Instance;
        if (activity is null || options.Count == 0) return Task.FromResult(-1);

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                // 与 AndroidConfirmDialog 同款链式写法：链式 setter 在 Java 绑定里返回可空 Builder，拆开逐句调
                var builder = new AlertDialog.Builder(activity);
                builder.SetTitle(title);
                builder.SetSingleChoiceItems(options.ToArray(), selectedIndex,
                    (d, e) =>
                    {
                        tcs.TrySetResult(e.Which);
                        ((IDialogInterface)d!).Dismiss();
                    });
                builder.SetNegativeButton("取消", (_, _) => tcs.TrySetResult(-1));
                builder.SetOnCancelListener(new CancelListener(() => tcs.TrySetResult(-1)));
                builder.Show();
            }
            catch (Exception)
            {
                // Activity 销毁后 Show() 抛 BadTokenException 等：不能让 TCS 永久悬挂 await 方
                tcs.TrySetResult(-1);
            }
        });
        return tcs.Task;
    }

    private sealed class CancelListener(Action onCanceled) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => onCanceled();
    }
}
