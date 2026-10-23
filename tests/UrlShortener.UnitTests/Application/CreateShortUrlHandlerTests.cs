using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Application.Ports;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>
    /// TSK-08: every branch of the create flow (LLD §6.1) with in-memory fakes: validation before the database,
    /// one transaction, replay/mismatch/expired keys, alias conflicts, and the generated-code defect path (AC8).
    /// </summary>
    public sealed class CreateShortUrlHandlerTests
    {
        private const string Owner = "owner-1";
        private const string Url = "https://example.com/some/path?q=1";

        private readonly FakeUnitOfWork _unitOfWork = new FakeUnitOfWork();
        private readonly FakeRepository _repository = new FakeRepository();
        private readonly FakeGenerator _generator = new FakeGenerator();
        private readonly FakeIdempotencyStore _idempotency = new FakeIdempotencyStore();
        private readonly FakeTime _time = new FakeTime();
        private readonly CreateShortUrlHandler _handler;

        public CreateShortUrlHandlerTests()
        {
            Uri baseUrl = new Uri("https://sho.rt/");
            _handler = new CreateShortUrlHandler(
                _unitOfWork, _repository, _generator, _idempotency, new SelfLinkCheck(baseUrl), baseUrl, _time,
                NullLogger<CreateShortUrlHandler>.Instance);
        }

        [Fact]
        public async Task Generated_code_is_created_and_committed()
        {
            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, null));

            result.Outcome.Should().Be(CreateShortUrlOutcome.Created);
            result.Body!.Code.Should().Be("0000001");
            result.Body.ShortUrl.Should().Be(new Uri("https://sho.rt/0000001"));
            result.Body.LongUrl.OriginalString.Should().Be(Url);
            result.Body.CreatedAt.Should().Be(_time.Now);
            result.Body.ExpiresAt.Should().BeNull();
            _repository.Links["0000001"].OwnerId.Should().Be(Owner);
            _unitOfWork.Commits.Should().Be(1);
            _idempotency.Rows.Should().BeEmpty("no key was sent");
        }

        [Fact]
        public async Task Alias_is_created_with_that_code()
        {
            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand(Owner, Url, "spring-sale", null, null));

            result.Outcome.Should().Be(CreateShortUrlOutcome.Created);
            result.Body!.Code.Should().Be("spring-sale");
            _repository.Links["spring-sale"].IsCustom.Should().BeTrue();
            _generator.Calls.Should().Be(0);
        }

        [Fact]
        public async Task Taken_alias_returns_alias_taken_and_rolls_back_the_key()
        {
            await Handle(new CreateShortUrlCommand("someone-else", Url, "spring-sale", null, null));

            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand(Owner, Url, "spring-sale", null, "key-1"));

            result.Outcome.Should().Be(CreateShortUrlOutcome.AliasTaken);
            result.Body.Should().BeNull();
            _unitOfWork.Rollbacks.Should().Be(1, "a 409 stores nothing, so a retry runs again");
            _idempotency.Completed.Should().Be(0);
        }

        [Fact]
        public async Task Same_key_and_request_replays_the_stored_body()
        {
            CreateShortUrlResult first = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));
            _time.Now = _time.Now.AddMinutes(5);

            CreateShortUrlResult second = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));

            second.Outcome.Should().Be(CreateShortUrlOutcome.Replayed);
            second.Body.Should().Be(first.Body, "the stored response comes back, with the original createdAt");
            _repository.Links.Should().HaveCount(1, "no duplicate link");
            _generator.Calls.Should().Be(1);
        }

        [Fact]
        public async Task Same_key_with_a_different_request_is_key_reused()
        {
            await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));

            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand(Owner, Url + "&other", null, null, "key-1"));

            result.Outcome.Should().Be(CreateShortUrlOutcome.KeyReused);
            _repository.Links.Should().HaveCount(1);
        }

        [Fact]
        public async Task Same_key_from_another_owner_is_independent()
        {
            await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));

            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand("owner-2", Url, null, null, "key-1"));

            result.Outcome.Should().Be(CreateShortUrlOutcome.Created);
            _repository.Links.Should().HaveCount(2);
        }

        [Fact]
        public async Task Key_older_than_24_hours_creates_a_new_link()
        {
            CreateShortUrlResult first = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));
            _time.Now = _time.Now.AddHours(24).AddSeconds(1);

            CreateShortUrlResult second = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));

            second.Outcome.Should().Be(CreateShortUrlOutcome.Created);
            second.Body!.Code.Should().NotBe(first.Body!.Code);
        }

        [Fact]
        public async Task Generated_code_conflict_is_undone_and_retried_with_the_next_code()
        {
            _repository.ForcedGeneratedConflicts = 2;

            CreateShortUrlResult result = await Handle(new CreateShortUrlCommand(Owner, Url, null, null, null));

            result.Outcome.Should().Be(CreateShortUrlOutcome.Created);
            result.Body!.Code.Should().Be("0000003");
            _unitOfWork.Events.Count(e => e == "rollback-to:generated_code").Should().Be(2);
            _unitOfWork.Commits.Should().Be(1);
        }

        [Fact]
        public async Task Repeated_generated_conflicts_fail_without_committing()
        {
            _repository.ForcedGeneratedConflicts = CreateShortUrlHandler.MaxGeneratedAttempts;

            Func<Task> act = () => Handle(new CreateShortUrlCommand(Owner, Url, null, null, null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*collided*");
            _unitOfWork.Commits.Should().Be(0);
        }

        [Theory]
        [InlineData(null, DomainErrorCodes.UrlRequired)]
        [InlineData("ftp://example.com/x", DomainErrorCodes.UrlScheme)]
        [InlineData("https://user:pw@example.com/", DomainErrorCodes.UrlCredentials)]
        [InlineData("https://sho.rt/abc", ApplicationErrorCodes.UrlSelfLink)]
        [InlineData("https://SHO.RT/abc", ApplicationErrorCodes.UrlSelfLink)]
        public async Task Invalid_url_fails_before_any_database_work(string? input, string expectedCode)
        {
            Func<Task> act = () => Handle(new CreateShortUrlCommand(Owner, input, null, null, "key-1"));

            (await act.Should().ThrowAsync<DomainValidationException>()).Which.Code.Should().Be(expectedCode);
            _unitOfWork.Begun.Should().Be(0);
        }

        [Theory]
        [InlineData("abc", DomainErrorCodes.AliasLength)]
        [InlineData("spring1", DomainErrorCodes.AliasGeneratedShape)]
        [InlineData("health", DomainErrorCodes.AliasReserved)]
        [InlineData("bad alias", DomainErrorCodes.AliasCharacters)]
        public async Task Invalid_alias_fails_before_any_database_work(string alias, string expectedCode)
        {
            Func<Task> act = () => Handle(new CreateShortUrlCommand(Owner, Url, alias, null, null));

            (await act.Should().ThrowAsync<DomainValidationException>()).Which.Code.Should().Be(expectedCode);
            _unitOfWork.Begun.Should().Be(0);
        }

        [Fact]
        public async Task Expiry_is_rejected_until_it_is_supported()
        {
            Func<Task> act = () => Handle(new CreateShortUrlCommand(Owner, Url, null, _time.Now.AddDays(1), null));

            (await act.Should().ThrowAsync<DomainValidationException>()).Which.Code.Should().Be(ApplicationErrorCodes.ExpiryUnsupported);
        }

        [Fact]
        public async Task Exhausted_id_space_surfaces_and_rolls_back()
        {
            _generator.Failure = new IdSpaceExhaustedException();

            Func<Task> act = () => Handle(new CreateShortUrlCommand(Owner, Url, null, null, "key-1"));

            await act.Should().ThrowAsync<IdSpaceExhaustedException>();
            _unitOfWork.Rollbacks.Should().Be(1);
            _idempotency.Completed.Should().Be(0);
        }

        [Fact]
        public void Constructor_rejects_missing_dependencies()
        {
            Uri baseUrl = new Uri("https://sho.rt/");
            SelfLinkCheck check = new SelfLinkCheck(baseUrl);
            NullLogger<CreateShortUrlHandler> log = NullLogger<CreateShortUrlHandler>.Instance;
            Action[] constructors =
            {
                () => _ = new CreateShortUrlHandler(null!, _repository, _generator, _idempotency, check, baseUrl, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, null!, _generator, _idempotency, check, baseUrl, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, null!, _idempotency, check, baseUrl, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, _generator, null!, check, baseUrl, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, _generator, _idempotency, null!, baseUrl, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, _generator, _idempotency, check, null!, _time, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, _generator, _idempotency, check, baseUrl, null!, log),
                () => _ = new CreateShortUrlHandler(_unitOfWork, _repository, _generator, _idempotency, check, baseUrl, _time, null!),
            };

            foreach (Action constructor in constructors)
            {
                constructor.Should().Throw<ArgumentNullException>();
            }
        }

        [Fact]
        public async Task Null_command_throws()
        {
            Func<Task> act = () => _handler.HandleAsync(null!, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        private Task<CreateShortUrlResult> Handle(CreateShortUrlCommand command)
        {
            return _handler.HandleAsync(command, CancellationToken.None);
        }
    }
}
