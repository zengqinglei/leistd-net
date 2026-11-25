using System.Collections.Concurrent;
using System.Globalization;
using CompanyName.ProjectName.Application.Settings.Dtos;
using CompanyName.ProjectName.Application.Settings.Preferences;

namespace CompanyName.ProjectName.UnitTests.Application;

public sealed class RecipientTextRendererTests
{
    [Theory]
#if (IncludeLocalization)
    [InlineData("zh-CN", "zh-CN")]
#endif
    [InlineData("fr-FR", "en")]
    public async Task Rendering_uses_one_recipient_snapshot_and_an_enabled_language(string language, string expectedLanguage)
    {
        using var caller = new CallerCultures();
        var recipient = Guid.NewGuid();
        var store = new PreferenceStore((_, _) => Task.FromResult<AccountPreferencesOutputDto?>(Preferences(language, "Asia/Tokyo")));
        var result = await new RecipientTextRenderer(store).RenderAsync(recipient, ReadContext, useTimeZone: true);

        Assert.Equal((expectedLanguage, expectedLanguage, "Asia/Tokyo"), result);
        Assert.Equal(recipient, Assert.Single(store.Reads));
        caller.AssertRestored();
    }

#if (IncludeLocalization)
    [Fact]
    public async Task Recipients_can_render_concurrently_under_different_languages()
    {
        using var caller = new CallerCultures();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new PreferenceStore(async (userId, _) =>
        {
            await release.Task;
            return userId == first ? Preferences("en", "Europe/Paris") : Preferences("zh-CN", "Asia/Tokyo");
        });
        var renderer = new RecipientTextRenderer(store);
        var firstText = renderer.RenderAsync(first, ReadContext, useTimeZone: true);
        var secondText = renderer.RenderAsync(second, ReadContext, useTimeZone: true);
        release.SetResult();

        Assert.Equal([("en", "en", "Europe/Paris"), ("zh-CN", "zh-CN", "Asia/Tokyo")],
            await Task.WhenAll(firstText, secondText));
        Assert.Equal(new[] { first, second }, store.Reads);
        caller.AssertRestored();
    }
#else
    [Fact]
    public async Task Single_language_text_without_a_local_time_skips_preferences()
    {
        using var caller = new CallerCultures();
        var store = new PreferenceStore((_, _) => throw new InvalidOperationException("This render must not read preferences."));
        var result = await new RecipientTextRenderer(store).RenderAsync(Guid.NewGuid(), ReadContext);

        Assert.Equal(("en", "en", "UTC"), result);
        Assert.Empty(store.Reads);
        caller.AssertRestored();
    }
#endif

    [Fact]
    public async Task A_rendering_exception_restores_both_caller_cultures()
    {
        using var caller = new CallerCultures();
        var store = new PreferenceStore((_, _) => Task.FromResult<AccountPreferencesOutputDto?>(Preferences("en", null)));
        var renderer = new RecipientTextRenderer(store);
        var expected = new InvalidOperationException("Rendering failed.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderAsync<string>(Guid.NewGuid(), _ => throw expected));

        Assert.Same(expected, actual);
        caller.AssertRestored();
    }

    [Fact]
    public async Task Cancellation_during_preference_read_preserves_both_caller_cultures()
    {
        using var caller = new CallerCultures();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<AccountPreferencesOutputDto?>();
        var store = new PreferenceStore((_, token) =>
        {
            started.SetResult();
            return response.Task.WaitAsync(token);
        });
        var task = new RecipientTextRenderer(store).RenderAsync<string>(
            Guid.NewGuid(), _ => throw new InvalidOperationException("Canceled rendering must not run."),
            useTimeZone: true, cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        caller.AssertRestored();
    }

    private static (string Culture, string UiCulture, string TimeZone) ReadContext(TimeZoneInfo timeZone)
        => (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, timeZone.Id);

    private static AccountPreferencesOutputDto Preferences(string language, string? timeZone) => new()
    {
        Preferences = new AccountPreferenceValuesDto { Language = language, TimeZone = timeZone },
        PreferenceOverrides = new AccountPreferenceValuesDto()
    };

    private sealed class PreferenceStore(Func<Guid, CancellationToken, Task<AccountPreferencesOutputDto?>> read) : IAccountPreferenceStore
    {
        public ConcurrentQueue<Guid> Reads { get; } = new();

        public Task<AccountPreferencesOutputDto?> GetForCurrentUserAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AccountPreferencesOutputDto?> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Reads.Enqueue(userId);
            return read(userId, cancellationToken);
        }

        public Task<AccountPreferencesOutputDto?> SetForCurrentUserAsync(
            Guid userId, string field, string? value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CallerCultures : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUi = CultureInfo.CurrentUICulture;

        public CallerCultures()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        }

        public void AssertRestored()
        {
            Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            Assert.Equal("en-GB", CultureInfo.CurrentUICulture.Name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previous;
            CultureInfo.CurrentUICulture = _previousUi;
        }
    }
}
