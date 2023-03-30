# Bannou Service

[![GitHub Actions](https://github.com/ParnassianStudios/bannou-service/actions/workflows/all.yml/badge.svg)](https://github.com/ParnassianStudios/bannou-service/actions/workflows/all.yml)

Bannou Service is a versatile ASP.NET Core application designed to provide a seamless codebase for creating HTTP Dapr APIs with minimal effort. Primarily designed to support a common backend microservice framework for largely multiplayer online video games, the service could in theory be a core part of any system requiring infinitely extensible REST APIs. By coupling with game engine servers like Unreal or Unity, Bannou Service becomes the foundation of the universal cloud-based platform for developing and hosting multiplayer video games, tentatively called "CelestialLink".

## Table of Contents

- [Bannou Service](#bannou-service)
  - [Table of Contents](#table-of-contents)
  - [Features](#features)
  - [Local Deploy (Compose)](#local-deploy-compose)
    - [Prerequisites](#prerequisites)
    - [Manual](#manual)
    - [Make](#make)
  - [Extending the Service](#extending-the-service)
    - [Adding APIs](#adding-apis)
    - [Implementing IDaprService](#implementing-idaprservice)
    - [Implementing IServiceConfiguration](#implementing-iserviceconfiguration)
    - [Implementing IDaprController](#implementing-idaprcontroller)
    - [Implementing IServiceAttribute](#implementing-iserviceattribute)
  - [Generated Docs (WIP)](#generated-docs-wip)
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

    `git clone https://github.com/ParnassianStudios/bannou-service.git`

2. To build, run the following, replacing `my_project` with your own project name:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project build`

3. To deploy locally (minimal service setup), run the following:

    `docker-compose -f provisioning/docker-compose.yml --project-name my_project up -d`

### Make

Alternatively, the following make commands have been provided to simplify the process. "cl" is used as a default project name with these.

1. `make build`
2. `make up -d`
3. `make down`

## Extending the Service

The Bannou Service's primary goal is to be extensible, and as such, there are numerous ways to do so. The traditional approach would be to fork this repository, adding your own APIs (see below on adding APIs) directly to the main application code in the same way that the existing services, controllers, and configuration are set up. The application code itself is your guide- MVC controllers are still the same MVC controllers you'd always deal with in .NET 7, and should be fairly self-explanatory.

Alternatively, you can add this entire repository as a project reference / git submodule of your own .NET application. The Program class in this monoservice has been kept intentionally minimalist, and all mechanisms of service, controller, and configuration discovery have been written in a way to also include other loaded assemblies. Until more examples can be included, the `unit tests` project clearly shows that extending the base attribute, configuration, service, and controller classes works just fine when using the Bannou Service as a project reference.

Finally, you can build this application using the dotnet commandline build tool, and reference/include in the generated library in your own application.

### Adding APIs

To add an API controller, first determine if the APIs are distinct enough to potentially have their own on/off switch enabling them for a given service instance. In other words, would you want only some nodes in your services network to have these APIs enabled, while others have them disabled?

If you need that control, then you'll want to start by implementing IDaprService in a new "service handler" class- this new class is where you'll do your initial setup in support of the API controller you'll be adding. It gives you the on/off switch to enable and disable the controllers by configuration, as well as handling long-running tasks for maintenance, cleanup, or generating events of some kind.

If your new APIs are generic enough that you don't mind requests being spread across every node in the network without that level of control, you can forego having a "service handler" entirely, and move right to adding the Controller.

### Implementing IDaprService

Dapr services are the classes which support the API controllers, by providing the business logic of transactional requests, an entry point for starting long-running service tasks, the cleanup handler when the service is shutting down, and the means by which easy per-controller configuration can be generated.

There are two requirements for adding a new "service handler" to the application- one is implementing the IDaprService interface, and the second is decorating the class with the `[DaprServiceAttribute]`. The attribute allows you to specify a service handler name- this should be unique per Dapr service, as it's used to determine the ENV for enabling and disabling the service handler (and its associated API controllers).

Your individual Dapr service can be enabled by setting `{SERVICE_NAME}_SERVICE_ENABLED=true` as an ENV or `--{service_name}-service-enabled=true` as a switch.

### Implementing IServiceConfiguration

Configuration is meant to be generated per-Dapr service, so there are a few mechanisms for doing so easily. Implement the IServiceConfiguration interface and decorate your configuration class with the `[ServiceConfiguration]` attribute pointing to the particular Dapr service it's meant for, and that's all you need. Any existing ENVs (and args/switches, if you provide them) will be used to populate your configuration class automatically. By adding a "prefix" param to the config attribute, you can specify that only ENVs with a given prefix should be used to populate it instead (a common mechanism for per-component configuration in a .NET application).

By decorating any configuration property in your new class with `[Required]` (from DataAnnotations), it will cause the service to fail to start if the configuration is NOT provided when the Dapr service is set to be enabled.

Dapr services can have any number of configuration classes without issue, but if you have more than one, then you should be sure to set `primary=true` in the configuration attribute for the one you wish to be treated as such. This equated to the configuration type being selected / populated automatically when building from the direction of the Dapr service / controller, without knowing the specific configuration type to use. And `[Required]` attributes in classes that are NOT the primary configuration for a service type will effectively be by ignored.

### Implementing IDaprController

To add a new API controller exposed through Dapr, implement IDaprController and decorate the class with the `[DaprController]` attribute. The attribute extends the `[Route]` attribute, so can be thought of the same way- add a template string for the route your controller will use, and each method will then further append to that route.

If your API controller has business logic, you should add a Dapr Service class (see above) as well to handle that logic, and reference the Dapr service type from the attribute on your Dapr controller class, to indicate that the controller is under the control of that Dapr service. A Dapr service can have any number of associated controllers (meaning, any number of controllers can reference the service), but the opposite is not true- Dapr controllers can only reference one Dapr service each.

If you need to make a request to a different Dapr service, meaning to APIs that may be optionally disabled on your particular node, then you MUST post that request out to the Dapr sidecar, and not attempt to bypass that step internally. This is prevent issues with concurrency, among other things.

### Implementing IServiceAttribute

IServiceAttribute is a shared interface for all app-specific attributes. The interface contains helper methods for finding all classes, methods, properties, or fields decorated with a given concrete type implementing IServiceAttribute.

The `[DaprService]`, `[DaprController]`, and `[ServiceConfiguration]` attributes all implement IServiceAttribute, and those same helper methods are what are used to locate and perform operations on all of the classes decorated with those attributes, so they can be used as examples of how those methods work.

## Generated Docs (WIP)

- [Service Configuration](documentation/configuration.md)
- [Service APIs](documentation/services.md)

## Contributing

If you would like to contribute to the Bannou Service project, please follow the [contributing guidelines](documentation/CONTRIBUTING.md).

## License

This project is licensed under the [MIT License](LICENSE).
