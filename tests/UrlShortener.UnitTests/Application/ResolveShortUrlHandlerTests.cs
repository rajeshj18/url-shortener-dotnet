using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.Application.Ports;
using UrlShortener.Application.Redirects;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>TSK-09 AC2/AC4/AC5/AC7: the redirect use case with fakes (no database).</summary>
    public sealed class ResolveShortUrlHandlerTests
    {
        private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
        private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148";

        private readonly FakeLookup _lookup = new FakeLookup();
        private readonly FakeBudget _budget = new FakeBudget();
        private readonly RecordingPublisher _publisher = new RecordingPublisher();
        private readonly FakeTime _time = new FakeTime();
        private readonly ResolveShortUrlHandler _handler;

        public ResolveShortUrlHandlerTests()
        {
            _handler = new ResolveShortUrlHandler(_lookup, _budget, _publisher, new TokenUserAgentClassifier(), _time);
        }

        [Fact]
        public async Task Active_link_on_a_replica_redirects_and_publishes_one_event()
        {
            _lookup.Replica["aZ3k9Qx"] = Target("aZ3k9Qx", "https://example.com/a?b=1", LinkState.Active);

            ResolveResult result = await Resolve("aZ3k9Qx", Browser, "https://twitter.com/some/post?id=1");

            result.Outcome.Should().Be(ResolveOutcome.Redirect);
            result.Target!.OriginalString.Should().Be("https://example.com/a?b=1");
            _lookup.PrimaryCalls.Should().Be(0);
            ClickEvent click = _publisher.Events.Should().ContainSingle().Subject;
            click.SchemaVersion.Should().Be(1);
            click.Code.Should().Be("aZ3k9Qx");
            click.OccurredAt.Should().Be(_time.Now);
            click.ReferrerHost.Should().Be("twitter.com", "only the host; the path and query never leave");
            click.Device.Should().Be("desktop");
            click.EventId.ToString()[14].Should().Be('7', "a UUIDv7");
        }

        [Fact]
        public async Task Malformed_code_is_not_found_without_any_lookup()
        {
            ResolveResult result = await Resolve("../etc/passwd", Browser, null);

            result.Outcome.Should().Be(ResolveOutcome.NotFound);
            _lookup.ReplicaCalls.Should().Be(0);
            _lookup.PrimaryCalls.Should().Be(0);
        }

        [Fact]
        public async Task Replica_miss_falls_back_to_the_primary_while_the_budget_allows()
        {
            _lookup.Primary["new-link"] = Target("new-link", "https://example.com/new", LinkState.Active);

            ResolveResult result = await Resolve("new-link", Iphone, null);

            result.Outcome.Should().Be(ResolveOutcome.Redirect);
            _lookup.PrimaryCalls.Should().Be(1);
            _publisher.Events.Should().ContainSingle().Which.Device.Should().Be("mobile");
        }

        [Fact]
        public async Task Replica_miss_with_no_budget_left_is_not_found_without_a_primary_call()
        {
            _lookup.Primary["new-link"] = Target("new-link", "https://example.com/new", LinkState.Active);
            _budget.Allow = false;

            ResolveResult result = await Resolve("new-link", Browser, null);

            result.Outcome.Should().Be(ResolveOutcome.NotFound);
            _lookup.PrimaryCalls.Should().Be(0);
        }

        [Fact]
        public async Task Unknown_code_is_not_found_and_publishes_nothing()
        {
            ResolveResult result = await Resolve("unknown-code", Browser, null);

            result.Outcome.Should().Be(ResolveOutcome.NotFound);
            _publisher.Events.Should().BeEmpty();
        }

        [Fact]
        public async Task Deleted_link_is_gone_and_publishes_nothing()
        {
            _lookup.Replica["old-link"] = Target("old-link", "https://example.com/old", LinkState.Deleted);

            ResolveResult result = await Resolve("old-link", Browser, null);

            result.Outcome.Should().Be(ResolveOutcome.Gone);
            result.Target.Should().BeNull();
            _publisher.Events.Should().BeEmpty();
        }

        [Fact]
        public async Task Expired_link_is_gone_and_publishes_nothing()
        {
            _lookup.Replica["old-sale"] = new RedirectTarget(ParseCode("old-sale"), LongUrl.Create("https://example.com/sale"), LinkState.Active, _time.Now.AddSeconds(-1));
            _lookup.Replica["new-sale"] = new RedirectTarget(ParseCode("new-sale"), LongUrl.Create("https://example.com/sale"), LinkState.Active, _time.Now.AddSeconds(1));

            ResolveResult expired = await Resolve("old-sale", Browser, null);
            ResolveResult notYet = await Resolve("new-sale", Browser, null);

            expired.Outcome.Should().Be(ResolveOutcome.Gone);
            notYet.Outcome.Should().Be(ResolveOutcome.Redirect);
            _publisher.Events.Should().ContainSingle("only the active link counts a click").Which.Code.Should().Be("new-sale");
        }

        [Fact]
        public async Task Head_redirects_but_publishes_nothing()
        {
            _lookup.Replica["aZ3k9Qx"] = Target("aZ3k9Qx", "https://example.com/a", LinkState.Active);

            ResolveResult result = await _handler.ResolveAsync(new ResolveRequest("aZ3k9Qx", true, Browser, null), CancellationToken.None);

            result.Outcome.Should().Be(ResolveOutcome.Redirect);
            _publisher.Events.Should().BeEmpty();
        }

        [Theory]
        [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)")]
        [InlineData("facebookexternalhit/1.1")]
        [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
        [InlineData("curl/8.5.0")]
        [InlineData("")]
        public async Task Bots_are_redirected_but_not_counted(string userAgent)
        {
            _lookup.Replica["aZ3k9Qx"] = Target("aZ3k9Qx", "https://example.com/a", LinkState.Active);

            ResolveResult result = await Resolve("aZ3k9Qx", userAgent, null);

            result.Outcome.Should().Be(ResolveOutcome.Redirect);
            _publisher.Events.Should().BeEmpty();
        }

        [Fact]
        public async Task Constructor_and_inputs_are_guarded()
        {
            TokenUserAgentClassifier classifier = new TokenUserAgentClassifier();
            Action[] constructors =
            {
                () => _ = new ResolveShortUrlHandler(null!, _budget, _publisher, classifier, _time),
                () => _ = new ResolveShortUrlHandler(_lookup, null!, _publisher, classifier, _time),
                () => _ = new ResolveShortUrlHandler(_lookup, _budget, null!, classifier, _time),
                () => _ = new ResolveShortUrlHandler(_lookup, _budget, _publisher, null!, _time),
                () => _ = new ResolveShortUrlHandler(_lookup, _budget, _publisher, classifier, null!),
                () => ResolveResult.Redirect(null!),
            };

            foreach (Action constructor in constructors)
            {
                constructor.Should().Throw<ArgumentNullException>();
            }

            Func<Task> nullRequest = () => _handler.ResolveAsync(null!, CancellationToken.None);
            await nullRequest.Should().ThrowAsync<ArgumentNullException>();
        }

        private static ShortCode ParseCode(string code)
        {
            ShortCode.TryParse(code, out ShortCode? parsed).Should().BeTrue();
            return parsed!;
        }

        private static RedirectTarget Target(string code, string url, LinkState state)
        {
            ShortCode.TryParse(code, out ShortCode? parsed).Should().BeTrue();
            return new RedirectTarget(parsed!, LongUrl.Create(url), state);
        }

        private Task<ResolveResult> Resolve(string code, string? userAgent, string? referrer)
        {
            return _handler.ResolveAsync(new ResolveRequest(code, false, userAgent, referrer), CancellationToken.None);
        }

        private sealed class FakeLookup : IRedirectLookup
        {
            public Dictionary<string, RedirectTarget> Replica { get; } = new Dictionary<string, RedirectTarget>(StringComparer.Ordinal);

            public Dictionary<string, RedirectTarget> Primary { get; } = new Dictionary<string, RedirectTarget>(StringComparer.Ordinal);

            public int ReplicaCalls { get; private set; }

            public int PrimaryCalls { get; private set; }

            public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken cancellationToken)
            {
                ReplicaCalls++;
                Replica.TryGetValue(code.Value, out RedirectTarget? target);
                return Task.FromResult(target);
            }

            public Task<RedirectTarget?> FindOnPrimaryAsync(ShortCode code, CancellationToken cancellationToken)
            {
                PrimaryCalls++;
                Primary.TryGetValue(code.Value, out RedirectTarget? target);
                return Task.FromResult(target);
            }
        }

        private sealed class FakeBudget : IPrimaryFallbackBudget
        {
            public bool Allow { get; set; } = true;

            public bool TryAcquire()
            {
                return Allow;
            }
        }

        private sealed class RecordingPublisher : IClickEventPublisher
        {
            public List<ClickEvent> Events { get; } = new List<ClickEvent>();

            public bool TryPublish(ClickEvent clickEvent)
            {
                Events.Add(clickEvent);
                return true;
            }
        }
    }
}
