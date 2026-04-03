# Group Management Refactor Plan

## Goal

Stop auto-importing all Entra groups. Groups are created manually by admins (optionally linked to an Entra Group ID). On Entra login, users are automatically added/removed from **Entra-linked** groups based on their token claims. Non-Entra (admin-managed) groups are never touched by the sync. Admins can delete any group (with confirmation popup).

---

## Current Behavior (what needs to change)

- **`GroupService.SyncEntraGroupMembershipsAsync`** (lines 124-131): If a user logs in with an Entra group claim that has no matching BaGetter group, it **auto-creates** a group named `Entra-{objectId}`. This must be removed.
- **`EntraGroupSyncService.EnsureAdminGroupPermissionsAsync`** (lines 130-134): Auto-creates the admin group if it doesn't exist. Same issue.
- **Admin UI (`Groups.cshtml`)**: No delete button exists for groups.
- **`IGroupService`**: No `DeleteGroupAsync` method exists.

---

## Changes

### 1. Remove auto-creation from `GroupService.SyncEntraGroupMembershipsAsync`

**File:** `src/BaGetter.Core/Authentication/GroupService.cs`

Change the sync loop (lines 119-138) so that when a group is **not found** by `FindByEntraGroupIdAsync`, it is simply **skipped** instead of created.

```csharp
// Current (REMOVE):
if (group == null)
{
    group = await CreateGroupAsync(
        $"Entra-{entraGroupId}", entraGroupId,
        "Auto-created from Entra ID group sync", cancellationToken);
}

// New:
if (group == null)
    continue;   // group not configured in BaGetter, skip
```

The removal logic (lines 141-149) already only touches Entra-linked groups the user is currently a member of, so it remains correct -- if the group doesn't exist in BaGetter, there's no membership to remove.

### 2. Remove auto-creation from `EntraGroupSyncService.EnsureAdminGroupPermissionsAsync`

**File:** `src/BaGetter/Authentication/EntraGroupSyncService.cs`

The admin group auto-creation (lines 130-134) should also be removed. If an admin wants the admin group synced, they must first create it in the admin UI and enter the Entra Group ID. Change:

```csharp
// Current (REMOVE):
var adminGroup = await _groupService.FindByEntraGroupIdAsync(adminGroupId, cancellationToken);
if (adminGroup == null)
{
    adminGroup = await _groupService.CreateGroupAsync("BaGetter Admins", adminGroupId,
        "Administrators synced from Entra", cancellationToken);
}

// New: just look it up, skip if not configured
var adminGroup = await _groupService.FindByEntraGroupIdAsync(adminGroupId, cancellationToken);
// Admin group not yet created by an admin -- skip group-based admin grant
```

Note: The `IsAdmin` flag on the user should still be set if they're in the admin group **and** the admin group exists. The only change is we don't auto-create the group entity. Keep the existing admin-check logic (`SetAdminAsync`) intact for when the group does exist.

### 3. Add `DeleteGroupAsync` to `IGroupService` and `GroupService`

**File:** `src/BaGetter.Core/Authentication/IGroupService.cs`

Add:
```csharp
Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken);
```

**File:** `src/BaGetter.Core/Authentication/GroupService.cs`

Implement:
```csharp
public async Task DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
{
    var group = await _context.Groups
        .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
    if (group == null) return;

    // Remove all memberships first
    var memberships = await _context.UserGroups
        .Where(ug => ug.GroupId == groupId)
        .ToListAsync(cancellationToken);
    _context.UserGroups.RemoveRange(memberships);

    // Remove all feed permissions for this group
    var permissions = await _context.FeedPermissions
        .Where(fp => fp.PrincipalId == groupId && fp.PrincipalType == PrincipalType.Group)
        .ToListAsync(cancellationToken);
    _context.FeedPermissions.RemoveRange(permissions);

    _context.Groups.Remove(group);
    await _context.SaveChangesAsync(cancellationToken);

    _logger.LogInformation("Deleted group {GroupName} (ID: {GroupId})", group.Name, groupId);
}
```

Note: The DB has cascade delete on UserGroups, but explicit removal is safer and keeps the FeedPermissions cleanup explicit (those don't cascade).

### 4. Add delete handler to `Groups.cshtml.cs`

**File:** `src/BaGetter.Web/Pages/Admin/Groups.cshtml.cs`

Add:
```csharp
public async Task<IActionResult> OnPostDeleteGroupAsync(
    Guid groupId, CancellationToken cancellationToken)
{
    if (!await IsCurrentUserAdminAsync(cancellationToken))
        return RedirectToPage("/Index");

    await _groupService.DeleteGroupAsync(groupId, cancellationToken);
    SuccessMessage = "Group deleted successfully.";

    Groups = await _groupService.GetAllGroupsAsync(cancellationToken);
    AllUsers = await _userService.GetAllUsersAsync(cancellationToken);
    return Page();
}
```

### 5. Add delete button with confirmation popup to `Groups.cshtml`

**File:** `src/BaGetter.Web/Pages/Admin/Groups.cshtml`

Add a delete button in each group's panel heading, next to the group name. Use a JavaScript `confirm()` dialog before submitting.

```html
<form method="post" asp-page-handler="DeleteGroup" style="display:inline"
      onsubmit="return confirm('Are you sure you want to delete this group? All memberships and permissions will be removed.');">
    @Html.AntiForgeryToken()
    <input type="hidden" name="groupId" value="@group.Id" />
    <button type="submit" class="btn btn-xs btn-danger">Delete</button>
</form>
```

Place this inside the `<div class="panel-heading">` block, after the group name `<h4>`.

### 6. Add indicator for Entra-linked vs admin-managed groups

In the groups list, make it visually clear which groups are Entra-linked (auto-synced membership) vs admin-managed (manual membership only). The current UI already shows `(Entra: <id>)` for Entra groups. Consider adding a small badge/label:

- Entra-linked groups: show `Entra-synced` badge
- Admin-managed groups: show `Manual` badge

This helps admins understand which groups will have members auto-managed on login.

---

## Summary of files to modify

| File | Change |
|------|--------|
| `src/BaGetter.Core/Authentication/IGroupService.cs` | Add `DeleteGroupAsync` |
| `src/BaGetter.Core/Authentication/GroupService.cs` | Remove auto-create in sync; add `DeleteGroupAsync` |
| `src/BaGetter/Authentication/EntraGroupSyncService.cs` | Remove auto-create of admin group |
| `src/BaGetter.Web/Pages/Admin/Groups.cshtml.cs` | Add `OnPostDeleteGroupAsync` handler |
| `src/BaGetter.Web/Pages/Admin/Groups.cshtml` | Add delete button with confirm popup; add group type badges |

---

## What stays the same

- **Entra login sync logic** (add/remove user from Entra-linked groups) -- stays, just skips unknown groups instead of creating them
- **Admin-managed groups** -- manual add/remove via the admin UI, untouched by Entra sync (they have no `EntraGroupId`)
- **AdminGroupId config** -- still used to set `IsAdmin` on users, but the group must be pre-created
- **Feed permissions** -- unchanged, still work for both users and groups
- **Group creation UI** -- already supports creating groups with optional Entra Group ID
