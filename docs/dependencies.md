# Dependencies

<!-- Maintained by /emmz:architecture; verified by `make deps`. -->

| Tool | Why | Version | Check | Auth check | Install / auth |
|---|---|---|---|---|---|
| dotnet | build, test, format, AOT publish (server, CLI, contracts) | >=10.0.403 (pinned by `global.json`, `rollForward: latestFeature`; 10.0.400 bundles a vulnerable AOT compiler, GHSA-p3jp-9v83-76w9) | `dotnet --list-sdks \| grep -Eq '^10\.0\.(40[3-9]\|4[1-9][0-9]\|[5-9][0-9]{2})'` | — | `dotnetup install 10.0.4xx` (dotnetup manages the SDKs on Emmz's machine) |
| dotnet-ef | EF Core migrations (design D6) | 10.0.x, matching the EF Core packages | `dotnet ef --version \| grep -q '^10\.'` | — | `dotnet tool install -g dotnet-ef --version 10.0.14` |
| clang toolchain | Native AOT link step for the CLI | Xcode CLT (macOS); clang + zlib dev (Linux) | `xcrun --find clang` (macOS) / `clang --version` (Linux) | — | `xcode-select --install` (macOS) / `apt install clang zlib1g-dev` (Linux) |
| node | web build, test, lint, format | >=22 | `node --version` | — | `brew install node` |
| npm | web package manager | >=10 | `npm --version` | — | ships with node |
| docker | Testcontainers (Postgres, SeaweedFS) in the dotnet test gate; image build | >=24 | `docker --version` | `docker info` | OrbStack: `brew install --cask orbstack` |
| git | repo identity, version control | any | `git --version` | — | `brew install git` |
| openspec | change tracking | >=1.15 | `openspec --version` | — | `npm i -g @fission-ai/openspec` |
| gh | PRs, GitHub Releases for CLI binaries | any | `gh --version` | `gh auth status` | `brew install gh`, then `gh auth login` |
| railway | reference deployment (Railway service, Postgres, Bucket) | >=4 | `railway --version` | `railway whoami` | `brew install railway`, then `railway login` |
