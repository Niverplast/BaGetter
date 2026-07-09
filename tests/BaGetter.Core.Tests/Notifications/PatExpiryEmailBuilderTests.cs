using System;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using BaGetter.Core.Notifications;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGetter.Core.Tests.Notifications;

public class PatExpiryEmailBuilderTests
{
    public class Build
    {
        private static PersonalAccessToken Token() => new()
        {
            Id = Guid.NewGuid(),
            Name = "ci-token",
            TokenPrefix = "bg_00000",
            ExpiresAtUtc = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            User = new User { Email = "owner@test.com" },
        };

        private static PatExpiryEmailBuilder BuildBuilder(string webBaseUrl = null)
        {
            return new PatExpiryEmailBuilder(Options.Create(new PatExpiryNotificationOptions { WebBaseUrl = webBaseUrl }));
        }

        [Fact]
        public void AddressesTheTokenOwnerAndDescribesTheDeadline()
        {
            var message = BuildBuilder().Build(Token(), daysUntilExpiry: 7);

            Assert.Equal(["owner@test.com"], message.To);
            Assert.Contains("in 7 days", message.Subject);
            Assert.Contains("ci-token", message.Subject);
        }

        [Theory]
        [InlineData(0, "today")]
        [InlineData(1, "in 1 day")]
        [InlineData(5, "in 5 days")]
        public void UsesFriendlyDeadlineWording(int daysUntil, string expected)
        {
            var message = BuildBuilder().Build(Token(), daysUntil);

            Assert.Contains(expected, message.Subject);
        }

        [Fact]
        public void IncludesTokenPageLinkWhenBaseUrlConfigured()
        {
            var message = BuildBuilder("https://packages.example.com/").Build(Token(), daysUntilExpiry: 2);

            Assert.Contains("""<a href="https://packages.example.com/account/tokens">""", message.Body);
        }

        [Fact]
        public void OmitsLinkWhenBaseUrlNotConfigured()
        {
            var message = BuildBuilder().Build(Token(), daysUntilExpiry: 2);

            Assert.DoesNotContain("<a href", message.Body);
            Assert.Contains("Tokens page", message.Body);
        }
    }
}
