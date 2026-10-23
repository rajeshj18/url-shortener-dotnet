using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-05 AC5: every domain exception carries a stable code.</summary>
    public sealed class DomainValidationExceptionTests
    {
        [Fact]
        public void Coded_constructor_keeps_code_and_message()
        {
            DomainValidationException ex = new DomainValidationException(DomainErrorCodes.AliasReserved, "This alias is reserved.");

            ex.Code.Should().Be("alias.reserved");
            ex.Message.Should().Be("This alias is reserved.");
        }

        [Fact]
        public void Standard_constructors_use_the_generic_code()
        {
            new DomainValidationException().Code.Should().Be("domain.invalid");
            new DomainValidationException("message").Code.Should().Be("domain.invalid");

            InvalidOperationException inner = new InvalidOperationException("inner");
            DomainValidationException wrapped = new DomainValidationException("message", inner);
            wrapped.Code.Should().Be("domain.invalid");
            wrapped.InnerException.Should().BeSameAs(inner);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Coded_constructor_requires_a_code(string? code)
        {
            Action act = () => _ = new DomainValidationException(code!, "message");

            act.Should().Throw<ArgumentException>();
        }
    }
}
