# Support

## Before asking for help

Run:

```text
lsptool version
lsptool self-test
lsptool inspect <file-or-directory>
```

For build problems, include:

```text
dotnet --info
python --version
```

For game-file problems, include only:

- command and complete error code/message;
- toolkit version;
- operating system;
- file size and SHA-256;
- `PARAM.SFO` fields that identify the disc revision;
- JSON report produced by the command, after reviewing it for private paths.

Never upload an ISO, EBOOT, CPK, firmware file, commercial font, or translated game asset to a public issue.

## Where to look

- Installation and usage: `docs/USAGE_RU.md`, `docs/CLI_REFERENCE.md`
- Build setup: `docs/DEVELOPMENT.md`, `docs/BUILD_RU.md`
- Common failures: `docs/TROUBLESHOOTING.md`
- Binary formats: `docs/FORMATS.md`
- Security constraints: `SECURITY.md`
