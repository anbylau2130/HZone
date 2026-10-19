namespace HZone.Core.Interfaces;

/// <summary>
/// 媒体文件存储抽象：文件落在 {HZone:DataDirectory}/media/，数据库只存 storedName。
/// storedName 由服务端生成并校验（仅 [A-Za-z0-9._-]，禁止 ".."），防路径遍历。
/// </summary>
public interface IFileStorage
{
    /// <summary>保存文件，返回生成的 storedName。</summary>
    Task<string> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct = default);

    /// <summary>删除文件；文件不存在时静默成功。</summary>
    Task DeleteAsync(string storedName, CancellationToken ct = default);

    /// <summary>打开文件读取流；文件不存在时抛 FileNotFoundException。</summary>
    Task<Stream> OpenAsync(string storedName);
}
