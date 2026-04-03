using System;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using BaGetter.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Authentication;

public class TokenServiceTests
{
    public class CreateTokenAsync : FactsBase
    {
        [Fact]
        public async Task CreatesTokenWithCorrectProperties()
        {
            var result = await _target.CreateTokenAsync(
                _userId, "My Token", DateTime.UtcNow.AddDays(30), _ct);

            Assert.NotNull(result);
            Assert.NotNull(result.Token);
            Assert.NotNull(result.PlaintextToken);
            Assert.Equal("My Token", result.Token.Name);
            Assert.Equal(_userId, result.Token.UserId);
            Assert.False(result.Token.IsRevoked);
            Assert.NotEqual(Guid.Empty, result.Token.Id);
        }

        [Fact]
        public async Task PlaintextTokenStartsWithPrefix()
        {
            var result = await _target.CreateTokenAsync(
                _userId, "Prefixed", DateTime.UtcNow.AddDays(30), _ct);

            Assert.StartsWith("bg_", result.PlaintextToken);
        }

        [Fact]
        public async Task TokenPrefixIsStoredCorrectly()
        {
            var result = await _target.CreateTokenAsync(
                _userId, "PrefixCheck", DateTime.UtcNow.AddDays(30), _ct);

            Assert.Equal(8, result.Token.TokenPrefix.Length);
            Assert.Equal(result.PlaintextToken[..8], result.Token.TokenPrefix);
        }

        [Fact]
        public async Task TokenHashIsNotPlaintext()
        {
            var result = await _target.CreateTokenAsync(
                _userId, "HashCheck", DateTime.UtcNow.AddDays(30), _ct);

            Assert.NotEqual(result.PlaintextToken, result.Token.TokenHash);
            Assert.NotEmpty(result.Token.TokenHash);
        }

        [Fact]
        public async Task ThrowsWhenExpiryExceedsMaxDays()
        {
            var tooFar = DateTime.UtcNow.AddDays(366);

            await Assert.ThrowsAsync<ArgumentException>(
                () => _target.CreateTokenAsync(_userId, "TooLong", tooFar, _ct));
        }

        [Fact]
        public async Task GeneratesUniqueTokensEachTime()
        {
            var result1 = await _target.CreateTokenAsync(
                _userId, "Token1", DateTime.UtcNow.AddDays(30), _ct);
            var result2 = await _target.CreateTokenAsync(
                _userId, "Token2", DateTime.UtcNow.AddDays(30), _ct);

            Assert.NotEqual(result1.PlaintextToken, result2.PlaintextToken);
            Assert.NotEqual(result1.Token.TokenHash, result2.Token.TokenHash);
        }
    }

