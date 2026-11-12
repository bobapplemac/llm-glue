.DEFAULT_GOAL := help

CONFIG ?= Release
RID ?= linux-x64
OUT ?= artifacts
REVISION ?= 0
DRY_RUN ?= 0
PYTHON ?= python3
OPENCODE ?=

ifeq ($(REVISION),0)
ASSEMBLY_VERSION := 0.0.0.0
FILE_VERSION := 0.0.0.0
PACKAGE_VERSION := 0.0.0
else
ASSEMBLY_VERSION := 1.0.0.0
FILE_VERSION := 1.$(REVISION).0.0
PACKAGE_VERSION := 1.$(REVISION).0
endif
VERSION_ARGS := -p:AssemblyVersion=$(ASSEMBLY_VERSION) -p:FileVersion=$(FILE_VERSION) -p:InformationalVersion=r$(REVISION) -p:Version=$(PACKAGE_VERSION)

.PHONY: help restore build docs docs-check test test-litellm clean version-policy publish-linux publish-windows package release-check release

help: ## Show this help and configurable variables
	@awk 'BEGIN {FS = ":.*## "; printf "LLM Glue r0 build\n\n"} /^[a-zA-Z0-9_-]+:.*## / {printf "  %-18s %s\n", $$1, $$2} END {printf "\nVariables: CONFIG=%s RID=%s OUT=%s REVISION=%s DRY_RUN=%s PYTHON=%s OPENCODE=%s\n", "$(CONFIG)", "$(RID)", "$(OUT)", "$(REVISION)", "$(DRY_RUN)", "$(PYTHON)", "$(OPENCODE)"}' $(MAKEFILE_LIST)

restore: ## Restore pinned .NET dependencies
	dotnet restore LlmGlue.slnx

build: ## Build the solution with warnings as errors
	dotnet build LlmGlue.slnx -c $(CONFIG) --no-restore

docs: build ## Regenerate CLI and TOML reference documentation
	dotnet src/LlmGlue/bin/$(CONFIG)/net10.0/llm-glue.dll docs generate --output docs

docs-check: build ## Verify generated CLI and TOML references are current
	dotnet src/LlmGlue/bin/$(CONFIG)/net10.0/llm-glue.dll docs generate --output docs --check

test: ## Run all automated tests
	dotnet test LlmGlue.slnx -c $(CONFIG) --no-restore --verbosity minimal

test-litellm: ## Run opt-in LiteLLM 1.109.0 forwarding integration test
	$(PYTHON) scripts/test-litellm-forwarding.py $(if $(OPENCODE),--opencode "$(OPENCODE)",)

clean: ## Clean owned build output only
	dotnet clean LlmGlue.slnx -c $(CONFIG)

version-policy: ## Reject nonzero revision builds outside the guarded release path
	@if [ "$(REVISION)" != "0" ] && [ "$${LLM_GLUE_OFFICIAL_RELEASE_BUILD:-}" != "1" ]; then echo "nonzero revision builds are restricted to the guarded official release path" >&2; exit 1; fi

publish-linux: version-policy ## Publish self-contained linux-x64 single file
	dotnet publish src/LlmGlue/LlmGlue.csproj -c $(CONFIG) -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None $(VERSION_ARGS) -o $(OUT)/publish/linux-x64

publish-windows: version-policy ## Publish self-contained win-x64 single file
	dotnet publish src/LlmGlue/LlmGlue.csproj -c $(CONFIG) -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None $(VERSION_ARGS) -o $(OUT)/publish/win-x64

package: publish-linux publish-windows ## Package both RIDs and write SHA-256 manifest
	bash scripts/package.sh "$(OUT)" "$(REVISION)"

release-check: ## Run guarded release preflight without publishing
	bash scripts/release.sh --revision "$(REVISION)" --dry-run --check-only

release: ## Explicit guarded GitHub release (DRY_RUN=1 prevents mutation)
	bash scripts/release.sh --revision "$(REVISION)" $(if $(filter 1,$(DRY_RUN)),--dry-run,)
