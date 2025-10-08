# Security & Authentication Documentation

## Overview

Silo implements comprehensive identity and access hardening with:
- Database-backed authentication using Entity Framework Core
- JWT token-based authentication with refresh token rotation
- API key authentication for service-to-service communication
- Role-Based Access Control (RBAC) with permission-based policies
- Global rate limiting to prevent abuse
- Multi-tenant support with tenant isolation

## Authentication Methods

### 1. JWT Token Authentication

**Signup:**
```http
POST /api/auth/signup
Content-Type: application/json

{
  "username": "user@example.com",
  "email": "user@example.com",
  "password": "SecurePassword123!",
  "firstName": "John",
  "lastName": "Doe"
}
```

**Login:**
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "user@example.com",
  "password": "SecurePassword123!",
  "rememberMe": false
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "base64-encoded-refresh-token",
  "expiresAt": "2024-01-01T12:00:00Z",
  "user": {
    "id": "guid",
    "username": "user@example.com",
    "email": "user@example.com",
    "roles": ["User"],
    "permissions": ["files:read", "files:write", "files:upload", "files:download"]
  }
}
```

**Refresh Token:**
```http
POST /api/auth/refresh
Content-Type: application/json

{
  "refreshToken": "your-refresh-token"
}
```

### 2. API Key Authentication

API keys are tenant-specific and can be created for service-to-service authentication.

**Using API Key:**
```http
GET /api/files/search?query=test
X-API-Key: your-api-key-here
```

## Authorization & Permissions

### Permission-Based Access Control

**File Permissions:**
- `files:read` - Read file metadata and search
- `files:write` - Modify file metadata
- `files:delete` - Delete files
- `files:upload` - Upload new files
- `files:download` - Download file content

**User Management Permissions:**
- `users:read` - View user information
- `users:write` - Create/update users
- `users:manage` - Full user management

**System Permissions:**
- `system:admin` - Full system administration
- `system:backup` - Manage backups
- `system:monitor` - View system metrics

### Default Roles

**Administrator:** All permissions, full system access

**FileManager:** All file operation permissions

**User:** Basic file read/write permissions

## Security Features

### Password Security

- **Hashing:** BCrypt with automatic salt generation
- **Minimum Length:** 8 characters
- **Failed Login Protection:** Account lockout after 5 failed attempts for 30 minutes

### Token Security

**Access Tokens:**
- Expiration: 15 minutes (configurable)
- Signed with HMAC-SHA256

**Refresh Tokens:**
- Expiration: 30 days (configurable)
- Automatically rotated on use
- Can be revoked

### Rate Limiting

**Default Limits:**
- 60 requests per minute per IP
- 1000 requests per hour per IP

**Endpoint-Specific Limits:**
- `/api/files/upload`: 10 requests per minute
- `/api/auth/login`: 5 requests per minute
- `/api/auth/signup`: 3 requests per hour

### Multi-Tenant Isolation

- Each user belongs to a tenant
- Tenant ID included in JWT claims
- Resources scoped to tenant
- Quota enforcement per tenant

## Configuration

### Authentication Settings

```json
{
  "Authentication": {
    "JwtSecretKey": "your-secret-key-min-32-chars",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 30,
    "MaxFailedLoginAttempts": 5,
    "LockoutDurationMinutes": 30
  }
}
```

## Database Schema

### Core Tables

- **Users:** Identity, tenant association, security tracking
- **Roles:** Role definitions
- **Permissions:** Granular permissions
- **UserRoles:** Many-to-many relationship
- **RolePermissions:** Permission assignments
- **UserSessions:** Refresh token storage
- **Tenants:** Multi-tenant organization data
- **TenantApiKeys:** API key management

## Security Best Practices

1. Never commit secrets to source control
2. Use environment variables for production
3. Rotate JWT secret keys periodically
4. Enforce HTTPS in production
5. Monitor authentication attempts
6. Review user permissions regularly

## Migration to Production

1. Generate strong JWT secret: `openssl rand -base64 64`
2. Configure environment variables
3. Run migrations: `dotnet ef database update`
4. Enable HTTPS
5. Configure production rate limits
