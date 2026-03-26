# Authentication

BaGetter supports multiple authentication modes for controlling access to your NuGet feed. You can use Azure Entra ID (formerly Azure AD) for enterprise single sign-on, local database-backed accounts, or a hybrid of both.

## Authentication modes

The `Authentication.Mode` setting controls which authentication mechanisms are active:

| Mode | Description |
|------|-------------|
| `None` | (Default) Legacy behavior. Uses config-based API keys and basic auth credentials. No database-backed users. |
| `Entra` | Azure Entra ID (OIDC) authentication only. Users sign in via their organization's Entra ID tenant. |
| `Local` | Local database-backed accounts only. Admins create user accounts with passwords. |
| `Hybrid` | Both Entra ID and local accounts are active. Users can sign in with either method. |

```json
{
    "Authentication": {
        "Mode": "Hybrid"
    }
}
```

:::info

When `Mode` is `None` (or the `Authentication` section is omitted), BaGetter behaves exactly as before -- using `Credentials` and `ApiKeys` from configuration. Existing deployments require no changes.

:::

## Azure Entra ID setup

### Prerequisites

1. An Azure Entra ID (Azure AD) tenant
2. An App Registration in your tenant
3. A client secret for the App Registration
4. (Optional) A security group for admin users

### Step 1: Create an App Registration

