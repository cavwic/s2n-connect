# Security policy

## Supported versions

Only the latest tagged release is supported.

## Reporting

Use GitHub private vulnerability reporting for credential disclosure, SSH
host-key verification, command injection, or privilege escalation issues.

## Credential handling

- Only the SSH private-key path is stored, never key contents.
- The key path is saved only after a successful SSH connection.
- Key passphrases are not persisted.
- The sing-box path is saved only after checksum and binary validation.
- VPS profile files contain client credentials and should be protected by the
  operating-system user account.
