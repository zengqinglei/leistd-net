using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Leistd.ServiceClient.Exceptions;
using OpenIddict.Abstractions;

namespace Leistd.ServiceClient.OAuth.Handlers;

internal sealed class TokenCache(IMemoryCache cache, ILogger<TokenCache> logger, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, Task<Token>> inFlight = new();

    internal sealed record Token(string Value, DateTimeOffset? ExpiresAt)
    {
        public DateTimeOffset ObtainedAt { get; init; }
        public DateTimeOffset? RetryAt { get; init; }
    }

    internal static string Key(params string?[] parts) => "leistd:oauth:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", parts))));

    internal async ValueTask<Token> GetAsync(string key, TimeSpan buffer,
        Func<CancellationToken, ValueTask<Token>> fetch, CancellationToken cancellationToken, bool renewEarly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = cache.Get<Token>(key);
        if (existing is not null && (!renewEarly || IsFresh(existing))) return existing;

        TaskCompletionSource<Token>? started = null;
        Task<Token> pending;
        lock (inFlight)
        {
            // 首次读取未命中后，回源可能已经完成并移除了任务。
            existing = cache.Get<Token>(key);
            if (existing is not null && (!renewEarly || IsFresh(existing))) return existing;
            if (!inFlight.TryGetValue(key, out pending!))
            {
                started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = inFlight[key] = started.Task;
            }
        }
        if (started is not null) _ = CompleteFetchAsync(started);

        if (!renewEarly || existing is null) return await pending.WaitAsync(cancellationToken);
        try
        {
            return await pending.WaitAsync(WaitBudget, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            if (pending.IsCompleted) return await pending;
            cancellationToken.ThrowIfCancellationRequested();
            return UseStale(key, buffer, timedOut: true) ?? await pending.WaitAsync(cancellationToken);
        }

        async Task CompleteFetchAsync(TaskCompletionSource<Token> completion)
        {
            try
            {
                completion.SetResult(await RefreshAsync(key, buffer, fetch, renewEarly));
            }
            catch (OperationCanceledException exception)
            {
                completion.SetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
                // 调用方可能都已结束等待。
                _ = completion.Task.Exception;
            }
        }
    }

    private async Task<Token> RefreshAsync(string key, TimeSpan buffer,
        Func<CancellationToken, ValueTask<Token>> fetch, bool renewEarly)
    {
        try
        {
            Token token;
            try
            {
                token = (await fetch(CancellationToken.None)) with { ObtainedAt = clock.GetUtcNow() };
            }
            catch (Exception exception)
            {
                var error = exception as ServiceClientException;
                // 发现配置服务可能包装内层异常，保留已经归一化的来源。
                for (var inner = exception.InnerException; error is null && inner is not null; inner = inner.InnerException)
                    error = inner as ServiceClientException;
                error ??= new ServiceClientException("OpenID Connect token request failed.", exception,
                    exception is OpenIddictExceptions.ProtocolException
                        ? ServiceClientFailureKind.InvalidResponse : ServiceClientFailureClassifier.Classify(exception));
                if (renewEarly && ServiceClientFailureClassifier.IsUnavailable(error))
                {
                    return UseStale(key, buffer, timedOut: false) ?? throw error;
                }

                if (error.FailureKind == ServiceClientFailureKind.RemoteFailure)
                    await RemoveAsync(key, CancellationToken.None);
                throw error;
            }

            lock (inFlight)
            {
                if (token.ExpiresAt is { } expires && expires - clock.GetUtcNow() - buffer is { } ttl && ttl > TimeSpan.Zero)
                    cache.Set(key, token, ttl);
            }
            return token;
        }
        finally
        {
            lock (inFlight) inFlight.Remove(key);
        }
    }

    // 401 删除之后，超时和后台失败都必须重读当前令牌。节流更新与写入、清除互斥。
    private Token? UseStale(string key, TimeSpan buffer, bool timedOut)
    {
        lock (inFlight)
        {
            var previous = cache.Get<Token>(key);
            var now = clock.GetUtcNow();
            if (previous?.ExpiresAt is not { } expiry || now >= expiry - buffer) return null;
            if (timedOut && IsFresh(previous)) return previous;
            var retry = now + RetryDelay;
            var result = previous with { RetryAt = retry < expiry - buffer ? retry : expiry - buffer };
            cache.Set(key, result, expiry - buffer - now);
            if (previous.RetryAt is null || now >= previous.RetryAt)
                logger.LogWarning("Token endpoint exceeded its wait budget or is unavailable; using a token stale for {Staleness} while validation may continue.",
                    now - (previous.ObtainedAt + (expiry - previous.ObtainedAt) / 2));
            return result;
        }
    }

    private bool IsFresh(Token token) => token.RetryAt > clock.GetUtcNow() || token.ExpiresAt is { } expiry &&
        clock.GetUtcNow() < token.ObtainedAt + (expiry - token.ObtainedAt) / 2;

    internal ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (inFlight) cache.Remove(key);
        return ValueTask.CompletedTask;
    }
}
