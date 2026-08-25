namespace MediaOrganizer.Core.Configuration;

/// <summary>文件操作类型。</summary>
public enum FileOperation { Copy, Move }

/// <summary>同名文件处理策略。</summary>
public enum ExistAction { Skip, Overwrite, Rename }

/// <summary>目标目录分级。</summary>
public enum ClassificationLevel { Year, Month, Day }

/// <summary>网络位置协议（ADR-0004：明确不支持 FTP）。</summary>
public enum NetworkType { Smb, WebDav }
