<#
.SYNOPSIS
    Creates the Entra application registration Printendar signs in with.

.DESCRIPTION
    Printendar is a public client desktop application. It holds no secret, asks only for
    read access to calendars, and its client id is public by design, which is why it can
    live in an open source repository and in a downloaded binary.

    The registration is created with signInAudience "AzureADandPersonalMicrosoftAccount",
    which is the setting that answers the usual question about who can use it:

      - The tenant this is created in decides only who ADMINISTERS the registration.
      - It does NOT decide who can sign in. Any work or school account from any
        organisation, and any personal Microsoft account, can sign in to the same
        registration.

    So a registration created under a personal account still works for company accounts,
    and the application is not associated with any particular organisation.

    Two things an organisation might still need, both already handled by the app:

      - A tenant that has disabled user consent for third party applications needs an
        administrator to consent once. The URL for that is printed at the end.
      - A tenant that will not consent at all can register its own copy of this and point
        Printendar at it with the PRINTENDAR_MS_CLIENT_ID environment variable.

.PARAMETER DisplayName
    The name shown on the consent screen. This is what users see when they sign in, so it
    should read as a product name.

.PARAMETER WhatIf
    Show what would be created without creating it.

.EXAMPLE
    ./New-PrintendarAppRegistration.ps1

.NOTES
    Requires the Microsoft.Graph.Authentication module, which supplies Connect-MgGraph and
    Invoke-MgGraphRequest. The heavier Microsoft.Graph.Applications module is deliberately
    not required; this talks to the REST API directly.

    Sign in with the account that should OWN the registration. The script shows which
    tenant it is about to create in and asks before doing anything.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DisplayName = 'Printendar',

    [string]$RedirectUri = 'http://localhost'
)

$ErrorActionPreference = 'Stop'

# Well known: the Microsoft Graph resource itself. Constant across every tenant.
$graphAppId = '00000003-0000-0000-c000-000000000000'

# Delegated permissions. Read only, and no broader than drawing a calendar needs.
# Calendars.Read.Shared covers calendars other people have shared with the user, which is
# most of the reason anyone prints a month.
$requestedScopes = @('User.Read', 'Calendars.Read', 'Calendars.Read.Shared')

function Assert-GraphModule {
    if (-not (Get-Module -ListAvailable -Name Microsoft.Graph.Authentication)) {
        throw "The Microsoft.Graph.Authentication module is required. Install it with: Install-Module Microsoft.Graph.Authentication -Scope CurrentUser"
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop
}

function Resolve-DelegatedScopeIds {
    <#
        Resolves permission names to ids by asking the Graph service principal, rather than
        hardcoding GUIDs. Hardcoded ids are correct until they are not, and a wrong one
        produces a registration that looks right and cannot read a calendar.
    #>
    param([string[]]$Names)

    $servicePrincipal = Invoke-MgGraphRequest -Method GET `
        -Uri "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId eq '$graphAppId'&`$select=id,oauth2PermissionScopes"

    $available = $servicePrincipal.value[0].oauth2PermissionScopes

    if (-not $available) {
        throw 'Could not read the delegated permissions from the Microsoft Graph service principal.'
    }

    $resolved = foreach ($name in $Names) {
        $match = $available | Where-Object { $_.value -eq $name }

        if (-not $match) {
            throw "Microsoft Graph does not expose a delegated permission called '$name'."
        }

        [pscustomobject]@{ Name = $name; Id = $match.id }
    }

    $resolved
}

Assert-GraphModule

Write-Host 'Signing in. Use the account that should own the registration.' -ForegroundColor Cyan
Connect-MgGraph -Scopes 'Application.ReadWrite.All' -NoWelcome

$context = Get-MgContext
$organization = Invoke-MgGraphRequest -Method GET -Uri 'https://graph.microsoft.com/v1.0/organization?$select=id,displayName'
$tenantName = $organization.value[0].displayName

Write-Host ''
Write-Host 'About to create the registration in:' -ForegroundColor Yellow
Write-Host "  Tenant : $tenantName"
Write-Host "  Id     : $($context.TenantId)"
Write-Host "  As     : $($context.Account)"
Write-Host ''
Write-Host 'This tenant decides who administers the registration. It does NOT limit who can' -ForegroundColor DarkGray
Write-Host 'sign in: work, school and personal Microsoft accounts can all use it.' -ForegroundColor DarkGray
Write-Host ''

if (-not $PSCmdlet.ShouldProcess("$DisplayName in $tenantName", 'Create app registration')) {
    return
}

$confirm = Read-Host 'Create the registration in this tenant? (y/N)'

if ($confirm -notmatch '^(y|yes)$') {
    Write-Host 'Nothing was created.' -ForegroundColor Yellow
    return
}

# Idempotent: re-running should report the existing registration rather than making a second.
$escapedName = $DisplayName.Replace("'", "''")
$existing = Invoke-MgGraphRequest -Method GET `
    -Uri "https://graph.microsoft.com/v1.0/applications?`$filter=displayName eq '$escapedName'&`$select=id,appId,displayName"

if ($existing.value.Count -gt 0) {
    Write-Host ''
    Write-Host "An application called '$DisplayName' already exists here." -ForegroundColor Yellow
    Write-Host "  Client id : $($existing.value[0].appId)"
    return
}

$scopeIds = Resolve-DelegatedScopeIds -Names $requestedScopes

$body = @{
    displayName    = $DisplayName

    # Work or school accounts from ANY tenant, plus personal Microsoft accounts. This single
    # setting is what makes one registration serve everybody.
    signInAudience = 'AzureADandPersonalMicrosoftAccount'

    # No secret, no certificate. A desktop application cannot keep one, and pretending
    # otherwise is how secrets end up in public repositories.
    isFallbackPublicClient = $true

    publicClient = @{
        # Loopback on a free port. MSAL listens here for the response from the system
        # browser. Not a real server, and nothing is exposed outside the machine.
        redirectUris = @($RedirectUri)
    }

    requiredResourceAccess = @(
        @{
            resourceAppId  = $graphAppId
            resourceAccess = @($scopeIds | ForEach-Object {
                @{ id = $_.Id; type = 'Scope' }   # Scope means delegated, not application
            })
        }
    )

    info = @{
        supportUrl = 'https://github.com/levinium/Printendar'
    }
} | ConvertTo-Json -Depth 10

$created = Invoke-MgGraphRequest -Method POST `
    -Uri 'https://graph.microsoft.com/v1.0/applications' `
    -Body $body `
    -ContentType 'application/json'

Write-Host ''
Write-Host 'Created.' -ForegroundColor Green
Write-Host ''
Write-Host "  Application : $($created.displayName)"
Write-Host "  Client id   : $($created.appId)" -ForegroundColor Cyan
Write-Host "  Object id   : $($created.id)"
Write-Host "  Audience    : $($created.signInAudience)"
Write-Host "  Permissions : $($requestedScopes -join ', ') (delegated, read only)"
Write-Host ''
Write-Host 'Next:' -ForegroundColor Yellow
Write-Host "  1. Put the client id in Microsoft365Options.cs, replacing UnconfiguredClientId."
Write-Host "  2. Or, to try it without editing code:"
Write-Host "       `$env:PRINTENDAR_MS_CLIENT_ID = '$($created.appId)'"
Write-Host ''
Write-Host 'If an organisation has disabled user consent, an administrator there consents once at:'
Write-Host "  https://login.microsoftonline.com/common/adminconsent?client_id=$($created.appId)" -ForegroundColor DarkGray
Write-Host ''
