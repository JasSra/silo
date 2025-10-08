# Silo Documentation

This directory contains comprehensive documentation for the Silo file management system.

## Security & Authentication

### [SECURITY.md](./SECURITY.md)
Complete guide to Silo's security features:
- Authentication methods (JWT tokens, API keys)
- Authorization and permissions (RBAC)
- Security features (password hashing, rate limiting, multi-tenancy)
- Configuration and best practices

### [SECRET_MANAGEMENT.md](./SECRET_MANAGEMENT.md)
Secret management and vault integration:
- Current implementation
- Vault provider integration (Azure Key Vault, HashiCorp Vault, AWS Secrets Manager)
- Secret rotation strategies
- Emergency procedures

### [SECURITY_TEST_CHECKLIST.md](./SECURITY_TEST_CHECKLIST.md)
Comprehensive security testing checklist covering:
- Authentication and authorization tests
- API key authentication tests
- Rate limiting tests
- Multi-tenant security tests
- Vulnerability testing
- Production readiness checks

## Quick Start

1. **Authentication Setup:**
   ```bash
   # Set JWT secret
   dotnet user-secrets set "Authentication:JwtSecretKey" "your-development-key"
   
   # Run migrations
   dotnet ef database update --project src/Silo.Api
   ```

2. **Create First User:**
   ```bash
   curl -X POST http://localhost:5000/api/auth/signup \
     -H "Content-Type: application/json" \
     -d '{
       "username": "admin@example.com",
       "email": "admin@example.com",
       "password": "SecurePassword123!"
     }'
   ```

3. **Login:**
   ```bash
   curl -X POST http://localhost:5000/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{
       "username": "admin@example.com",
       "password": "SecurePassword123!"
     }'
   ```

4. **Use Access Token:**
   ```bash
   curl -X GET http://localhost:5000/api/files/search?query=test \
     -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
   ```

## Phase 1 - Identity & Access Hardening (Completed)

✅ Database-backed authentication with EF Core  
✅ JWT token-based authentication with refresh token rotation  
✅ API key authentication for service-to-service communication  
✅ BCrypt password hashing  
✅ Role-Based Access Control (RBAC) with permission-based policies  
✅ Global rate limiting to prevent abuse  
✅ Multi-tenant support with tenant isolation  
✅ Account lockout after failed login attempts  
✅ Security documentation and testing checklists  

## Next Steps

### Phase 2 - Multi-Tenant Data Layer
- Add tenant scopes to all storage models/services
- Partition MinIO buckets per tenant
- Partition OpenSearch indexes per tenant
- Wire tenancy into pipeline steps
- Tenant provisioning and quota management

### Phase 3 - Observability & Reliability
- Instrument services with OpenTelemetry
- Replace static health check with dependency probes
- Add Hangfire job dashboards with alerts
- Define SLOs and error budgets

## Support

For questions or issues:
- Review the documentation in this directory
- Check the security test checklist for common scenarios
- See the main [README.md](../README.md) for general information
