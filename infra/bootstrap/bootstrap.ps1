#Requires -Version 7.3
<#
One-time administrator setup for the GISR production deployment. Safe to re-run.

Creates: the Api and Web managed identities, the Api and Web Entra app registrations (access_as_user scope,
Officer app role, Graph User.ReadBasic.All, managed identity federated credentials, admin consent), and the
GitHub deployer identity (OIDC, like trip-planner-github-oidc). Then publishes the IDs the deploy workflow needs
as variables on the GitHub environment.

Run after `az login` (and `gh auth login`) as an account that can create app registrations, grant admin consent,
assign subscription roles, and administer PostgreSQL authentication on the shared server.
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId = 'fdadb01b-83a6-4002-9eca-5ff098cdd3bd',
    [string]$Location = 'northcentralus',
    [string]$AppsResourceGroup = 'rg-apps',
    [string]$PlatformResourceGroup = 'rg-platform',
    [string]$ContainerAppsEnvironment = 'cae-shared',
    [string]$PostgresServer = 'accpsqlshared',
    [string]$RegistryName = 'acccrshared',
    [string]$Repository = 'TK11703/holonet-gisr',
    [string]$GitHubEnvironment = 'production',
    [string]$EnvironmentName = 'prod',
    [string]$DeployerName = 'gisr-github-oidc'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$graphAppId = '00000003-0000-0000-c000-000000000000'
$userReadBasicAllScopeId = 'b340eb25-3456-403f-be2f-af7a0d370277'
$userReadScopeId = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
$apiScopeName = 'access_as_user'

function Invoke-Graph {
    param([string]$Method, [string]$Path, $Body)
    $azArgs = @('rest', '--method', $Method, '--url', "https://graph.microsoft.com/v1.0$Path", '--headers', 'Content-Type=application/json')
    $file = $null
    if ($null -ne $Body) {
        $file = New-TemporaryFile
        $Body | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $file -Encoding utf8
        $azArgs += @('--body', "@$($file.FullName)")
    }
    try { $out = az @azArgs } finally { if ($file) { Remove-Item -LiteralPath $file } }
    if ($out) { $out | ConvertFrom-Json }
}

function Find-Graph([string]$Collection, [string]$Filter) {
    $result = Invoke-Graph GET ("/$Collection`?`$filter=" + [uri]::EscapeDataString($Filter))
    @($result.value) | Select-Object -First 1
}

function Set-App([string]$DisplayName, [hashtable]$Body) {
    $app = Find-Graph 'applications' "displayName eq '$DisplayName'"
    if (-not $app) {
        $app = Invoke-Graph POST '/applications' @{ displayName = $DisplayName; signInAudience = 'AzureADMyOrg' }
    }
    if ($Body.Count -gt 0) {
        Invoke-Graph PATCH "/applications/$($app.id)" $Body | Out-Null
    }
    Invoke-Graph GET "/applications/$($app.id)"
}

function Set-ServicePrincipal([string]$AppId) {
    $sp = Find-Graph 'servicePrincipals' "appId eq '$AppId'"
    if (-not $sp) { $sp = Invoke-Graph POST '/servicePrincipals' @{ appId = $AppId } }
    $sp
}

function Set-FederatedCredential($App, [string]$Name, [string]$Issuer, [string]$Subject) {
    $existing = (Invoke-Graph GET "/applications/$($App.id)/federatedIdentityCredentials").value | Where-Object name -eq $Name
    $body = @{ issuer = $Issuer; subject = $Subject; audiences = @('api://AzureADTokenExchange') }
    if ($existing) {
        Invoke-Graph PATCH "/applications/$($App.id)/federatedIdentityCredentials/$($existing.id)" $body | Out-Null
    }
    else {
        $body.name = $Name
        Invoke-Graph POST "/applications/$($App.id)/federatedIdentityCredentials" $body | Out-Null
    }
}

