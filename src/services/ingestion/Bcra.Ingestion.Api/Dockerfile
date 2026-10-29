FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY ["src/shared/Bcra.Shared.Domain/Bcra.Shared.Domain.csproj", "src/shared/Bcra.Shared.Domain/"]
COPY ["src/shared/Bcra.Shared.Infrastructure/Bcra.Shared.Infrastructure.csproj", "src/shared/Bcra.Shared.Infrastructure/"]
COPY ["src/services/ingestion/Bcra.Ingestion.Domain/Bcra.Ingestion.Domain.csproj", "src/services/ingestion/Bcra.Ingestion.Domain/"]
COPY ["src/services/ingestion/Bcra.Ingestion.Application/Bcra.Ingestion.Application.csproj", "src/services/ingestion/Bcra.Ingestion.Application/"]
COPY ["src/services/ingestion/Bcra.Ingestion.Infrastructure/Bcra.Ingestion.Infrastructure.csproj", "src/services/ingestion/Bcra.Ingestion.Infrastructure/"]
COPY ["src/services/ingestion/Bcra.Ingestion.Api/Bcra.Ingestion.Api.csproj", "src/services/ingestion/Bcra.Ingestion.Api/"]
RUN dotnet restore "src/services/ingestion/Bcra.Ingestion.Api/Bcra.Ingestion.Api.csproj"

COPY . .
RUN dotnet publish "src/services/ingestion/Bcra.Ingestion.Api/Bcra.Ingestion.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bcra.Ingestion.Api.dll"]
