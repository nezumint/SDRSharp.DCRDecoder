# GitHub registration and first release

## Current state

- Version: `1.0.0`, branch: `main`.
- Source, build scripts, tests and third-party notices belong in Git.
- SDK files, archived local material, the development Python environment and downloaded runtime packages are ignored. Windows x64 release ZIPs in `artifacts/` are tracked; source ZIPs are ignored.
- Project license: MIT (LICENSE). Third-party terms are retained in THIRD_PARTY_NOTICES.md. The SDRSharp SDK is excluded.

## First commit and push

Create an empty GitHub repository without an automatically generated README or license. The local repository has no configured Git author or remote; supply your own values. Review the staged files before committing.

```powershell
git config user.name "YOUR_NAME"
git config user.email "YOUR_COMMIT_EMAIL"
git status --short
git diff --cached --stat
git commit -m "Release v1.0.0"
git remote add origin https://github.com/YOUR_ACCOUNT/YOUR_REPOSITORY.git
git push -u origin main
```

The name, email and URL above are placeholders. If the repository has not yet been staged, run `git add .` first. The local Git repository contains no imported SDK or private reference history.

## Release package

```powershell
./build.ps1
./tools/Package-Release.ps1
```

Output: `artifacts/SDRSharp.DCRDecoder-v1.0.0-win-x64.zip`.

After the initial commit, create and push a tag when ready to publish:

```powershell
git tag -a v1.0.0 -m "SDRSharp DCR Decoder 1.0.0"
git push origin v1.0.0
```

The Windows x64 release ZIP in `artifacts/` is tracked in Git. It can also be attached to the GitHub release. Use CHANGELOG.md for the description. Do not include a user-installed voice engine; the packaging script rejects engine files and wheels.

GitHub Actions verifies SDK-independent builds and 19 self-contained audio-buffer tests. It does not perform actual SDRSharp GUI or hardware reception tests.