function Grant-AdminConsent([string]$ClientSpId, [string]$ResourceSpId, [string]$Scope) {
    $filter = "clientId eq '$ClientSpId' and resourceId eq '$ResourceSpId' and consentType eq 'AllPrincipals'"
    $grant = Find-Graph 'oauth2PermissionGrants' $filter
    if ($grant) {
        Invoke-Graph PATCH "/oauth2PermissionGrants/$($grant.id)" @{ scope = $Scope } | Out-Null
    }
    else {
        Invoke-Graph POST '/oauth2PermissionGrants' @{ clientId = $ClientSpId; consentType = 'AllPrincipals'; resourceId = $ResourceSpId; scope = $Scope } | Out-Null
    }
}

az account set --subscription $SubscriptionId
$tenantId = az account show --query tenantId -o tsv
$subscriptionScope = "/subscriptions/$SubscriptionId"

Write-Host '[1/7] Managed identities'
$apiIdentityName = "id-gisr-api-$EnvironmentName"
$webIdentityName = "id-gisr-web-$EnvironmentName"
$apiIdentity = az identity create -g $AppsResourceGroup -n $apiIdentityName -l $Location --query '{clientId:clientId,principalId:principalId}' -o json | ConvertFrom-Json
$webIdentity = az identity create -g $AppsResourceGroup -n $webIdentityName -l $Location --query '{clientId:clientId,principalId:principalId}' -o json | ConvertFrom-Json

$domain = az resource show -g $AppsResourceGroup -n $ContainerAppsEnvironment --resource-type Microsoft.App/managedEnvironments --api-version 2024-03-01 --query properties.defaultDomain -o tsv
if (-not $domain) { throw "Could not read the default domain of $ContainerAppsEnvironment." }
$webUrl = "https://ca-gisr-web-$EnvironmentName.$domain"
Write-Host "  ok: $apiIdentityName, $webIdentityName; Web URL $webUrl"

Write-Host '[2/7] Api app registration'
$existingApi = Find-Graph 'applications' "displayName eq 'HoloNet GISR API'"
$scopeId = if ($existingApi) { ($existingApi.api.oauth2PermissionScopes | Where-Object value -eq $apiScopeName).id } else { $null }
if (-not $scopeId) { $scopeId = [guid]::NewGuid().ToString() }
$roleId = if ($existingApi) { ($existingApi.appRoles | Where-Object value -eq 'Officer').id } else { $null }
if (-not $roleId) { $roleId = [guid]::NewGuid().ToString() }

$api = Set-App 'HoloNet GISR API' @{
    api                    = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes      = @(@{
                id                      = $scopeId
                value                   = $apiScopeName
                type                    = 'User'
                isEnabled               = $true
                adminConsentDisplayName = 'Access GISR as the signed-in user'
                adminConsentDescription = 'Allows the app to call the GISR API on behalf of the signed-in user.'
                userConsentDisplayName  = 'Access GISR as you'
                userConsentDescription  = 'Allows the app to call the GISR API on your behalf.'
            })
    }
    appRoles               = @(@{
            id                 = $roleId
            allowedMemberTypes = @('User')
            displayName        = 'Officer'
            description        = 'Officers write service records about others, seal records, and search all records.'
            value              = 'Officer'
            isEnabled          = $true
        })
    requiredResourceAccess = @(@{ resourceAppId = $graphAppId; resourceAccess = @(@{ id = $userReadBasicAllScopeId; type = 'Scope' }, @{ id = $userReadScopeId; type = 'Scope' }) })
}
Invoke-Graph PATCH "/applications/$($api.id)" @{ identifierUris = @("api://$($api.appId)") } | Out-Null
Write-Host "  ok: client ID $($api.appId)"

Write-Host '[3/7] Web app registration'
$web = Set-App 'HoloNet GISR Web' @{
    web                    = @{
        redirectUris          = @("$webUrl/signin-oidc")
        logoutUrl             = "$webUrl/signout-callback-oidc"
        implicitGrantSettings = @{ enableIdTokenIssuance = $true }
    }
    requiredResourceAccess = @(@{ resourceAppId = $api.appId; resourceAccess = @(@{ id = $scopeId; type = 'Scope' }) })
}

Write-Host "  ok: client ID $($web.appId)"

