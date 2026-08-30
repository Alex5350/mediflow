# Multi-stage build shared by all MediFlow services. Build with:
#   docker build -f src/MediFlow.Claims.Api/Dockerfile --build-arg PROJECT=MediFlow.Claims.Api .
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG PROJECT
WORKDIR /src

# Restore first for layer caching.
COPY Directory.Build.props Directory.Packages.props* nuget.config ./
COPY src/MediFlow.Domain/*.csproj src/MediFlow.Domain/
COPY src/MediFlow.Contracts/*.csproj src/MediFlow.Contracts/
COPY src/MediFlow.Infrastructure/*.csproj src/MediFlow.Infrastructure/
COPY src/MediFlow.Claims.Api/*.csproj src/MediFlow.Claims.Api/
COPY src/MediFlow.Claims.Api/*.csproj src/MediFlow.Claims.Api/
COPY src/MediFlow.Worker/*.csproj src/MediFlow.Worker/
COPY src/MediFlow.Blazor/*.csproj src/MediFlow.Blazor/
COPY src/MediFlow.Mcp/*.csproj src/MediFlow.Mcp/
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"

COPY . .
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
ARG PROJECT
WORKDIR /app
COPY --from=build /app/publish .

# Non-root runtime user.
RUN addgroup -S mediflow && adduser -S mediflow -G mediflow \
    && chown -R mediflow:mediflow /app
USER mediflow

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_TieredPGO=1

# Alpine culture ICU: invariant globalization keeps the image small.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "MediFlow.Claims.Api.dll"]
