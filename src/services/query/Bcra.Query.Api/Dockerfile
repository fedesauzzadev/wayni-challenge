FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY ["src/shared/Bcra.Shared.Domain/Bcra.Shared.Domain.csproj", "src/shared/Bcra.Shared.Domain/"]
COPY ["src/shared/Bcra.Shared.Infrastructure/Bcra.Shared.Infrastructure.csproj", "src/shared/Bcra.Shared.Infrastructure/"]
COPY ["src/services/query/Bcra.Query.Domain/Bcra.Query.Domain.csproj", "src/services/query/Bcra.Query.Domain/"]
COPY ["src/services/query/Bcra.Query.Application/Bcra.Query.Application.csproj", "src/services/query/Bcra.Query.Application/"]
COPY ["src/services/query/Bcra.Query.Infrastructure/Bcra.Query.Infrastructure.csproj", "src/services/query/Bcra.Query.Infrastructure/"]
COPY ["src/services/query/Bcra.Query.Api/Bcra.Query.Api.csproj", "src/services/query/Bcra.Query.Api/"]
RUN dotnet restore "src/services/query/Bcra.Query.Api/Bcra.Query.Api.csproj"

COPY . .
RUN dotnet publish "src/services/query/Bcra.Query.Api/Bcra.Query.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bcra.Query.Api.dll"]
