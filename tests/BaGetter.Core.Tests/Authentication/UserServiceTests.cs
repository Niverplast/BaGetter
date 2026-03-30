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

public class UserServiceTests
{
    public class FindByUsernameAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenUserDoesNotExist()
        {
            var result = await _target.FindByUsernameAsync("nonexistent", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsUserWhenExists()
        {
            var user = await CreateLocalUser("testuser");

            var result = await _target.FindByUsernameAsync("testuser", _ct);

            Assert.NotNull(result);
            Assert.Equal(user.Id, result.Id);
            Assert.Equal("testuser", result.Username);
        }
    }

    public class FindByIdAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByIdAsync(Guid.NewGuid(), _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsUserById()
        {
            var user = await CreateLocalUser("findme");

            var result = await _target.FindByIdAsync(user.Id, _ct);

            Assert.NotNull(result);
            Assert.Equal("findme", result.Username);
        }
    }

    public class FindByEntraObjectIdAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullWhenNotFound()
        {
            var result = await _target.FindByEntraObjectIdAsync("not-an-oid", _ct);

            Assert.Null(result);
        }

        [Fact]
        public async Task ReturnsEntraUser()
        {
            var user = await _target.CreateEntraUserAsync(
                "oid-123", "entrauser", "Entra User", _ct);

            var result = await _target.FindByEntraObjectIdAsync("oid-123", _ct);

            Assert.NotNull(result);
            Assert.Equal(user.Id, result.Id);
            Assert.Equal(AuthProvider.Entra, result.AuthProvider);
        }
    }

    public class CreateEntraUserAsync : FactsBase
    {
        [Fact]
        public async Task CreatesEntraUserWithCorrectProperties()
        {
            var result = await _target.CreateEntraUserAsync(
                "oid-abc", "entrauser", "Entra Display", _ct);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("entrauser", result.Username);
            Assert.Equal("Entra Display", result.DisplayName);
            Assert.Equal(AuthProvider.Entra, result.AuthProvider);
            Assert.Equal("oid-abc", result.EntraObjectId);
            Assert.True(result.IsEnabled);
            Assert.True(result.CanLoginToUI);
            Assert.Null(result.PasswordHash);
        }
    }

    public class CreateLocalUserAsync : FactsBase
    {
        [Fact]
        public async Task CreatesLocalUserWithHashedPassword()
        {
            var result = await _target.CreateLocalUserAsync(
                "localuser", "Local User", "MyPassword123!", true, _adminUserId, _ct);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("localuser", result.Username);
            Assert.Equal(AuthProvider.Local, result.AuthProvider);
            Assert.True(result.IsEnabled);
            Assert.True(result.CanLoginToUI);
            Assert.NotNull(result.PasswordHash);
            Assert.NotEqual("MyPassword123!", result.PasswordHash);
            Assert.Equal(_adminUserId, result.CreatedByUserId);
        }
    }

    public class VerifyPasswordAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsTrueForCorrectPassword()
        {
            var user = await CreateLocalUser("pwduser", "CorrectPassword");

            var result = await _target.VerifyPasswordAsync(user, "CorrectPassword");

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsFalseForIncorrectPassword()
        {
            var user = await CreateLocalUser("pwduser", "CorrectPassword");

            var result = await _target.VerifyPasswordAsync(user, "WrongPassword");

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsFalseWhenNoPasswordHashSet()
        {
            var user = await _target.CreateEntraUserAsync(
                "oid-1", "entrauser", "Entra", _ct);

            var result = await _target.VerifyPasswordAsync(user, "anything");

            Assert.False(result);
        }
    }

    public class RecordFailedLoginAsync : FactsBase
    {
        [Fact]
        public async Task IncrementsFailedLoginCount()
        {
            var user = await CreateLocalUser("failuser");

            await _target.RecordFailedLoginAsync(user.Id, _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.Equal(1, updated.FailedLoginCount);
        }

        [Fact]
        public async Task LocksOutAfterMaxAttempts()
        {
            var user = await CreateLocalUser("lockuser");

            for (var i = 0; i < 5; i++)
            {
                await _target.RecordFailedLoginAsync(user.Id, _ct);
            }

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.Equal(5, updated.FailedLoginCount);
            Assert.NotNull(updated.LockedUntilUtc);
            Assert.True(updated.LockedUntilUtc > DateTime.UtcNow);
        }
    }

    public class IsLockedOutAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseWhenNotLocked()
        {
            var user = await CreateLocalUser("notlocked");

            var result = await _target.IsLockedOutAsync(user);

            Assert.False(result);
        }

