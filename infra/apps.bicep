// GISR container apps in rg-apps. Runs after platform.bicep and infra/scripts/configure-database.sh.
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environmentName string = 'prod'
param containerAppsEnvironmentName string = 'cae-shared'
param apiIdentityName string = 'id-gisr-api-prod'
param webIdentityName string = 'id-gisr-web-prod'

@description('Resource group that holds the registry, PostgreSQL server, storage account, and Foundry account.')
param platformResourceGroupName string = 'rg-platform'
param registryName string = 'acccrshared'
param postgresServerName string = 'accpsqlshared'
param databaseName string = 'Gisr'
param storageAccountName string = 'strgisr'
param foundryAccountName string = 'aif-shared-acc'
param embeddingDeployment string = 'text-embedding-3-large'
param chatDeployment string = 'gpt-4.1-mini'

@description('Image tag shared by both images, e.g. 20261008115247.')
param imageTag string

param tenantId string
param apiClientId string
param webClientId string

param apiScopeName string = 'access_as_user'
param cpu string = '0.5'
param memory string = '1Gi'
param apiMaxReplicas int = 2

// Web is Blazor Server with an in-memory token cache and view-grant store, so it must stay on one replica.
param webMaxReplicas int = 1

var tags = {
  app: 'gisr'
  environment: environmentName
}

var apiAppName = 'ca-gisr-api-${environmentName}'
var webAppName = 'ca-gisr-web-${environmentName}'

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: apiIdentityName
}

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: webIdentityName
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(platformResourceGroupName)
}

var forwardedHeaders = {
  name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
  value: 'true'
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registry.properties.loginServer}/gisr-api:${imageTag}'
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            forwardedHeaders
            // Selects the user-assigned identity for DefaultAzureCredential (PostgreSQL, Blob, Foundry).
            { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
            { name: 'ConnectionStrings__Gisr', value: 'Host=${postgresServerName}.postgres.database.azure.com;Database=${databaseName};Username=${apiIdentityName};SSL Mode=Require;Maximum Pool Size=20' }
            { name: 'Database__UseEntraAuthentication', value: 'true' }
            { name: 'Storage__BlobEndpoint', value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}' }
            { name: 'Search__OpenAiEndpoint', value: 'https://${foundryAccountName}.openai.azure.com/' }
            { name: 'Search__EmbeddingDeployment', value: embeddingDeployment }
            { name: 'SummaryEnhancement__OpenAiEndpoint', value: 'https://${foundryAccountName}.openai.azure.com/' }
            { name: 'SummaryEnhancement__ChatDeployment', value: chatDeployment }
            { name: 'Search__OcrFallback__Endpoint', value: 'https://${foundryAccountName}.services.ai.azure.com/' }
            { name: 'AzureAd__TenantId', value: tenantId }
            { name: 'AzureAd__ClientId', value: apiClientId }
            { name: 'AzureAd__ClientCredentials__0__SourceType', value: 'SignedAssertionFromManagedIdentity' }
            { name: 'AzureAd__ClientCredentials__0__ManagedIdentityClientId', value: apiIdentity.properties.clientId }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: apiMaxReplicas
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        stickySessions: {
          affinity: 'sticky'
        }
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: webIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registry.properties.loginServer}/gisr-web:${imageTag}'
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            forwardedHeaders
            { name: 'Api__BaseUrl', value: 'https://${api.properties.configuration.ingress.fqdn}' }
            { name: 'Api__Scope', value: 'api://${apiClientId}/${apiScopeName}' }
            { name: 'AzureAd__TenantId', value: tenantId }
            { name: 'AzureAd__ClientId', value: webClientId }
            { name: 'AzureAd__ClientCredentials__0__SourceType', value: 'SignedAssertionFromManagedIdentity' }
            { name: 'AzureAd__ClientCredentials__0__ManagedIdentityClientId', value: webIdentity.properties.clientId }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: webMaxReplicas
      }
    }
  }
}

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
