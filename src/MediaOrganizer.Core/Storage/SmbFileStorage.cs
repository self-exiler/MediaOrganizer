namespace MediaOrganizer.Core.Storage;

/// <summary>
/// SMB 共享存储：目标为 UNC 路径（\\server\share\...），走操作系统文件 API。
/// v1.0 仅 Windows 可用（macOS/Linux 需用户自行挂载后按本地路径使用）。
/// </summary>
public sealed class SmbFileStorage(string uncRoot) : LocalFileStorage(uncRoot);
