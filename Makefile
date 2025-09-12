build:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build

up:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

ci_up:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ci.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

elk_up:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl up -d

down:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl down --remove-orphans

elk_down:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl down --remove-orphans

clean:
	git submodule foreach --recursive git clean -fdx && docker container prune -f && docker image prune -f && docker volume prune -f

libs:
	bash ./build-libs.sh

tests:
	bash ./service-tests.sh

# Local development testing commands
test-http:
	@echo "🧪 Running HTTP endpoint tests..."
	dotnet run --project http-tester

test-websocket:
	@echo "🧪 Running WebSocket protocol tests..."
	dotnet run --project edge-tester

test-integration:
	@echo "🧪 Running integration tests with Docker..."
	if [ ! -f .env ]; then touch .env; fi
	docker-compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.ci.yml --project-name bannou-test up --exit-code-from=bannou-tester

test-all: test-unit test-http test-websocket test-integration
	@echo "✅ All tests completed"

test-unit:
	@echo "🧪 Running unit tests..."
	dotnet test

# CI/CD testing (matches GitHub Actions)
ci-test:
	@echo "🚀 Running CI test pipeline..."
	if [ ! -f .env ]; then touch .env; fi
	docker-compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" build --no-cache --pull
	docker-compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester
	docker-compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" down --remove-orphans -v

generate-services:
	@echo "🔧 Generating all services (NSwag + Roslyn)..."
	./generate-all-services.sh
	@echo "✅ Service generation completed"

# Fix line endings and final newlines for all project files
fix-endings:
	@echo "🔧 Fixing line endings for all project files..."
	./fix-endings.sh

# Complete formatting workflow  
format: fix-endings
	@echo "🔧 Running complete code formatting..."
	dotnet format
	@echo "✅ All formatting completed"

generate-services-legacy:
	@echo "🔧 [LEGACY] Generating services from OpenAPI schemas via MSBuild..."
	@echo "⚠️  This method has known issues with NSwag config file execution"
	cd bannou-service && dotnet build -p:GenerateNewServices=true
	@echo "✅ Service generation completed"

regenerate-all-services: generate-services
	@echo "✅ All services regenerated (using working script method)"

regenerate-all-services-legacy:
	@echo "🔧 [LEGACY] Regenerating all services (including clients and events)..."
	@echo "⚠️  This method has known issues with NSwag config file execution"
	cd bannou-service && dotnet msbuild -t:RegenerateAllServices
	@echo "✅ All services regenerated"

sync:
	git pull && git submodule update --init --recursive

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
