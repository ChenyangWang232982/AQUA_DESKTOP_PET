using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AquaPet.Desktop.Infrastructure.Configuration;

/// <summary>
/// 按配置别名覆写 JSON 顶层字段。
/// 默认配置随应用发布；用户改动存储在 LocalAppData，不修改安装目录。
/// </summary>
public static class JsonConfigStore
{
    private const string PathFileName = "path.json";
    private const string ApplicationName = "AquaPet";

    public static async Task SaveAsync<T>(
        string fileAlias,
        string field,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var relativePath = await ResolveRelativePathAsync(fileAlias, cancellationToken);
        var targetPath = await EnsureUserConfigAsync(relativePath, cancellationToken);
        var configuration = await ReadObjectAsync(targetPath, cancellationToken);
        configuration[field] = JsonSerializer.SerializeToNode(value);

        var temporaryPath = $"{targetPath}.tmp";
        var json = configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    /// <summary>
    /// 按配置别名读取一个顶层字段。用户配置优先；不存在时回退到默认配置。
    /// </summary>
    public static async Task<T> CheckAsync<T>(
        string fileAlias,
        string field,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var relativePath = await ResolveRelativePathAsync(fileAlias, cancellationToken);
        var userPath = Path.Combine(GetUserConfigRoot(), relativePath);
        var defaultPath = Path.Combine(GetDefaultConfigRoot(), relativePath);
        var sourcePath = File.Exists(userPath) ? userPath : defaultPath;

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Configuration file was not found for alias: {fileAlias}", sourcePath);
        }

        var configuration = await ReadObjectAsync(sourcePath, cancellationToken);
        var value = configuration[field];
        if (value is null)
        {
            throw new KeyNotFoundException($"Field '{field}' was not found in configuration alias: {fileAlias}");
        }

        return value.Deserialize<T>()
            ?? throw new InvalidDataException($"Field '{field}' could not be read as {typeof(T).Name}.");
    }

    private static async Task<string> ResolveRelativePathAsync(string fileAlias, CancellationToken cancellationToken)
    {
        var pathFile = Path.Combine(GetDefaultConfigRoot(), PathFileName);
        var registry = await ReadObjectAsync(pathFile, cancellationToken);
        var relativePath = registry["files"]?[fileAlias]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new KeyNotFoundException($"Unknown configuration alias: {fileAlias}");
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid configuration path for alias: {fileAlias}");
        }

        return relativePath!;
    }

    private static async Task<string> EnsureUserConfigAsync(string relativePath, CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(GetUserConfigRoot(), relativePath);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(targetPath))
        {
            var defaultPath = Path.Combine(GetDefaultConfigRoot(), relativePath);
            if (File.Exists(defaultPath))
            {
                File.Copy(defaultPath, targetPath);
            }
            else
            {
                await File.WriteAllTextAsync(targetPath, "{}", cancellationToken);
            }
        }

        return targetPath!;
    }

    private static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"Configuration must be a JSON object: {path}");
    }

    private static string GetDefaultConfigRoot() => Path.Combine(AppContext.BaseDirectory, "config");

    private static string GetUserConfigRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationName,
        "config");
}
