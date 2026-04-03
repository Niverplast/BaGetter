using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Authentication;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Tests.Authentication;

public class EntraRoleSyncServiceTests
{
    public class OnTokenValidatedAsync : FactsBase
    {
        [Fact]
        public async Task SkipsWhenObjectIdMissing()
        {
            var principal = CreatePrincipal(oid: null);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(
                s => s.FindByEntraObjectIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ProvisionsNewUser()
        {
            var principal = CreatePrincipal(oid: "oid-1", email: "alice@test.com", name: "Alice Test");
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-1", _ct)).ReturnsAsync((User)null);
            _userService.Setup(s => s.CreateEntraUserAsync("oid-1", "alice@test.com", "Alice Test", _ct))
                .ReturnsAsync(CreateUserEntity("oid-1", "alice@test.com"));

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(s => s.CreateEntraUserAsync("oid-1", "alice@test.com", "Alice Test", _ct));
        }

        [Fact]
        public async Task UpdatesDisplayNameForExistingUser()
        {
            var user = CreateUserEntity("oid-2", "bob@test.com", displayName: "Old Name");
            var principal = CreatePrincipal(oid: "oid-2", email: "bob@test.com", name: "New Name");
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-2", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            Assert.Equal("New Name", user.DisplayName);
            _userService.Verify(s => s.UpdateUserAsync(user, _ct));
        }

        [Fact]
        public async Task ThrowsWhenUserDisabled()
        {
            var user = CreateUserEntity("oid-3", "disabled@test.com");
            user.IsEnabled = false;
            var principal = CreatePrincipal(oid: "oid-3", roles: new[] { "Admin" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-3", _ct)).ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _target.OnTokenValidatedAsync(principal, _ct));

            _groupService.Verify(
                s => s.SyncAppRoleMembershipsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ThrowsWhenCanLoginToUIDisabled()
        {
            var user = CreateUserEntity("oid-nologin", "nologin@test.com");
            user.IsEnabled = true;
            user.CanLoginToUI = false;
            var principal = CreatePrincipal(oid: "oid-nologin", roles: new[] { "Admin" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-nologin", _ct)).ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _target.OnTokenValidatedAsync(principal, _ct));

            _groupService.Verify(
                s => s.SyncAppRoleMembershipsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GrantsAdminWhenAdminRolePresent()
        {
            var user = CreateUserEntity("oid-4", "admin@test.com");
            user.IsAdmin = false;
            var principal = CreatePrincipal(oid: "oid-4", roles: new[] { "Admin" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-4", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(s => s.SetAdminAsync(user.Id, true, _ct));
        }

        [Fact]
        public async Task RevokesAdminWhenAdminRoleMissing()
        {
            var user = CreateUserEntity("oid-5", "wasadmin@test.com");
            user.IsAdmin = true;
            var principal = CreatePrincipal(oid: "oid-5", roles: new[] { "TeamFrontend" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-5", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(s => s.SetAdminAsync(user.Id, false, _ct));
        }

        [Fact]
        public async Task DoesNotChangeAdminWhenAlreadyCorrect()
        {
            var user = CreateUserEntity("oid-6", "alreadyadmin@test.com");
            user.IsAdmin = true;
            var principal = CreatePrincipal(oid: "oid-6", roles: new[] { "Admin" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-6", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(s => s.SetAdminAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SyncsGroupMembershipsFromRoles()
        {
            var user = CreateUserEntity("oid-7", "roleuser@test.com");
            var principal = CreatePrincipal(oid: "oid-7", roles: new[] { "TeamFrontend", "TeamBackend" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-7", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _groupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 2),
                _ct));
        }

        [Fact]
        public async Task SyncsEmptyRolesList()
        {
            var user = CreateUserEntity("oid-8", "noroles@test.com");
            var principal = CreatePrincipal(oid: "oid-8", roles: Array.Empty<string>());
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-8", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            _groupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 0),
                _ct));
        }

        [Fact]
        public async Task ManualGroupsNotTouchedBySync()
        {
            var user = CreateUserEntity("oid-9", "manual@test.com");
            var principal = CreatePrincipal(oid: "oid-9", roles: new[] { "TeamQA" });
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-9", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            // SyncAppRoleMembershipsAsync is called which internally only touches role-linked groups
            _groupService.Verify(s => s.SyncAppRoleMembershipsAsync(
                user.Id,
                It.Is<IReadOnlyList<string>>(r => r.Count == 1 && r[0] == "TeamQA"),
                _ct));
        }

        [Fact]
        public async Task ReplacesNameIdentifierClaimWithBaGetterId()
        {
            var user = CreateUserEntity("oid-10", "claims@test.com");
            var principal = CreatePrincipal(oid: "oid-10");
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-10", _ct)).ReturnsAsync(user);

            await _target.OnTokenValidatedAsync(principal, _ct);

            var identity = principal.Identity as ClaimsIdentity;
            var nameIdClaim = identity?.FindFirst(ClaimTypes.NameIdentifier);
            Assert.NotNull(nameIdClaim);
            Assert.Equal(user.Id.ToString(), nameIdClaim.Value);

            var authProviderClaim = identity?.FindFirst("auth_provider");
            Assert.NotNull(authProviderClaim);
            Assert.Equal("Entra", authProviderClaim.Value);
        }

        [Fact]
        public async Task UsesCustomRoleClaim()
        {
            _options.Value.Authentication.Entra.RoleClaim = "custom_roles";
            var user = CreateUserEntity("oid-11", "custom@test.com");
            var principal = CreatePrincipal(oid: "oid-11", roles: new[] { "Admin" }, roleClaim: "custom_roles");
            _userService.Setup(s => s.FindByEntraObjectIdAsync("oid-11", _ct)).ReturnsAsync(user);
            user.IsAdmin = false;

            await _target.OnTokenValidatedAsync(principal, _ct);

            _userService.Verify(s => s.SetAdminAsync(user.Id, true, _ct));
        }
    }

    public class FactsBase
    {
        protected readonly Mock<IUserService> _userService;
        protected readonly Mock<IGroupService> _groupService;
        protected readonly IOptions<BaGetterOptions> _options;
        protected readonly EntraRoleSyncService _target;
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected FactsBase()
        {
            _userService = new Mock<IUserService>();
            _groupService = new Mock<IGroupService>();

            var opts = new BaGetterOptions
            {
                Authentication = new NugetAuthenticationOptions
                {
                    Entra = new EntraOptions
                    {
                        RoleClaim = "roles"
                    }
                }
            };
            _options = Options.Create(opts);

            _target = new EntraRoleSyncService(
                _userService.Object,
                _groupService.Object,
                _options,
                Mock.Of<ILogger<EntraRoleSyncService>>());
        }

        protected static User CreateUserEntity(string oid, string username, string displayName = null)
        {
            return new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                DisplayName = displayName ?? username,
                AuthProvider = AuthProvider.Entra,
                EntraObjectId = oid,
                IsEnabled = true,
                CanLoginToUI = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        protected static ClaimsPrincipal CreatePrincipal(
            string oid = null,
            string email = null,
            string name = null,
            string[] roles = null,
            string roleClaim = "roles")
        {
            var claims = new List<Claim>();

            if (oid != null)
                claims.Add(new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", oid));

            if (email != null)
                claims.Add(new Claim(ClaimTypes.Email, email));

            if (name != null)
                claims.Add(new Claim("name", name));

            if (roles != null)
            {
                foreach (var role in roles)
                    claims.Add(new Claim(roleClaim, role));
            }

            // Add a default NameIdentifier that should be replaced
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "entra-sub-value"));

            var identity = new ClaimsIdentity(claims, "TestAuth");
            return new ClaimsPrincipal(identity);
        }
    }
}
