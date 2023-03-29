build:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

up:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

down:
	docker-compose -f provisioning/docker-compose.yml --project-name cl "$@"

clean:
	git submodule foreach --recursive git clean -fdx

sync:
	git pull && git submodule update --init --recursive