    public class ValidateTokenAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsTokenForValidPlaintext()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "Valid", DateTime.UtcNow.AddDays(30), _ct);

            var result = await _target.ValidateTokenAsync(created.PlaintextToken, _ct);

            Assert.NotNull(result);
            Assert.Equal(created.Token.Id, result.Id);
        }

        [Fact]
        public async Task ReturnsNullForUnknownToken()
        {
            var result = await _target.ValidateTokenAsync("bg_unknowntoken1234567890abcdef12345678", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullForEmptyToken()
        {
            var result = await _target.ValidateTokenAsync("", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullForNullToken()
        {
            var result = await _target.ValidateTokenAsync(null, _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullForRevokedToken()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "Revoked", DateTime.UtcNow.AddDays(30), _ct);

            await _target.RevokeTokenAsync(created.Token.Id, _ct);

            var result = await _target.ValidateTokenAsync(created.PlaintextToken, _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullForExpiredToken()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "Expired", DateTime.UtcNow.AddDays(30), _ct);

            // Manually set expiry to the past
            created.Token.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await _context.SaveChangesAsync(_ct);

            var result = await _target.ValidateTokenAsync(created.PlaintextToken, _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsNullForDisabledUser()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "DisabledUser", DateTime.UtcNow.AddDays(30), _ct);

            // Disable the user
            var user = await _context.Users.FindAsync(_userId);
            user.IsEnabled = false;
            await _context.SaveChangesAsync(_ct);

            var result = await _target.ValidateTokenAsync(created.PlaintextToken, _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task UpdatesLastUsedTimestamp()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "LastUsed", DateTime.UtcNow.AddDays(30), _ct);

            Assert.Null(created.Token.LastUsedAtUtc);

            await _target.ValidateTokenAsync(created.PlaintextToken, _ct);

            var token = await _context.PersonalAccessTokens.FindAsync(created.Token.Id);
            Assert.NotNull(token.LastUsedAtUtc);
        }
    }

    public class RevokeTokenAsync : FactsBase
    {
        [Fact]
        public async Task SetsRevokedFlag()
        {
            var created = await _target.CreateTokenAsync(
                _userId, "ToRevoke", DateTime.UtcNow.AddDays(30), _ct);

            await _target.RevokeTokenAsync(created.Token.Id, _ct);

            var token = await _context.PersonalAccessTokens.FindAsync(created.Token.Id);
            Assert.True(token.IsRevoked);
            Assert.NotNull(token.RevokedAtUtc);
        }

        [Fact]
        public async Task DoesNothingWhenTokenNotFound()
        {
            await _target.RevokeTokenAsync(Guid.NewGuid(), _ct);

            // No exception should be thrown
        }
    }

    public class GetUserTokensAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmptyWhenNoTokens()
        {
            var result = await _target.GetUserTokensAsync(_userId, _ct);

            Assert.Empty(result);
        }

        [Fact]
        public async Task ReturnsTokensForUser()
        {
            await _target.CreateTokenAsync(_userId, "Token1", DateTime.UtcNow.AddDays(30), _ct);
            await _target.CreateTokenAsync(_userId, "Token2", DateTime.UtcNow.AddDays(60), _ct);

            var result = await _target.GetUserTokensAsync(_userId, _ct);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task DoesNotReturnOtherUsersTokens()
        {
            var otherUserId = Guid.NewGuid();
            _context.Users.Add(new User
            {
                Id = otherUserId,
                Username = "otheruser",
                DisplayName = "Other",
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = "oid-other",
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(_ct);

            await _target.CreateTokenAsync(_userId, "MyToken", DateTime.UtcNow.AddDays(30), _ct);
            await _target.CreateTokenAsync(otherUserId, "TheirToken", DateTime.UtcNow.AddDays(30), _ct);

            var result = await _target.GetUserTokensAsync(_userId, _ct);

            Assert.Single(result);
            Assert.Equal("MyToken", result[0].Name);
        }

        [Fact]
        public async Task ReturnsTokensOrderedByCreatedDescending()
        {
            var result1 = await _target.CreateTokenAsync(
                _userId, "First", DateTime.UtcNow.AddDays(30), _ct);

            // Manually adjust CreatedAtUtc to ensure ordering
            result1.Token.CreatedAtUtc = DateTime.UtcNow.AddHours(-2);
            await _context.SaveChangesAsync(_ct);

            await _target.CreateTokenAsync(
                _userId, "Second", DateTime.UtcNow.AddDays(30), _ct);

            var tokens = await _target.GetUserTokensAsync(_userId, _ct);

            Assert.Equal(2, tokens.Count);
            Assert.Equal("Second", tokens[0].Name);
            Assert.Equal("First", tokens[1].Name);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext _context;
        protected readonly TokenService _target;
        protected readonly CancellationToken _ct = CancellationToken.None;
        protected readonly Guid _userId;

        protected FactsBase()
        {
            _context = TestDbContext.Create();

            _userId = Guid.NewGuid();
            _context.Users.Add(new User
            {
                Id = _userId,
                Username = "tokenuser",
                DisplayName = "Token User",
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = "oid-tokenuser",
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            _context.SaveChanges();

            var authOptions = new NugetAuthenticationOptions
            {
                MaxTokenExpiryDays = 365
            };

            var optionsSnapshot = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            optionsSnapshot.Setup(o => o.Value).Returns(authOptions);

            _target = new TokenService(
                _context,
                optionsSnapshot.Object,
                Mock.Of<ILogger<TokenService>>());
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
