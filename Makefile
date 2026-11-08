# Panopticon — gate targets.
#
# Every gate prints its own exit code as `LABEL_EXIT:<n>` and exits with it, so a
# result can be quoted rather than inferred from output that may look clean.
# `gates` runs the whole set with -k, so one run reports every failure.

SHELL := /bin/bash

CHANGES := $(notdir $(patsubst %/,%,$(filter-out %/archive/,$(wildcard openspec/changes/*/))))

.PHONY: build test format aot web-install web-build web-test web-format web-lint validate changes gates deps

build:
	@dotnet build Panopticon.slnx -c Release -warnaserror; code=$$?; echo "BUILD_EXIT:$$code"; exit $$code

# Needs Docker (Testcontainers: Postgres, MinIO).
test:
	@dotnet test --solution Panopticon.slnx -c Release; code=$$?; echo "TEST_EXIT:$$code"; exit $$code

format:
	@dotnet format Panopticon.slnx --verify-no-changes; code=$$?; echo "FORMAT_EXIT:$$code"; exit $$code

# Native AOT publish of the CLI — AOT problems only surface at publish time.
aot:
	@dotnet publish src/Panopticon.Cli/Panopticon.Cli.csproj -c Release --use-current-runtime -warnaserror -o artifacts/cli; code=$$?; echo "AOT_EXIT:$$code"; exit $$code

# Prerequisite for the web gates, not a gate itself (make runs it once per invocation).
web-install:
	@npm --prefix web ci; code=$$?; echo "WEB_INSTALL_EXIT:$$code"; exit $$code

web-build: web-install
	@npm --prefix web run build; code=$$?; echo "WEB_BUILD_EXIT:$$code"; exit $$code

web-test: web-install
	@npm --prefix web run test; code=$$?; echo "WEB_TEST_EXIT:$$code"; exit $$code

web-format: web-install
	@npm --prefix web run format:check; code=$$?; echo "WEB_FORMAT_EXIT:$$code"; exit $$code

web-lint: web-install
	@npm --prefix web run lint; code=$$?; echo "WEB_LINT_EXIT:$$code"; exit $$code

# Validates every active change. No active change is a failure, not a silent pass.
validate:
	@fail=0; \
	if [ -z "$(CHANGES)" ]; then \
		echo "no active change found under openspec/changes/"; fail=1; \
	else \
		for c in $(CHANGES); do openspec validate $$c --strict || fail=1; done; \
	fi; \
	echo "VALIDATE_EXIT:$$fail"; exit $$fail

changes:
	@echo "$(CHANGES)"

gates:
	@$(MAKE) --no-print-directory -k build test format aot web-build web-test web-format web-lint validate; code=$$?; \
	echo "GATES_EXIT:$$code"; exit $$code

# Every tool and login in docs/dependencies.md — keep the two in step. Not part of
# `gates`: it checks the machine, not the code, and auth checks may need the network.
deps:
	@fail=0; \
	dotnet --list-sdks 2>/dev/null | grep -Eq '^10\.0\.(40[3-9]|4[1-9][0-9]|[5-9][0-9]{2})' || { echo "MISSING/WRONG VERSION dotnet — need SDK 10.0.403+: dotnetup install 10.0.4xx"; fail=1; }; \
	if [ "$$(uname)" = "Darwin" ]; then xcrun --find clang >/dev/null 2>&1 || { echo "MISSING clang toolchain — xcode-select --install"; fail=1; }; \
	else clang --version >/dev/null 2>&1 || { echo "MISSING clang toolchain — apt install clang zlib1g-dev"; fail=1; }; fi; \
	node --version 2>/dev/null | grep -Eq '^v(2[2-9]|[3-9][0-9])\.' || { echo "MISSING/WRONG VERSION node — need >=22: brew install node"; fail=1; }; \
	npm --version 2>/dev/null | grep -Eq '^(1[0-9]|[2-9][0-9])\.' || { echo "MISSING/WRONG VERSION npm — need >=10 (ships with node)"; fail=1; }; \
	docker --version >/dev/null 2>&1 || { echo "MISSING docker — brew install --cask orbstack"; fail=1; }; \
	docker info >/dev/null 2>&1 || { echo "NOT RUNNING/REACHABLE docker — start OrbStack"; fail=1; }; \
	git --version >/dev/null 2>&1 || { echo "MISSING git — brew install git"; fail=1; }; \
	openspec --version >/dev/null 2>&1 || { echo "MISSING openspec — npm i -g @fission-ai/openspec"; fail=1; }; \
	gh --version >/dev/null 2>&1 || { echo "MISSING gh — brew install gh"; fail=1; }; \
	gh auth status >/dev/null 2>&1 || { echo "NOT AUTHENTICATED gh — gh auth login"; fail=1; }; \
	railway --version >/dev/null 2>&1 || { echo "MISSING railway — brew install railway"; fail=1; }; \
	railway whoami >/dev/null 2>&1 || { echo "NOT AUTHENTICATED railway — railway login"; fail=1; }; \
	echo "DEPS_EXIT:$$fail"; exit $$fail
