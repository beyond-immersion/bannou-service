FROM mcr.microsoft.com/dotnet/sdk:7.0

COPY . /app
WORKDIR /app

RUN dotnet restore
RUN dotnet build

CMD ["dotnet", "run", "--project", "bannou-service"]
