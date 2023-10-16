build:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

up:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

down:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

clean:
	git submodule foreach --recursive git clean -fdx

libs:
	./build-libs.sh

sync:
	git pull && git submodule update --init --recursive

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
