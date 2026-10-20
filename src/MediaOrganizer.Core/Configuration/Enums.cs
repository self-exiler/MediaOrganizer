namespace MediaOrganizer.Core.Configuration;

/// <summary>文件操作类型。</summary>
public enum FileOperation { Copy, Move }

/// <summary>同名文件处理策略。</summary>
public enum ExistAction { Skip, Overwrite, Rename }

/// <summary>目标目录分级。声明顺序即工作台下拉顺序（日/月/年），JSON 按名称存取（JsonStringEnumConverter），
/// 重排不破坏旧配置。</summary>
public enum ClassificationLevel { Day, Month, Year }

/// <summary>网络位置协议。</summary>
public enum NetworkType { Smb, WebDav }
