targetScope = 'resourceGroup'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Base name used to derive resource names.')
param namePrefix string = 'needly-prod-001'

@description('Object id of the Microsoft Entra principal that administers the SQL logical server.')
param sqlAdminObjectId string

@description('Display name of the Microsoft Entra SQL administrator.')
param sqlAdminLogin string

@description('GitHub environment that gates the deployment workflow.')
param gitHubEnvironment string = 'production'

@description('GitHub Actions OIDC subject allowed to deploy to this environment.')
param gitHubOidcSubject string = 'repo:kasuken@2757486/Needly@1355126176:environment:production'

@description('Enables the GitHub App integration. Secrets are supplied separately as app settings.')
param gitHubIntegrationEnabled bool = false

var sqlServerName = '${namePrefix}-server'
var sqlDatabaseName = '${namePrefix}-database'
var planName = 'needly-prodplan-linux'
var deployIdentityName = 'id-needly-github-deploy'

// Website Contributor and SQL Server Contributor: the least privilege the release workflow needs.
var websiteContributorRoleId = 'de139f84-1756-47ae-9be6-808fbbe84772'
var sqlServerContributorRoleId = '6d8ee4ec-f05a-4a1d-8b00-a9b17e38b437'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-law'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: namePrefix
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    maxSizeBytes: 2147483648
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// 0.0.0.0 is the sentinel rule that allows access from Azure services, including this App Service.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: namePrefix
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      webSocketsEnabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/live'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
        {
          name: 'GitHubApp__Enabled'
          value: string(gitHubIntegrationEnabled)
        }
      ]
      connectionStrings: [
        {
          name: 'Needly'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;'
        }
      ]
    }
  }
}

resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deployIdentityName
  location: location
}

resource deployFederation 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deployIdentity
  name: 'github-release-${gitHubEnvironment}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: gitHubOidcSubject
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource siteDeployRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: site
  name: guid(site.id, deployIdentity.id, websiteContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributorRoleId)
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource sqlDeployRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sqlServer
  name: guid(sqlServer.id, deployIdentity.id, sqlServerContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sqlServerContributorRoleId)
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = site.name
output webAppUrl string = 'https://${site.properties.defaultHostName}'
output webAppPrincipalId string = site.identity.principalId
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output deployIdentityClientId string = deployIdentity.properties.clientId
output tenantId string = subscription().tenantId
output subscriptionId string = subscription().subscriptionId
