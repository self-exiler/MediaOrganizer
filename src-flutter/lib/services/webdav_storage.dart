/// WebDAV 目标存储（对应 Core/Storage/WebDavFileStorage.cs，ADR-0005/FR-A9）。
/// 基于 dart:io HttpClient：PROPFIND 测试连接、MKCOL 递归建目录、
/// PUT 流式上传、MOVE 临时名改名；Basic 认证。8MB 分块由 HttpClient 流式写出自然形成。
library;

import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'credential_crypto.dart';
import 'file_storage.dart';

class WebDavStorage implements FileStorage {
  final String baseUrl; // https://dav.example.com/photos（末尾不带 /）
  final String username;
  final String password;

  @override
  final bool isNetwork = true;

  WebDavStorage({
    required this.baseUrl,
    this.username = '',
    String password = '',
  }) : password = CredentialCrypto.decrypt(password) {
    assert(baseUrl.startsWith('http'));
    _base = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl;
  }

  late final String _base;

  static final HttpClient _client = HttpClient()
    ..connectionTimeout = const Duration(seconds: 30);

  Uri _uri(String relativePath) {
    final encoded = relativePath
        .split('/')
        .where((s) => s.isNotEmpty)
        .map(Uri.encodeComponent)
        .join('/');
    return Uri.parse(encoded.isEmpty ? '$_base/' : '$_base/$encoded');
  }

  Map<String, String> get _authHeaders => username.isEmpty && password.isEmpty
      ? {}
      : {
          'Authorization':
              'Basic ${base64Encode(utf8.encode('$username:$password'))}'
        };

  /// 通用请求；返回响应（调用方负责关闭）。非 2xx/207 且不在 [accept] 内时抛异常。
  Future<HttpClientResponse> _send(
      String method, String relativePath,
      {Map<String, String>? headers,
      Stream<List<int>>? body,
      Set<int>? accept}) async {
    final req = await _client.openUrl(method, _uri(relativePath));
    req.headers.set(HttpHeaders.contentTypeHeader,
        headers?['Content-Type'] ?? 'application/octet-stream');
    _authHeaders.forEach(req.headers.set);
    headers?.forEach((k, v) {
      if (k != 'Content-Type') req.headers.set(k, v);
    });
    if (body != null) {
      await req.addStream(body);
    }
    final res = await req.close();
    final okStatus = res.statusCode >= 200 && res.statusCode < 300 ||
        (method == 'PROPFIND' && res.statusCode == 207);
    if (okStatus || (accept?.contains(res.statusCode) ?? false)) {
      // drain 响应体以便连接复用
      unawaited(res.drain<void>().catchError((_) {}));
      return res;
    }
    var reason = res.reasonPhrase;
    try {
      reason = await utf8.decodeStream(res);
      if (reason.length > 200) reason = reason.substring(0, 200);
    } catch (_) {}
    throw HttpException('HTTP ${res.statusCode} $reason', uri: res.uri);
  }

  /// 测试连接（认证 + 写权限探测，FR-A9.3）：PROPFIND 根 + MKCOL 探测目录。
  static Future<(bool, String)> testConnection(NetworkProfileLike p) async {
    try {
      final storage = WebDavStorage(
        baseUrl: p.address,
        username: p.username,
        password: p.password,
      );
      await storage._send('PROPFIND', '', headers: {'Depth': '0'});
      final probe = '.mo-probe-${DateTime.now().millisecondsSinceEpoch}';
      await storage._send('MKCOL', probe);
      await storage._send('DELETE', probe).catchError((_) {});
      return (true, '连接正常');
    } catch (e) {
      return (false, '连接失败：$e');
    }
  }

  @override
  Future<bool> exists(String relativePath) async {
    try {
      await _send('HEAD', relativePath);
      return true;
    } catch (_) {
      return false;
    }
  }

  @override
  Future<int> getLength(String relativePath) async {
    try {
      final req = await _client.openUrl('HEAD', _uri(relativePath));
      _authHeaders.forEach(req.headers.set);
      final res = await req.close();
      unawaited(res.drain<void>().catchError((_) {}));
      if (res.statusCode >= 200 && res.statusCode < 300) {
        return int.tryParse(res.headers.value('content-length') ?? '') ?? -1;
      }
      return -1;
    } catch (_) {
      return -1;
    }
  }

  @override
  Future<void> delete(String relativePath) => _send('DELETE', relativePath);

  @override
  Future<void> createDirectory(String relativeDir) async {
    // 逐段 MKCOL，405（Collection Already Exists）视为成功
    var cumulative = '';
    for (final seg in relativeDir.split('/')) {
      if (seg.isEmpty) continue;
      cumulative = '$cumulative/$seg';
      await _send('MKCOL', cumulative, accept: const {405});
    }
  }

  @override
  Future<void> copyFromLocal(File source, String relativeTarget,
      {void Function(int bytesDone)? onProgress}) async {
    var done = 0;
    Stream<List<int>> tracked() async* {
      await for (final chunk in source.openRead()) {
        done += chunk.length;
        onProgress?.call(done);
        yield chunk;
      }
    }

    await _send('PUT', relativeTarget, body: tracked());
  }

  @override
  Future<void> move(String fromRelative, String toRelative) =>
      _send('MOVE', fromRelative, headers: {
        'Destination': _uri(toRelative).toString(),
        'Overwrite': 'T',
      });

  @override
  Future<void> setModifiedUtc(String relativePath, DateTime utc) async {
    // PROPPATCH getlastmodified 多数服务器支持有限：按 FR-A5.5 失败时静默跳过
    try {
      final lastModified = HttpDate.format(utc.toUtc()); // RFC1123 惯例格式
      final xml = '<?xml version="1.0" encoding="utf-8"?>'
          '<D:propertyupdate xmlns:D="DAV:"><D:set><D:prop>'
          '<getlastmodified>$lastModified</getlastmodified>'
          '</D:prop></D:set></D:propertyupdate>';
      await _send('PROPPATCH', relativePath,
          headers: {'Content-Type': 'application/xml'},
          body: Stream.value(utf8.encode(xml)));
    } catch (_) {
      // 静默跳过
    }
  }
}

/// 避免循环依赖的轻量 profile 形状（仅 address/username/password）。
class NetworkProfileLike {
  final String address;
  final String username;
  final String password;

  const NetworkProfileLike(this.address, this.username, this.password);
}