        [Fact]
        public async Task ReturnsTrueWhenLockedInFuture()
        {
            var user = await CreateLocalUser("locked");
            user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(10);

            var result = await _target.IsLockedOutAsync(user);

            Assert.True(result);
        }

        [Fact]
        public async Task ReturnsFalseWhenLockExpired()
        {
            var user = await CreateLocalUser("expired");
            user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);

            var result = await _target.IsLockedOutAsync(user);

            Assert.False(result);
        }
    }

    public class ResetFailedLoginCountAsync : FactsBase
    {
        [Fact]
        public async Task ResetsCountAndLock()
        {
            var user = await CreateLocalUser("resetuser");

            // Simulate some failures and lockout
            for (var i = 0; i < 5; i++)
            {
                await _target.RecordFailedLoginAsync(user.Id, _ct);
            }

            await _target.ResetFailedLoginCountAsync(user.Id, _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.Equal(0, updated.FailedLoginCount);
            Assert.Null(updated.LockedUntilUtc);
        }
    }

    public class SetPasswordAsync : FactsBase
    {
        [Fact]
        public async Task UpdatesPassword()
        {
            var user = await CreateLocalUser("setpwd", "OldPassword");

            await _target.SetPasswordAsync(user.Id, "NewPassword", _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.True(await _target.VerifyPasswordAsync(updated, "NewPassword"));
            Assert.False(await _target.VerifyPasswordAsync(updated, "OldPassword"));
        }

        [Fact]
        public async Task ThrowsWhenUserNotFound()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.SetPasswordAsync(Guid.NewGuid(), "pwd", _ct));
        }
    }

    public class SetEnabledAsync : FactsBase
    {
        [Fact]
        public async Task DisablesUser()
        {
            var user = await CreateLocalUser("disableme");
            Assert.True(user.IsEnabled);

            await _target.SetEnabledAsync(user.Id, false, _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.False(updated.IsEnabled);
        }

        [Fact]
        public async Task ThrowsWhenUserNotFound()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.SetEnabledAsync(Guid.NewGuid(), false, _ct));
        }
    }

    public class SetCanLoginToUIAsync : FactsBase
    {
        [Fact]
        public async Task RevokesWebAccess()
        {
            var user = await CreateLocalUser("revokeui");
            Assert.True(user.CanLoginToUI);

            await _target.SetCanLoginToUIAsync(user.Id, false, _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.False(updated.CanLoginToUI);
        }

        [Fact]
        public async Task GrantsWebAccess()
        {
            var user = await CreateLocalUser("grantui");
            // Revoke first, then grant
            await _target.SetCanLoginToUIAsync(user.Id, false, _ct);
            var revoked = await _target.FindByIdAsync(user.Id, _ct);
            Assert.False(revoked.CanLoginToUI);

            await _target.SetCanLoginToUIAsync(user.Id, true, _ct);

            var updated = await _target.FindByIdAsync(user.Id, _ct);
            Assert.True(updated.CanLoginToUI);
        }

        [Fact]
        public async Task ThrowsWhenUserNotFound()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _target.SetCanLoginToUIAsync(Guid.NewGuid(), false, _ct));
        }
    }

    public class GetAllUsersAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsPreSeededAdminUser()
        {
            var result = await _target.GetAllUsersAsync(_ct);

            Assert.Single(result);
        }

        [Fact]
        public async Task ReturnsAllUsers()
        {
            await CreateLocalUser("user1");
            await CreateLocalUser("user2");

            var result = await _target.GetAllUsersAsync(_ct);

            // 2 local users + 1 pre-seeded admin
            Assert.Equal(3, result.Count);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext _context;
        protected readonly UserService _target;
        protected readonly CancellationToken _ct = CancellationToken.None;
        protected readonly Guid _adminUserId;

        protected FactsBase()
        {
            _context = TestDbContext.Create();

            // Create an admin user to serve as CreatedByUser for local accounts
            _adminUserId = Guid.NewGuid();
            _context.Users.Add(new User
            {
                Id = _adminUserId,
                Username = "admin",
                DisplayName = "Admin",
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = "oid-admin",
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            _context.SaveChanges();

            var authOptions = new NugetAuthenticationOptions
            {
                MaxFailedAttempts = 5,
                LockoutMinutes = 15
            };

            var optionsSnapshot = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
            optionsSnapshot.Setup(o => o.Value).Returns(authOptions);

            _target = new UserService(
                _context,
                optionsSnapshot.Object,
                Mock.Of<ILogger<UserService>>());
        }

        protected async Task<User> CreateLocalUser(string username, string password = "TestPassword123!")
        {
            return await _target.CreateLocalUserAsync(
                username, $"{username} Display",
                password, true, _adminUserId, _ct);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
