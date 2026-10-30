FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
ARG PROJECT
WORKDIR /src

COPY Directory.Build.props ./
COPY ["src/shared/Bcra.Shared.Domain/Bcra.Shared.Domain.csproj", "src/shared/Bcra.Shared.Domain/"]
COPY ["src/shared/Bcra.Shared.Infrastructure/Bcra.Shared.Infrastructure.csproj", "src/shared/Bcra.Shared.Infrastructure/"]
COPY ["src/services/${SERVICE}/${PROJECT}.Domain/${PROJECT}.Domain.csproj", "src/services/${SERVICE}/${PROJECT}.Domain/"]
COPY ["src/services/${SERVICE}/${PROJECT}.Application/${PROJECT}.Application.csproj", "src/services/${SERVICE}/${PROJECT}.Application/"]
COPY ["src/services/${SERVICE}/${PROJECT}.Infrastructure/${PROJECT}.Infrastructure.csproj", "src/services/${SERVICE}/${PROJECT}.Infrastructure/"]
COPY ["src/services/${SERVICE}/${PROJECT}.Api/${PROJECT}.Api.csproj", "src/services/${SERVICE}/${PROJECT}.Api/"]
RUN dotnet restore "src/services/${SERVICE}/${PROJECT}.Api/${PROJECT}.Api.csproj"

COPY . .
RUN dotnet publish "src/services/${SERVICE}/${PROJECT}.Api/${PROJECT}.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJECT
WORKDIR /app
COPY --from=build /app/publish .
ENV PROJECT_DLL=${PROJECT}.Api.dll
EXPOSE 8080
ENTRYPOINT exec dotnet "$PROJECT_DLL"
