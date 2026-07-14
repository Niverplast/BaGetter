using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using BaGetter.Core.Email;
using BaGetter.Core.Entities;
using BaGetter.Core.Extensions;
using BaGetter.Core.Notifications;
using BaGetter.Core.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Notifications;

public class PatExpiryNotificationServiceTests
{
    public class RunOnceAsync : FactsBase
    {
        [Fact]
        public async Task SendsAtWidestThresholdAndRecordsIt()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 10);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(1, sent);
            Assert.Single(Sent);
            Assert.Equal(14, ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task DoesNotResendTheSameThreshold()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 10);
            var target = BuildTarget();

            await target.RunOnceAsync(Ct);
            var second = await target.RunOnceAsync(Ct);

            Assert.Equal(0, second);
            Assert.Single(Sent);
        }

        [Fact]
        public async Task DoesNotResendWhenAlreadyNotifiedAtANearerThreshold()
        {
            // Token is 5 days out -> due threshold 7, but it was already notified at the nearer
            // 2-day threshold. Since the recorded threshold <= the due threshold, no email is sent
            // and the recorded threshold is left untouched.
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 5, alreadyNotified: 2);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
            Assert.Empty(Sent);
            Assert.Equal(2, ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task SendsNextThresholdAsExpiryApproaches()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 10);
            var target = BuildTarget();

            await target.RunOnceAsync(Ct); // 14-day warning

            Now = Now.AddDays(5); // token now 5 days out -> 7-day threshold
            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(1, sent);
            Assert.Equal(2, Sent.Count);
            Assert.Equal(7, ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task SkipsTokensBeyondTheWidestThreshold()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 30);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
            Assert.Empty(Sent);
        }

        [Fact]
        public async Task DoesNothingWhenEmailIsNotConfigured()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 10);
            var target = BuildTarget(useNullSender: true);

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
            Assert.Empty(Sent);
            Assert.Null(ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task SkipsTokensThatHaveAlreadyExpired()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: -30);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
            Assert.Empty(Sent);
            Assert.Null(ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task SkipsRevokedTokens()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 5, isRevoked: true);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
        }

        [Fact]
        public async Task SkipsTokensOfDisabledUsers()
        {
            var user = SeedUser(isEnabled: false);
            SeedToken(user.Id, daysUntilExpiry: 5);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
        }

        [Fact]
        public async Task SkipsUsersWithoutAnEmailAddress()
        {
            var user = SeedUser(email: null);
            SeedToken(user.Id, daysUntilExpiry: 5);
            var target = BuildTarget();

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(0, sent);
            Assert.Null(ReloadToken().ExpiryNotificationThresholdDays);
        }

        [Fact]
        public async Task OneFailedSendDoesNotStarveOrDiscardTheRestOfTheBatch()
        {
            var user = SeedUser();
            SeedToken(user.Id, daysUntilExpiry: 5, name: "bad-token");
            SeedToken(user.Id, daysUntilExpiry: 5, name: "good-token");

            // Fail only the first token's email; the second must still be sent and recorded.
            var target = BuildTarget(throwFor: m => m.Subject.Contains("bad-token"));

            var sent = await target.RunOnceAsync(Ct);

            Assert.Equal(1, sent);
            Assert.Single(Sent);
            Assert.Contains("good-token", Sent[0].Subject);

            Assert.Equal(7, ReloadToken("good-token").ExpiryNotificationThresholdDays);
            Assert.Null(ReloadToken("bad-token").ExpiryNotificationThresholdDays);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly List<EmailMessage> Sent = new();
        protected readonly PatExpiryNotificationOptions Options = new();
        protected readonly CancellationToken Ct = CancellationToken.None;
        protected DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected FactsBase()
        {
            Context = TestDbContext.Create();
        }

        protected User SeedUser(string email = "user@test.com", bool isEnabled = true)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "user",
                DisplayName = "User",
                AuthProvider = AuthProvider.Local,
                Email = email,
                IsEnabled = isEnabled,
                CanLoginToUI = true,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            };
            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        protected PersonalAccessToken SeedToken(
            Guid userId,
            int daysUntilExpiry,
            string name = "ci-token",
            bool isRevoked = false,
            int? alreadyNotified = null)
        {
            var token = new PersonalAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                TokenHash = Guid.NewGuid().ToString("N"),
                TokenPrefix = "bg_00000",
                ExpiresAtUtc = Now.AddDays(daysUntilExpiry),
                CreatedAtUtc = Now.AddDays(-30),
                IsRevoked = isRevoked,
                ExpiryNotificationThresholdDays = alreadyNotified,
            };
            Context.PersonalAccessTokens.Add(token);
            Context.SaveChanges();
            return token;
        }

        protected PersonalAccessToken ReloadToken()
        {
            return Context.PersonalAccessTokens.AsNoTracking().Single();
        }

        protected PersonalAccessToken ReloadToken(string name)
        {
            return Context.PersonalAccessTokens.AsNoTracking().Single(t => t.Name == name);
        }

        protected PatExpiryNotificationService BuildTarget(Func<EmailMessage, bool> throwFor = null, bool useNullSender = false)
        {
            var emailSender = new Mock<IEmailSender>();
            emailSender
                .Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Callback<EmailMessage, CancellationToken>((m, _) =>
                {
                    if (throwFor != null && throwFor(m))
                        throw new InvalidOperationException("simulated send failure");
                    Sent.Add(m);
                })
                .Returns(Task.CompletedTask);

            // Use the real no-op sender to exercise the "email not configured" guard; otherwise the mock.
            IEmailSender sender = useNullSender
                ? new NullEmailSender(NullLogger<NullEmailSender>.Instance)
                : emailSender.Object;

            // Registered as a singleton (not scoped) so the container does not dispose our
            // shared context when the scanner disposes its per-run scope.
            var services = new ServiceCollection()
                .AddSingleton<IContext>(Context)
                .AddSingleton(sender)
                .BuildServiceProvider();

            var emailBuilder = new PatExpiryEmailBuilder(Microsoft.Extensions.Options.Options.Create(Options));

            // Re-evaluates on each access so tests can advance Now between runs.
            var systemTime = new Mock<SystemTime>();
            systemTime.Setup(t => t.UtcNow).Returns(() => Now);

            return new PatExpiryNotificationService(
                services,
                Microsoft.Extensions.Options.Options.Create(Options),
                emailBuilder,
                systemTime.Object,
                NullLogger<PatExpiryNotificationService>.Instance);
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
