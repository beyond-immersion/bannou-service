# Bannou Service

Bannou Service is a versatile ASP.NET Core application designed to provide a seamless codebase for creating HTTP Dapr APIs with minimal effort. Primarily designed to support a common backend microservice framework for largely multiplayer online video games, the service could in theory be a core part of any system requiring infinitely extensible REST APIs. By coupling with game engine servers like Unreal or Unity, Bannou Service becomes the foundation of the universal cloud-based platform for developing and hosting multiplayer video games, tentatively called "CelestialLink".

## Table of Contents

- [Bannou Service](#bannou-service)
  - [Table of Contents](#table-of-contents)
  - [Features](#features)
  - [Local Deploy (Compose)](#local-deploy-compose)
    - [Prerequisites](#prerequisites)
    - [Manual](#manual)
    - [Make](#make)
  - [Documentation](#documentation)
  - [Contributing](#contributing)
  - [License](#license)

## Features

- Utilizes C#, .NET 7, Dapr, GitHub Actions, Docker, Docker-Compose, and/or Kubernetes
- Works in conjunction with popular game engines like Unreal and Unity
- Provides a number built-in REST APIs for backend multiplayer video game support
- Easily scalable and maintainable with microservices architecture
- Complements the CelestialLink universal platform for online game development and hosting

## Local Deploy (Compose)

The service can be deployed locally (usually for initial testing purposes) with Docker-Compose / Docker for Desktop.

### Prerequisites

- Docker / Docker-Compose (Docker for Desktop)
- Make (optional)

### Manual

1. Clone this repository:

    `git clone https://github.com/your_username/bannou-service.git`

2. To build, run the following, replacing `my_project` with your own project name:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project build`

3. To deploy locally (minimal service setup), run the following:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project up -d`

### Make

Alternatively, the following make commands have been provided to simplify the process. "cl" is used as a default project name with these.

1. `make build`
2. `make up -d`
3. `make down`

## Documentation

- [Service Configuration](#documentation/configuration.md)
- [Service APIs](#documentation/controllers.md)
- [Service Diagram](#documentation/diagram.md)

## Contributing

If you would like to contribute to the Bannou Service project, please follow the [contributing guidelines](#documentation/CONTRIBUTING.md).

## License

This project is licensed under the [MIT License](#LICENSE).
