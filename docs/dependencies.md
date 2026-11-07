# Dependencies

<!-- Maintained by /emmz:architecture; verified by `make deps`. -->

| Tool | Why | Version | Check | Auth check | Install / auth |
|---|---|---|---|---|---|
| dotnet | build, test, format, AOT publish (server, CLI, contracts) | >=10.0.400 (pinned by `global.json`, `rollForward: latestFeature`) | `dotnet --list-sdks \| grep -q '^10\.0\.[4-9]'` | — | `brew install --cask dotnet-sdk` (macOS) / https://dot.net/install (Linux) |
| clang toolchain | Native AOT link step for the CLI | Xcode CLT (macOS); clang + zlib dev (Linux) | `xcrun --find clang` (macOS) / `clang --version` (Linux) | — | `xcode-select --install` (macOS) / `apt install clang zlib1g-dev` (Linux) |
| node | web build, test, lint, format | >=22 | `node --version` | — | `brew install node` |
| npm | web package manager | >=10 | `npm --version` | — | ships with node |
| docker | Testcontainers (Postgres, MinIO) in the dotnet test gate; image build | >=24 | `docker --version` | `docker info` | OrbStack: `brew install --cask orbstack` |
| git | repo identity, version control | any | `git --version` | — | `brew install git` |
| openspec | change tracking | >=1.15 | `openspec --version` | — | `npm i -g @fission-ai/openspec` |
| gh | PRs, GitHub Releases for CLI binaries | any | `gh --version` | `gh auth status` | `brew install gh`, then `gh auth login` |
| railway | reference deployment (Railway service, Postgres, Bucket) | >=4 | `railway --version` | `railway whoami` | `brew install railway`, then `railway login` |
