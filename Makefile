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

generate-services:
	@echo "🔧 Generating services from OpenAPI schemas..."
	cd bannou-service && dotnet build -p:GenerateNewServices=true
	@echo "✅ Service generation completed"

regenerate-all-services:
	@echo "🔧 Regenerating all services (including clients and events)..."
	cd bannou-service && dotnet msbuild -t:RegenerateAllServices
	@echo "✅ All services regenerated"

sync:
	git pull && git submodule update --init --recursive

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