1. Go to the [Azure Portal](https://portal.azure.com) > **Microsoft Entra ID** > **App registrations** > **New registration**
2. Set the **Name** (e.g., "BaGetter NuGet Feed")
3. Set **Supported account types** to "Accounts in this organizational directory only" (single tenant)
4. Set the **Redirect URI** to `https://your-bagetter-url/signin-oidc` (type: Web)
5. Click **Register**
6. Note the **Application (client) ID** and **Directory (tenant) ID** from the overview page

### Step 2: Create a client secret

1. In your App Registration, go to **Certificates & secrets** > **Client secrets** > **New client secret**
2. Add a description and expiration period
3. Copy the secret **Value** immediately (it will not be shown again)

### Step 3: Configure group claims (optional)

To enable group-based admin permissions:

1. In your App Registration, go to **Token configuration** > **Add groups claim**
2. Select **Security groups**
3. Under the **ID** token type, ensure **Group ID** is selected
4. Click **Add**
5. Create a security group in Entra ID (e.g., "BaGetter Admins") and add your admin users to it

### Step 4: Configure BaGetter

Add the Entra configuration to `appsettings.json`:

```json
{
    "Authentication": {
        "Mode": "Entra",
        "Entra": {
            "Instance": "https://login.microsoftonline.com/",
            "TenantId": "<your-tenant-id>",
            "ClientId": "<your-client-id>",
            "ClientSecret": "<your-client-secret>",
            "CallbackPath": "/signin-oidc",
            "AdminGroupName": "BaGetter Admins"
        }
    }
}
```

:::warning

Do not store the `ClientSecret` in `appsettings.json` in production. Use environment variables, Docker secrets, or a secrets manager instead:

```shell
# Environment variable
Authentication__Entra__ClientSecret=your-client-secret

# Docker secret file
/run/secrets/Authentication__Entra__ClientSecret
```

:::

### Entra configuration reference

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Instance` | Yes | -- | The Azure AD instance URL (e.g., `https://login.microsoftonline.com/`) |
| `TenantId` | Yes | -- | Your Azure AD tenant ID |
| `ClientId` | Yes | -- | The application (client) ID from your App Registration |
| `ClientSecret` | Yes | -- | The client secret value |
| `CallbackPath` | No | `/signin-oidc` | The OIDC callback path. Must match the redirect URI in your App Registration. |
| `AdminGroupName` | No | -- | Name of the Entra ID group whose members are granted admin permissions on the default feed |

### How Entra authentication works

When a user signs in via Entra ID:

1. The user is redirected to Microsoft's login page
2. After successful authentication, the OIDC token is validated
3. BaGetter automatically provisions a local user record linked to the Entra Object ID
4. Group memberships from the `groups` claim are synchronized
5. If the user belongs to the configured `AdminGroupId` group, they are granted admin permissions on the default feed
6. A session cookie (`BaGetter.Auth`) is issued with a 60-minute sliding expiration

## Local accounts

When `Mode` is `Local` or `Hybrid`, administrators can create local user accounts. Local accounts use bcrypt-hashed passwords and support account lockout after repeated failed login attempts.

### Account lockout

Local accounts are protected by an automatic lockout mechanism:

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxFailedAttempts` | `5` | Number of consecutive failed logins before lockout |
| `LockoutMinutes` | `15` | Duration (in minutes) that the account remains locked |

```json
{
    "Authentication": {
        "Mode": "Local",
        "MaxFailedAttempts": 5,
        "LockoutMinutes": 15
    }
}
```

After `MaxFailedAttempts` consecutive failed logins, the account is locked for `LockoutMinutes`. The counter resets on successful login.

## Personal access tokens (PATs)

Personal access tokens allow users to authenticate with NuGet CLI tools without using their interactive login credentials. PATs are available for both Entra and local users.

### How PATs work

- Users generate PATs from the web UI (available when signed in)
- Each token has a name and an expiration date
- The plaintext token is shown only once at creation time
- Tokens are stored as SHA-256 hashes in the database
- Tokens can be revoked at any time

### Token expiry

The maximum allowed token lifetime is controlled by the `MaxTokenExpiryDays` setting:

```json
{
    "Authentication": {
        "MaxTokenExpiryDays": 365
    }
}
```

### Using PATs with NuGet clients

Use the PAT as the password when configuring a NuGet source. The username can be any non-empty string:

```shell
# dotnet CLI
dotnet nuget add source "https://your-bagetter-url/v3/index.json" \
    --name "BaGetter" \
    --username "pat" \
    --password "<your-personal-access-token>"

# NuGet CLI
nuget sources add -Name "BaGetter" \
    -Source "https://your-bagetter-url/v3/index.json" \
    -UserName "pat" \
    -Password "<your-personal-access-token>"
```

To push packages using a PAT:

```shell
dotnet nuget push -s https://your-bagetter-url/v3/index.json \
    -k <your-personal-access-token> \
    package.1.0.0.nupkg
```

## Feed permissions

BaGetter uses a feed-scoped permission model. Permissions can be granted to individual users or groups.

| Permission | Description |
|------------|-------------|
| `Pull` | Can restore/download packages from the feed |
| `Push` | Can publish packages to the feed |
| `Admin` | Full control, including managing users, groups, and permissions |

Permissions are evaluated by the authorization handler on every NuGet API request. Users without the required permission for an operation receive a `401 Unauthorized` or `403 Forbidden` response.

### Group permissions

Permissions can be assigned to groups. A user inherits permissions from all groups they belong to. When Entra ID is enabled, group memberships are automatically synchronized from the Entra `groups` claim on each sign-in.

## Full configuration reference

```json
{
    "Authentication": {
        "Mode": "Hybrid",
        "Entra": {
            "Instance": "https://login.microsoftonline.com/",
            "TenantId": "<tenant-id>",
            "ClientId": "<client-id>",
            "ClientSecret": "<client-secret>",
            "CallbackPath": "/signin-oidc",
            "AdminGroupId": "<admin-group-object-id>"
        },
        "MaxTokenExpiryDays": 365,
        "MaxFailedAttempts": 5,
        "LockoutMinutes": 15,
        "Credentials": [
            {
                "Username": "legacy-user",
                "Password": "legacy-password"
            }
        ],
        "ApiKeys": [
            {
                "Key": "legacy-api-key"
            }
        ]
    }
}
```

:::info

The `Credentials` and `ApiKeys` arrays are only used when `Mode` is `None` (legacy mode). When `Mode` is `Entra`, `Local`, or `Hybrid`, authentication is handled through the database-backed user system and PATs.

:::

## Environment variables

All authentication settings can be provided via environment variables using the double-underscore (`__`) separator:

| Environment Variable | Description |
|---------------------|-------------|
| `Authentication__Mode` | Authentication mode (`None`, `Entra`, `Local`, `Hybrid`) |
| `Authentication__Entra__Instance` | Azure AD instance URL |
| `Authentication__Entra__TenantId` | Azure AD tenant ID |
| `Authentication__Entra__ClientId` | Application (client) ID |
| `Authentication__Entra__ClientSecret` | Client secret |
| `Authentication__Entra__CallbackPath` | OIDC callback path |
| `Authentication__Entra__AdminGroupId` | Admin group object ID (GUID) |
| `Authentication__MaxTokenExpiryDays` | Maximum PAT lifetime in days |
| `Authentication__MaxFailedAttempts` | Failed login threshold for lockout |
| `Authentication__LockoutMinutes` | Lockout duration in minutes |

## Docker Compose example

```yaml
services:
  bagetter:
    image: bagetter/bagetter:latest
    ports:
      - "5000:8080"
    environment:
      - Database__Type=PostgreSql
      - Database__ConnectionString=Host=db;Database=bagetter;Username=bagetter;Password=secret
      - Storage__Type=FileSystem
      - Storage__Path=/srv/baget/packages
      - Authentication__Mode=Entra
      - Authentication__Entra__Instance=https://login.microsoftonline.com/
      - Authentication__Entra__TenantId=your-tenant-id
      - Authentication__Entra__ClientId=your-client-id
      - Authentication__Entra__CallbackPath=/signin-oidc
      - Authentication__Entra__AdminGroupId=<admin-group-object-id>
    volumes:
      - bagetter-data:/srv/baget
    secrets:
      - entra_client_secret

secrets:
  entra_client_secret:
    file: ./secrets/entra-client-secret.txt
```

When using Docker secrets, create the secret file at the path matching the configuration key:

```shell
/run/secrets/Authentication__Entra__ClientSecret
```

## Database migrations

When upgrading from a version of BaGetter without authentication support, the required database tables (Users, Groups, UserGroups, PersonalAccessTokens, FeedPermissions) are created automatically on startup via EF Core migrations. No manual migration steps are needed.

## Troubleshooting

### "The 'TenantId' config is required for Entra authentication"

The `Authentication.Entra.TenantId` is missing or empty. Ensure your configuration or environment variables include the tenant ID from your Azure App Registration.

### "The 'ClientId' config is required for Entra authentication"

The `Authentication.Entra.ClientId` is missing or empty. Copy the Application (client) ID from the Azure Portal App Registration overview page.

### OIDC callback fails with "correlation failed"

This typically means the redirect URI in your Azure App Registration does not match the `CallbackPath` combined with your application's external URL. Verify:

1. The redirect URI in Azure is set to `https://your-external-url/signin-oidc`
2. Your reverse proxy (if any) is forwarding the `Host` header and the `X-Forwarded-Proto` header correctly
3. The application is using HTTPS in production

### Group claims are missing from the token

If users are not getting admin permissions despite being in the configured admin group:

1. Verify that **group claims** are configured in the App Registration under **Token configuration**
2. Check that the token includes the `groups` claim (use [jwt.ms](https://jwt.ms) to decode a token)
3. If you have more than 150 groups in your tenant, Azure will return a groups overage claim instead of inline groups. Consider using application roles or filtering groups in the App Registration manifest.

### Local account is locked out

If a local account is locked after too many failed attempts, wait for the lockout period to expire (`LockoutMinutes`, default 15 minutes). An administrator can also re-enable the account from the user management page.

### NuGet client returns 401 Unauthorized

1. Verify the NuGet source is configured with valid credentials (username/password or PAT)
2. Check that the user has `Pull` permission on the feed
3. If using a PAT, verify it has not expired or been revoked
4. Ensure the `Mode` setting matches your authentication method (e.g., do not use `Credentials` when `Mode` is `Entra`)
