# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| Latest  | Yes                |
| Older   | No                 |

Only the latest release receives security fixes. We recommend always running the most recent version.

## Reporting a Vulnerability

If you discover a security vulnerability in Winix, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please use [GitHub private vulnerability reporting](https://github.com/Yortw/winix/security/advisories/new) to submit your report. This ensures the issue is handled privately until a fix is available.

### What to include

- Description of the vulnerability
- Steps to reproduce
- Affected tool(s) and version(s)
- Impact assessment (if known)

### What to expect

- Acknowledgement within 48 hours
- An assessment and remediation timeline within 7 days
- Credit in the release notes (unless you prefer to remain anonymous)

## Scope

Winix tools execute child processes and handle file system operations. Areas of particular security interest include:

- Command injection via argument handling
- Path traversal in file operations
- Unsafe handling of symbolic links
- Compression-related attacks (e.g., zip bombs, path traversal in archives)
