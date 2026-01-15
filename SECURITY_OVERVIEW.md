# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 3.x     | :white_check_mark: |
| 2.x     | :x: (Python legacy)|
| 1.x     | :x:                |

## Security Features

This application implements several security measures:

### Data Protection
- **Encrypted Settings**: All user settings are encrypted using Windows DPAPI (Data Protection API)
- **Encrypted Progress**: User progression data is protected with the same encryption
- **Machine-Bound**: Encryption keys are derived from machine-specific identifiers

### Code Security
- **Input Sanitization**: All user inputs are sanitized to prevent injection attacks
- **Path Traversal Protection**: File paths are validated to prevent directory traversal
- **Rate Limiting**: Built-in rate limiting for sensitive operations

### Application Security
- **Single Instance**: Prevents multiple instances from running simultaneously
- **Memory Protection**: Sensitive data is cleared from memory after use
- **No Network**: Application operates entirely offline with no external connections
- **No Telemetry**: Zero data collection or phone-home functionality

### Build Security
- **Trimmed Build**: Unused code is removed, reducing attack surface
- **ReadyToRun**: Pre-compiled for faster startup and harder reverse engineering
- **Deterministic Builds**: Reproducible builds for verification

## Reporting a Vulnerability

If you discover a security vulnerability, please:

1. **DO NOT** open a public issue
2. Use GitHub's [Private Security Advisory](../../security/advisories/new) feature

### What to Include
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### Response Timeline
- **Acknowledgment**: Within 48 hours
- **Initial Assessment**: Within 1 week
- **Resolution**: Varies based on severity

## Security Best Practices for Users

1. **Always keep safety features enabled** (Panic Key, Mercy System)
2. **Download only from official releases** on GitHub
3. **Verify file hashes** if provided in release notes
4. **Don't run as Administrator** unless necessary
5. **Keep Windows updated** for latest security patches

## Known Limitations

- **No Code Signing**: Executables are not signed with a certificate (Windows SmartScreen warning expected)
- **Decompilable**: As with all .NET applications, determined attackers could reverse engineer the code
- **Local Storage**: Settings are stored locally and could be accessed by other programs with user privileges

## Acknowledgments

We appreciate security researchers who responsibly disclose vulnerabilities.
