// Azure deployment for the MediFlow reference architecture.
// Deploy: az deployment group create -g <rg> -f infra/main.bicep -p @infra/main.parameters.json
//
// Components: Container Apps environment (4 apps), Azure SQL (server + database),
// Log Analytics workspace + Application Insights, and a Key Vault holding the
// API keys. Connection strings are wired through Container Apps secrets.
@description('Suffix for unique resource names')
param nameSuffix string
@description('SQL administrator login')
param sqlAdminLogin string
@description('SQL administrator password (from a secret in real deployments)')
@secure()
param sqlAdminPassword string
@description('Container registry image prefix, e.g. ghcr.io/alex5350/mediflow')
param imagePrefix string
@description('Container image tag')
param imageTag string = 'latest'
@description('Comma-separated API keys accepted by the APIs')
@secure()
param apiKeys string

resource location_scope 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: resourceGroup().name
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'mediflow-logs-${nameSuffix}'
  location: resourceGroup().location
  sku: { name: 'PerGB2018' }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'mediflow-appinsights-${nameSuffix}'
  location: resourceGroup().location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'mediflow-sql-${nameSuffix}'
  location: resourceGroup().location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }

  resource database 'databases@2023-08-01-preview' = {
    name: 'MediFlow'
    location: resourceGroup().location
    sku: { name: 'Standard', tier: 'Standard', capacity: 50 }
  }

  resource firewall 'firewallRules@2023-08-01-preview' = {
    name: 'AllowAzureServices'
    properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'mediflow-vault-${nameSuffix}'
  location: resourceGroup().location
  sku: { family: 'A', name: 'standard' }
  properties: {
    enableRbacAuthorization: true
    tenantId: subscription().tenantId
  }
}

resource apps 'Microsoft.App/managedEnvironments@2023-11-02-preview' = {
  name: 'mediflow-env-${nameSuffix}'
  location: resourceGroup().location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: listsecrets(logs.id, logs.apiVersion[0]).primarySharedKey }
    }
  }
}

var sqlConnectionString = 'Server=${sql.properties.fullyQualifiedDomainName};Database=MediFlow;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False'

resource api 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'api'
  location: resourceGroup().location
  properties: {
    environmentId: apps.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        { name: 'connection-string', value: sqlConnectionString }
        { name: 'api-keys', value: apiKeys }
      ]
      ingress: { external: true, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${imagePrefix}/MediFlow.Api:${imageTag}'
          env: [
            { name: 'ConnectionStrings__MediFlowDb', secretRef: 'connection-string' }
            { name: 'Api__Keys', secretRef: 'api-keys' }
            { name: 'Seed__Enabled', value: 'false' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource claimsApi 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'claims-api'
  location: resourceGroup().location
  properties: {
    environmentId: apps.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        { name: 'connection-string', value: sqlConnectionString }
        { name: 'api-keys', value: apiKeys }
      ]
      ingress: { external: true, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'claims-api'
          image: '${imagePrefix}/MediFlow.Claims.Api:${imageTag}'
          env: [
            { name: 'ConnectionStrings__MediFlowDb', secretRef: 'connection-string' }
            { name: 'Api__Keys', secretRef: 'api-keys' }
            { name: 'Database__InitializeOnStartup', value: 'false' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'worker'
  location: resourceGroup().location
  properties: {
    environmentId: apps.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        { name: 'connection-string', value: sqlConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${imagePrefix}/MediFlow.Worker:${imageTag}'
          env: [
            { name: 'ConnectionStrings__MediFlowDb', secretRef: 'connection-string' }
            { name: 'Database__InitializeOnStartup', value: 'false' }
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: 'http://localhost:4318' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }   // safe: leasing is atomic in SQL
    }
  }
}

resource blazor 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: 'blazor'
  location: resourceGroup().location
  properties: {
    environmentId: apps.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        { name: 'api-key', value: apiKeys }
      ]
      ingress: { external: true, targetPort: 8080 }
    }
    template: {
      containers: [
        {
          name: 'blazor'
          image: '${imagePrefix}/MediFlow.Blazor:${imageTag}'
          env: [
            { name: 'Api__EnrollmentBaseUrl', value: 'https://${api.properties.configuration.ingress.fqdn}' }
            { name: 'Api__ClaimsBaseUrl', value: 'https://${claimsApi.properties.configuration.ingress.fqdn}' }
            { name: 'Api__Key', secretRef: 'api-key' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

output dashboardUrl string = 'https://${blazor.properties.configuration.ingress.fqdn}'
output enrollmentApiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output claimsApiUrl string = 'https://${claimsApi.properties.configuration.ingress.fqdn}'
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output keyVaultName string = vault.name
