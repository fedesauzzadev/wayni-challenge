FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY ["src/shared/Bcra.Shared.Domain/Bcra.Shared.Domain.csproj", "src/shared/Bcra.Shared.Domain/"]
COPY ["src/shared/Bcra.Shared.Infrastructure/Bcra.Shared.Infrastructure.csproj", "src/shared/Bcra.Shared.Infrastructure/"]
COPY ["src/services/notifier/Bcra.Notifier.Domain/Bcra.Notifier.Domain.csproj", "src/services/notifier/Bcra.Notifier.Domain/"]
COPY ["src/services/notifier/Bcra.Notifier.Application/Bcra.Notifier.Application.csproj", "src/services/notifier/Bcra.Notifier.Application/"]
COPY ["src/services/notifier/Bcra.Notifier.Infrastructure/Bcra.Notifier.Infrastructure.csproj", "src/services/notifier/Bcra.Notifier.Infrastructure/"]
COPY ["src/services/notifier/Bcra.Notifier.Api/Bcra.Notifier.Api.csproj", "src/services/notifier/Bcra.Notifier.Api/"]
RUN dotnet restore "src/services/notifier/Bcra.Notifier.Api/Bcra.Notifier.Api.csproj"

COPY . .
RUN dotnet publish "src/services/notifier/Bcra.Notifier.Api/Bcra.Notifier.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bcra.Notifier.Api.dll"]