Write-Host '[4/7] Service principals, consent, federated credentials'
$apiSp = Set-ServicePrincipal $api.appId
$webSp = Set-ServicePrincipal $web.appId
$graphSp = Find-Graph 'servicePrincipals' "appId eq '$graphAppId'"
Grant-AdminConsent $apiSp.id $graphSp.id 'User.ReadBasic.All User.Read'
Grant-AdminConsent $webSp.id $apiSp.id $apiScopeName

$entraIssuer = "https://login.microsoftonline.com/$tenantId/v2.0"
Set-FederatedCredential $api 'managed-identity' $entraIssuer $apiIdentity.principalId
Set-FederatedCredential $web 'managed-identity' $entraIssuer $webIdentity.principalId

Write-Host '  ok: consent granted, managed identities trusted'

Write-Host '[5/7] GitHub deployer identity'
$deployer = Set-App $DeployerName @{}
$deployerSp = Set-ServicePrincipal $deployer.appId
$githubIssuer = 'https://token.actions.githubusercontent.com'
# Repos with immutable subjects present repo:OWNER@ownerId/REPO@repoId, not repo:OWNER/REPO.
$subjectPrefix = (gh api "repos/$Repository/actions/oidc/customization/sub" | ConvertFrom-Json).sub_claim_prefix
if (-not $subjectPrefix) { $subjectPrefix = "repo:$Repository" }
Set-FederatedCredential $deployer "gh-env-$GitHubEnvironment" $githubIssuer "${subjectPrefix}:environment:$GitHubEnvironment"
Set-FederatedCredential $deployer 'gh-main-branch' $githubIssuer "${subjectPrefix}:ref:refs/heads/main"

$registryId = az acr show -n $RegistryName -g $PlatformResourceGroup --query id -o tsv
$assignments = @(
    @{ Role = 'Contributor'; Scope = $subscriptionScope }
    @{ Role = 'User Access Administrator'; Scope = $subscriptionScope }
    @{ Role = 'AcrPush'; Scope = $registryId }
)
foreach ($assignment in $assignments) {
    $existing = az role assignment list --assignee $deployerSp.id --role $assignment.Role --scope $assignment.Scope --query '[0].id' -o tsv
    if (-not $existing) {
        az role assignment create --assignee-object-id $deployerSp.id --assignee-principal-type ServicePrincipal --role $assignment.Role --scope $assignment.Scope | Out-Null
    }
}

Write-Host "  ok: $DeployerName has Contributor, User Access Administrator, and AcrPush"

Write-Host '[6/7] PostgreSQL Entra administrator'
$admins = az postgres flexible-server microsoft-entra-admin list -g $PlatformResourceGroup -s $PostgresServer --query "[?objectId=='$($deployerSp.id)'].objectId" -o tsv
if (-not $admins) {
    az postgres flexible-server microsoft-entra-admin create -g $PlatformResourceGroup -s $PostgresServer `
        --display-name $DeployerName --object-id $deployerSp.id --type ServicePrincipal | Out-Null
}

Write-Host "  ok: $DeployerName is an administrator on $PostgresServer"

Write-Host "[7/7] GitHub environment '$GitHubEnvironment'"
gh api --method PUT "repos/$Repository/environments/$GitHubEnvironment" | Out-Null
$variables = @{
    AZURE_CLIENT_ID       = $deployer.appId
    AZURE_TENANT_ID       = $tenantId
    AZURE_SUBSCRIPTION_ID = $SubscriptionId
    GISR_API_CLIENT_ID    = $api.appId
    GISR_WEB_CLIENT_ID    = $web.appId
}
foreach ($name in $variables.Keys) {
    gh variable set $name --env $GitHubEnvironment --repo $Repository --body $variables[$name]
}

Write-Host "  ok: $($variables.Count) variables set"

Write-Host ''
Write-Host 'Done. Remaining manual steps:'
Write-Host "  1. Assign the Officer app role to users/groups: Entra > Enterprise applications > HoloNet GISR API > Users and groups."
Write-Host "  2. Add required reviewers to the '$GitHubEnvironment' environment: https://github.com/$Repository/settings/environments"
