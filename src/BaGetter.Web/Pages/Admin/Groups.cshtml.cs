using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Authentication;
using BaGetter.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BaGetter.Web.Pages.Admin;

[Authorize(AuthenticationSchemes = Core.Authentication.AuthenticationConstants.CookieScheme)]
public class GroupsModel : PageModel
{
    private readonly IGroupService _groupService;
    private readonly IUserService _userService;
    private readonly IPermissionService _permissionService;

    public GroupsModel(
        IGroupService groupService,
        IUserService userService,
        IPermissionService permissionService)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
    }

    public List<Group> Groups { get; set; } = new();
    public List<User> AllUsers { get; set; } = new();
    public Dictionary<Guid, FeedPermission> GroupPermissions { get; set; } = new();

    [FromQuery]
    public Guid? SavedGroupId { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Group name is required.")]
    [MaxLength(256)]
    public string NewGroupName { get; set; }

    [BindProperty]
    [MaxLength(128)]
    public string NewAppRoleValue { get; set; }

    [BindProperty]
    [MaxLength(4000)]
    public string NewDescription { get; set; }

    public string SuccessMessage { get; set; }
    public string ErrorMessage { get; set; }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return false;
        return await _userService.IsAdminAsync(userId, cancellationToken);
    }

    private async Task LoadGroupPermissionsAsync(CancellationToken cancellationToken)
    {
        foreach (var group in Groups)
        {
            var permission = await _permissionService.GetPermissionAsync(
                group.Id, PrincipalType.Group, "default", cancellationToken);
            if (permission != null)
            {
                GroupPermissions[group.Id] = permission;
            }
        }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateGroupAsync(CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!ModelState.IsValid)
        {
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        var existing = await _groupService.FindByNameAsync(NewGroupName, cancellationToken);
        if (existing != null)
        {
            ErrorMessage = $"Group '{NewGroupName}' already exists.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.CreateGroupAsync(
            NewGroupName,
            string.IsNullOrWhiteSpace(NewAppRoleValue) ? null : NewAppRoleValue.Trim(),
            NewDescription,
            cancellationToken);

        SuccessMessage = $"Group '{NewGroupName}' created successfully.";
        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAddUserAsync(
        Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await _groupService.CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            ErrorMessage = "Cannot manually add Entra users to role-linked groups. Membership is managed by Azure AD App Roles.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.AddUserToGroupAsync(userId, groupId, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveUserAsync(
        Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        if (!await _groupService.CanManuallyModifyMembershipAsync(groupId, userId, cancellationToken))
        {
            ErrorMessage = "Cannot manually remove Entra users from role-linked groups. Membership is managed by Azure AD App Roles.";
            Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
            AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
            await LoadGroupPermissionsAsync(cancellationToken);
            return Page();
        }

        await _groupService.RemoveUserFromGroupAsync(userId, groupId, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGrantPermissionAsync(
        Guid principalId,
        PrincipalType principalType,
        string feedId,
        bool canPush,
        bool canPull,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await _permissionService.GrantPermissionAsync(
            principalId, principalType, feedId ?? "default",
            canPush, canPull, cancellationToken);

        return RedirectToPage(new { savedGroupId = principalId });
    }

    public async Task<IActionResult> OnPostRevokePermissionAsync(
        Guid permissionId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await _permissionService.RevokePermissionAsync(permissionId, cancellationToken);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteGroupAsync(
        Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentUserAdminAsync(cancellationToken))
            return RedirectToPage("/Index");

        await _groupService.DeleteGroupAsync(groupId, cancellationToken);
        SuccessMessage = "Group deleted successfully.";

        Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
        AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
        await LoadGroupPermissionsAsync(cancellationToken);
        return Page();
    }
}
