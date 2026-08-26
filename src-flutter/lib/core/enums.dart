/// 文件操作与配置枚举（对应 Core/Configuration/Enums.cs）。
library;

/// 文件操作类型。
enum FileOperation { copy, move }

FileOperation fileOperationFromName(String? name) =>
    name == 'Move' ? FileOperation.move : FileOperation.copy;

/// 同名文件处理策略。
enum ExistAction { skip, overwrite, rename }

ExistAction existActionFromName(String? name) => switch (name) {
      'Overwrite' => ExistAction.overwrite,
      'Rename' => ExistAction.rename,
      _ => ExistAction.skip,
    };

/// 目标目录分级。
enum ClassificationLevel { year, month, day }

ClassificationLevel classificationLevelFromName(String? name) =>
    switch (name) {
      'Year' => ClassificationLevel.year,
      'Month' => ClassificationLevel.month,
      _ => ClassificationLevel.day,
    };

/// 网络位置协议（ADR-0004：明确不支持 FTP）。
enum NetworkType { smb, webDav }

NetworkType networkTypeFromName(String? name) =>
    name == 'WebDav' ? NetworkType.webDav : NetworkType.smb;

String networkTypeName(NetworkType t) =>
    t == NetworkType.webDav ? 'WebDav' : 'Smb';
