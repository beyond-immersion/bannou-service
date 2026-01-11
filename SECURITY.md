# Security Policy

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue, please report it responsibly.

### How to Report

**For sensitive security issues**, please use one of these private channels:

1. **Email**: admin@beyond-immersion.com
2. **GitHub Security Advisories**: [Report a vulnerability](https://github.com/beyond-immersion/bannou-service/security/advisories/new) (preferred for detailed reports)

**Please do not:**
- Open public GitHub issues for security vulnerabilities
- Discuss vulnerabilities in Discord or other public channels
- Exploit vulnerabilities beyond what's necessary to demonstrate them

### What to Include

When reporting, please provide:

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fixes (optional but appreciated)

### What to Expect

1. **Acknowledgment**: We'll acknowledge receipt within 48 hours
2. **Assessment**: We'll assess severity and determine a fix timeline
3. **Updates**: We'll keep you informed of progress
4. **Credit**: With your permission, we'll credit you in the security advisory

### Severity Levels

| Severity | Examples | Typical Response |
|----------|----------|------------------|
| Critical | Remote code execution, auth bypass, data breach | Fix within 24-48 hours |
| High | Privilege escalation, sensitive data exposure | Fix within 1 week |
| Medium | Limited data exposure, denial of service | Fix within 2 weeks |
| Low | Minor information disclosure | Fix in next release |

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest release | Yes |
| Previous release | Security fixes only |
| Older versions | No |

We recommend always running the latest version.

## Security Considerations

### For Self-Hosting

When deploying Bannou, consider:

- **Secrets Management**: Never commit secrets to version control. Use environment variables or secret management tools.
- **Network Security**: Place services behind a firewall. Only expose Connect (WebSocket gateway) and Website services publicly.
- **TLS**: Always use HTTPS/WSS in production.
- **Updates**: Keep dependencies updated, especially for security patches.

### Architecture Security Features

Bannou includes several security features by design:

- **Client-Salted GUIDs**: Each client receives unique endpoint identifiers, preventing cross-session attacks
- **Permission System**: Fine-grained x-permissions on all endpoints
- **Schema Validation**: All requests validated against OpenAPI schemas
- **Infrastructure Abstraction**: No direct database/queue access from services

### Known Limitations

- The development configuration in `.env.example` uses placeholder secrets. Never use these in production.
- WebSocket connections without TLS expose traffic to interception.
- The JWT secret must be strong and kept confidential.

## Security Updates

Security updates are announced through:

- GitHub Security Advisories
- GitHub Releases (with security tag)
- Discord announcements channel

## Acknowledgments

We appreciate security researchers who help keep Bannou safe. Contributors who responsibly disclose vulnerabilities will be acknowledged (with permission) in our security advisories.

Thank you for helping keep Bannou and its users safe.
