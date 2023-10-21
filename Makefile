build:
	docker-compose -f provisioning/docker-compose.yml --project-name cl $(MAKECMDGOALS)

up:
	docker-compose -f provisioning/docker-compose.yml --project-name cl $(MAKECMDGOALS) --remove-orphans

up-with-elk:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.elk.yml --project-name cl $(MAKECMDGOALS) --remove-orphans

down:
	docker-compose -f provisioning/docker-compose.yml --project-name cl $(MAKECMDGOALS)

down-with-elk:
	docker-compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.elk.yml --project-name cl $(MAKECMDGOALS)

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
