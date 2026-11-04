using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Logging;
using MediaOrganizer.Core.Models;
using MediaOrganizer.Shared.Services;
using MediaOrganizer.Shared.ViewModels;

namespace MediaOrganizer.Shared.Tests;

/// <summary>
/// Shared VM 层的回归测试（走查 #13）：此前 test/ 只引用 Core，
/// 输出目标写坏、并发搬运、勾选计数这些竞态全靠真机发现。
/// </summary>
public sealed class FailedFilesViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mo-shared-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private FailedFilesViewModel CreateVM(IConfirmDialog? confirm = null)
    {
        var state = new AppState(new AppConfig(), [],
            Path.Combine(_dir, "config.json"), Path.Combine(_dir, "patterns.json"));
        return new FailedFilesViewModel(state, new AppLogger(),
            confirm ?? new ImmediateConfirm(), new NoOpOpener());
    }

    private static AnalysisResult Result(params string[] names)
        => new("src", DateTimeOffset.Now, [],
            names.Select(n => new UnparsedFile(new MediaFile(n, 1, "jpg"), "未识别")).ToArray());

    [Fact]
    public void Refresh_清空选中与预览()
    {
        var vm = CreateVM();
        vm.Refresh(Result("a.jpg"));
        vm.Selected = vm.Items[0];

        vm.Refresh(null);

        Assert.Null(vm.Selected);
        Assert.Equal("选择文件查看预览", vm.PreviewInfo);
        Assert.Empty(vm.Items);
        Assert.Equal("全选", vm.SelectAllText);
    }

    [Fact]
    public void 逐项勾选_全选文案按增量计数切换()
    {
        var vm = CreateVM();
        vm.Refresh(Result("a.jpg", "b.jpg", "c.jpg"));
        Assert.Equal("全选", vm.SelectAllText);

        foreach (var item in vm.Items) item.IsChecked = true;
        Assert.Equal("取消全选", vm.SelectAllText);

        vm.Items[0].IsChecked = false;
        Assert.Equal("全选", vm.SelectAllText);
    }

    [Fact]
    public async Task MoveToPending_确认等待期间已置忙碌()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVM(new BlockingConfirm(gate));
        vm.Refresh(Result("a.jpg"));
        vm.PendingDir = _dir;

        var task = vm.MoveToPendingCommand.ExecuteAsync(null);
        // 守卫必须在 await 确认弹窗之前置位，否则等待期间按钮可重复点击（走查 #7）
        Assert.True(vm.IsMoving);

        gate.SetResult(false); // 取消确认
        await task;
        Assert.False(vm.IsMoving);
    }

    private sealed class ImmediateConfirm : IConfirmDialog
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }

    private sealed class BlockingConfirm(TaskCompletionSource<bool> gate) : IConfirmDialog
    {
        public Task<bool> ConfirmAsync(string title, string message) => gate.Task;
    }

    private sealed class NoOpOpener : ISystemFileOpener
    {
        public Task OpenAsync(string identifier) => Task.CompletedTask;
    }
}
