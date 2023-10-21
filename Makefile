build:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.swarm.yml --project-name cl build

up:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.swarm.yml --project-name cl up

elk up:
	if [ ! -f .env ]; then touch .env; fi
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.swarm.yml -f provisioning/docker-compose.elk.yml --project-name cl up

down:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.swarm.yml --project-name cl down --remove-orphans

elk down:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.swarm.yml -f provisioning/docker-compose.elk.yml --project-name cl down --remove-orphans

clean:
	git submodule foreach --recursive git clean -fdx

libs:
	bash ./build-libs.sh

tests:
	bash ./service-tests.sh

sync:
	git pull && git submodule update --init --recursive

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
