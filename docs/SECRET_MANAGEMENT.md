# Secret Management & Vault Integration

## Overview

This document outlines the secret management strategy and vault integration for Silo's identity and access system.

## Current Implementation

### Configuration-Based Secrets

Currently, secrets are managed through configuration:

**appsettings.json (Development):**
```json
{
  "Authentication": {
    "JwtSecretKey": "development-key-should-be-replaced-in-production",
    "JwtIssuer": "Silo.Api",
    "JwtAudience": "Silo.Client"
  },
  "ConnectionStrings": {
    "Database": "Host=localhost;Port=5432;Database=silo_dev;Username=dev;Password=devpassword"
  }
}
```

**Environment Variables (Production):**
```bash
export Authentication__JwtSecretKey="your-production-secret"
export ConnectionStrings__Database="your-production-connection-string"
```

## Vault Integration (Recommended for Production)

### Supported Vault Providers

#### 1. Azure Key Vault

**Installation:**
```bash
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
dotnet add package Azure.Identity
```

**Integration in Program.cs:**
```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

**Secret Naming Convention:**
- `Authentication--JwtSecretKey`
- `ConnectionStrings--Database`
- `MinIO--SecretKey`

#### 2. HashiCorp Vault

**Installation:**
```bash
dotnet add package VaultSharp
```

**Integration:**
```csharp
var vaultClient = new VaultClient(new VaultClientSettings(
    vaultServerUriWithPort,
    authMethodInfo));

var secrets = await vaultClient.V1.Secrets.KeyValue.V2
    .ReadSecretAsync(path: "silo/config");

builder.Configuration.AddInMemoryCollection(secrets.Data.Data);
```

#### 3. AWS Secrets Manager

**Installation:**
```bash
dotnet add package AWSSDK.SecretsManager
dotnet add package Amazon.Extensions.Configuration.SystemsManager
```

**Integration:**
```csharp
builder.Configuration.AddSecretsManager(
    configurator: options =>
    {
        options.SecretFilter = entry => entry.Name.StartsWith("silo/");
        options.KeyGenerator = (entry, key) =>
            key.Replace("__", ":");
    });
```

## Secret Rotation

### Automatic Rotation Strategy

#### JWT Secret Key Rotation

**Process:**
1. Generate new secret key
2. Keep old key valid for grace period (24-48 hours)
3. Issue new tokens with new key
4. Allow old tokens to expire naturally
5. Remove old key after grace period

**Implementation:**
```csharp
public class JwtSecretRotationService : IHostedService
{
    private readonly IConfiguration _config;
    private readonly IVaultService _vault;
    
    public async Task RotateSecretAsync()
    {
        var newSecret = GenerateSecureSecret();
        await _vault.StoreSecretAsync("jwt-secret-new", newSecret);
        
        // Wait for grace period
        await Task.Delay(TimeSpan.FromHours(48));
        
        // Promote new to current
        await _vault.StoreSecretAsync("jwt-secret", newSecret);
        await _vault.DeleteSecretAsync("jwt-secret-old");
    }
    
    private string GenerateSecureSecret()
    {
        var key = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        return Convert.ToBase64String(key);
    }
}
```

#### Database Password Rotation

**Process:**
1. Create new database user with same permissions
2. Update connection string to use new user
3. Verify connectivity
4. Revoke old user credentials

#### API Key Rotation

API keys can be rotated by:
1. User creates new key
2. Updates applications to use new key
3. Monitors old key usage
4. Revokes old key when no longer used

**Auto-expiration:**
- Set `ExpiresAt` when creating API keys
- Background job to notify owners before expiration
- Automatic revocation on expiration

## Secret Storage Best Practices

### Never Commit Secrets

**.gitignore:**
```
appsettings.Production.json
appsettings.*.json
!appsettings.json
!appsettings.Development.json
.env
.env.*
*.key
*.pem
```

### Encryption at Rest

All secrets in the database are hashed or encrypted:
- **Password hashes:** BCrypt (one-way)
- **API key hashes:** BCrypt (one-way)
- **Refresh tokens:** Stored as-is (random, single-use)

### Environment-Specific Configuration

**Development:**
- Use user secrets: `dotnet user-secrets set "Authentication:JwtSecretKey" "dev-key"`

**Staging/Production:**
- Use vault integration
- Or secure environment variables

### Secret Access Control

**Principle of Least Privilege:**
- Application identity has read-only access
- Rotation service has write access
- Manual secrets require admin approval

## Monitoring & Auditing

### Secret Usage Tracking

Log all secret access:
```csharp
_logger.LogInformation("Secret {SecretName} accessed by {ServiceName}", 
    secretName, serviceName);
```

### Failed Authentication Monitoring

Alert on patterns:
- Multiple failed logins for same user
- Failed API key usage
- Unusual access patterns

### Audit Log Requirements

Track:
- Secret creation/rotation timestamp
- Who rotated the secret
- Services using each secret
- Failed authentication attempts

## Implementation Checklist

- [ ] Choose vault provider (Azure KV, HashiCorp, AWS)
- [ ] Configure vault connection
- [ ] Migrate secrets from configuration to vault
- [ ] Implement secret rotation schedule
- [ ] Create monitoring/alerting for secret access
- [ ] Document rotation procedures
- [ ] Test secret rotation in staging
- [ ] Create runbook for secret compromise
- [ ] Set up automated rotation jobs

## Emergency Procedures

### Secret Compromise

If a secret is compromised:

1. **Immediately rotate the secret:**
   ```bash
   # Generate new secret
   NEW_SECRET=$(openssl rand -base64 64)
   
   # Update vault
   vault kv put secret/silo/jwt-secret value=$NEW_SECRET
   
   # Restart application
   kubectl rollout restart deployment/silo-api
   ```

2. **Revoke all existing sessions:**
   ```sql
   UPDATE user_sessions SET revoked_at = NOW();
   ```

3. **Force all users to re-authenticate**

4. **Audit access logs for unauthorized usage**

5. **Notify security team**

## Future Enhancements

- Implement automatic key rotation (quarterly)
- Add support for hardware security modules (HSM)
- Implement secret versioning
- Add secret usage analytics dashboard
- Support for temporary/short-lived credentials
