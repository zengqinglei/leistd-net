using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>前端静态文件的缓存头与压缩：带内容哈希的构建产物长期缓存并按请求压缩，其余每次校验、不压缩。</summary>
/// <remarks>
/// 缺了 no-cache，发版后浏览器会继续用旧的 index.html 与词条文件；
/// 反过来把不带哈希的文件标成 immutable，更新后一年内都取不到新内容。两头都不报错，只能在这里钉住。
/// 长期缓存的构建产物不论是否压缩都要带 Vary，否则缓存里的原文会交给支持压缩的请求。
/// </remarks>
public sealed class SpaStaticFileCachingTests : IDisposable
{
    private const string Fingerprinted = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";

    // 文件名取自真实构建产物：哈希的长度与字符集并不统一。assets/ 下的脚本与样式是压缩范围的反例
    private static readonly string[] WebRootFiles =
    [
        "index.html",
        "main-Q7K2WI64.js",
        "chunk-B6s-Xnrj.js",
        "styles-BTJSTJZA.css",
        "media/geist-latin-400-normal-3YTLMAJE.woff2",
        "i18n/en.json",
        "images/og-default.png",
        "favicon.svg",
        "assets/legacy.js",
        "assets/legacy.css",
    ];

    private readonly DirectoryInfo webRoot = Directory.CreateTempSubdirectory("spa-cache-");

    [Fact]
    public async Task Fingerprinted_bundles_are_cached_and_compressed_and_everything_else_is_revalidated()
    {
        foreach (var file in WebRootFiles)
        {
            var path = Path.Combine(webRoot.FullName, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file);
        }

        using var factory = new ProjectWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(WebHostDefaults.WebRootKey, webRoot.FullName));
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        // 每行：路径、是否协商压缩、期望的缓存头、期望的 Content-Encoding、是否带 Vary: Accept-Encoding
        (string Path, bool Negotiate, string Cache, string Encoding, bool Vary)[] cases =
        [
            ("/", true, Revalidate, "", false),
            ("/index.html", false, Revalidate, "", false),
            // SPA 回退：请求的是前端路由，发出的是 index.html
            ("/workspace/dashboard", false, Revalidate, "", false),
            ("/i18n/en.json", true, Revalidate, "", false),
            ("/images/og-default.png", false, Revalidate, "", false),
            ("/favicon.svg", false, Revalidate, "", false),
            ("/main-Q7K2WI64.js", true, Fingerprinted, "br", true),
            ("/main-Q7K2WI64.js", false, Fingerprinted, "", true),
            ("/chunk-B6s-Xnrj.js", false, Fingerprinted, "", true),
            ("/styles-BTJSTJZA.css", true, Fingerprinted, "br", true),
            ("/media/geist-latin-400-normal-3YTLMAJE.woff2", true, Fingerprinted, "", false),
            ("/assets/legacy.js", true, Revalidate, "", false),
            ("/assets/legacy.css", true, Revalidate, "", false),
        ];

        var mismatches = new List<string>();
        foreach (var (path, negotiate, expectedCache, expectedEncoding, expectedVary) in cases)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (negotiate)
            {
                request.Headers.AcceptEncoding.ParseAdd("br, gzip");
            }

            var response = await client.SendAsync(request);
            var cache = response.Headers.CacheControl?.ToString();
            var encoding = string.Join(',', response.Content.Headers.ContentEncoding);
            var varyCount = response.Headers.Vary.Count(value => value.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase));
            // 夹具文件的内容就是它的文件名；根路径与前端路由发出的是 index.html。未压缩时应能直接读出原文
            var source = path == "/" || path.StartsWith("/workspace", StringComparison.Ordinal) ? "index.html" : path.TrimStart('/');
            var readable = encoding.Length > 0 || await response.Content.ReadAsStringAsync() == source;
            if (response.StatusCode != HttpStatusCode.OK
                || cache != expectedCache
                || encoding != expectedEncoding
                || varyCount != (expectedVary ? 1 : 0)
                || !readable)
            {
                mismatches.Add($"{path} (negotiate={negotiate}): {(int)response.StatusCode} cache '{cache}' encoding '{encoding}' vary x{varyCount}，期望 200 '{expectedCache}' '{expectedEncoding}' vary x{(expectedVary ? 1 : 0)}");
            }
        }

        Assert.Empty(mismatches);
    }

    public void Dispose() => webRoot.Delete(recursive: true);
}
