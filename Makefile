build:
	dotnet build

build-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build

up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

ci-up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ci.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

elk-up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl up -d

down-compose:
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl down --remove-orphans

elk-down-compose:
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl down --remove-orphans

# Cleans caches, generated files, and clears up resources from git, docker, and dotnet
# Usage: make clean [PLUGIN=plugin-name] - if PLUGIN is specified, only cleans that plugin
clean:
	@if [ "$(PLUGIN)" ]; then \
		echo "🧹 Cleaning plugin: $(PLUGIN)..."; \
		if [ -d "./lib-$(PLUGIN)/Generated" ]; then \
			rm -rf "./lib-$(PLUGIN)/Generated"; \
			echo "  Removed lib-$(PLUGIN)/Generated"; \
		else \
			echo "  No Generated directory found for lib-$(PLUGIN)"; \
		fi; \
		echo "✅ Clean completed for plugin: $(PLUGIN)"; \
	else \
		echo "🧹 Cleaning all generated files..."; \
		find . -path "./lib-*/Generated" -type d -exec rm -rf {} + 2>/dev/null || true; \
		rm -rf Bannou.Client.SDK 2>/dev/null || true; \
		echo "🧹 Cleaning caches and resources..."; \
		git submodule foreach --recursive git clean -fdx && docker container prune -f && docker image prune -f && docker volume prune -f && dotnet clean; \
		echo "✅ Clean completed"; \
	fi

# Builds all libs with xml service tags and copies the DLLs to /libs directory
build-service-libs:
	@echo "🔧 Building service libs for docker container"
	bash scripts/build-service-libs.sh
	@echo "✅ Service libs built for inclusion in docker container"

# Regenerate all services and SDK
generate-all:
	@echo "🔧 Generating everything"
	scripts/generate-all-services.sh
	scripts/generate-client-sdk.sh
	@echo "✅ All generations completed"

# Regenerate all plugins/types but service implementations from schema
generate-services:
	@if [ "$(PLUGIN)" ]; then \
		echo "🔧 Generating plugin: $(PLUGIN)..."; \
		scripts/generate-all-services.sh $(PLUGIN); \
		echo "✅ Service generation completed for plugin: $(PLUGIN)"; \
	else \
		echo "🔧 Generating all services (NSwag + Roslyn)..."; \
		scripts/generate-all-services.sh; \
		echo "✅ Service generation completed"; \
	fi

# Generate Client SDK from generated services
generate-sdk:
	@echo "🔧 Generating Bannou Client SDK..."
	scripts/generate-client-sdk.sh
	@echo "✅ Client SDK generation completed"

# Fix line endings and final newlines for all project files
fix-endings:
	@echo "🔧 Fixing line endings for all project files..."
	scripts/fix-endings.sh

# Complete formatting workflow
format: fix-endings
	@echo "🔧 Running complete code formatting..."
	dotnet format
	@echo "✅ All formatting completed"

# Bring project and all submodules up to latest
sync:
	git pull && git submodule update --init --recursive

# GitHub Actions 10-step pipeline (local reproduction)
test-ci: generate-services-for-consistency test-unit build-compose ci-up-compose test-infrastructure test-http-daemon test-edge-daemon
	@echo "🚀 Complete CI pipeline executed locally (matches GitHub Actions)"

# Service generation consistency check (matches steps 4-5)
generate-services-for-consistency:
	@echo "🔍 Testing service generation consistency..."
	$(MAKE) generate-services
	git diff --exit-code || (echo "❌ Service generation created changes" && exit 1)
	@echo "✅ Service generation is consistent"

# Unit testing (matches steps 6)
test-unit:
	@echo "🧪 Running unit tests..."
	dotnet test

# Infrastructure testing
test-infrastructure:
	@echo "🚀 Running infrastructure tests"
	bash scripts/infrastructure-tests.sh

# Infrastructure testing (matches step 7)
test-infrastructure-compose:
	@echo "🚀 Running infrastructure tests with Docker Compose..."
	if [ ! -f .env ]; then touch .env; fi
	set -a && . ./.env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester

# HTTP testing
test-http:
	@echo "🧪 Running HTTP endpoint tests..."
	dotnet run --project http-tester

# HTTP testing with daemon mode (matches step 8)
test-http-daemon:
	@echo "🧪 Running HTTP integration tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project http-tester --configuration Release

# WebSocket testing
test-edge:
	@echo "🧪 Running WebSocket protocol tests..."
	dotnet run --project edge-tester

# WebSocket testing with daemon mode (matches steps 9-10)  
test-edge-daemon:
	@echo "🧪 Running WebSocket protocol tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project edge-tester --configuration Release

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
