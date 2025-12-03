# DbExecPlanMonitor - Multi-stage Dockerfile
# Builds a minimal runtime image for the Worker Service

# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY src/DbExecPlanMonitor.sln ./
COPY src/DbExecPlanMonitor.Domain/DbExecPlanMonitor.Domain.csproj DbExecPlanMonitor.Domain/
COPY src/DbExecPlanMonitor.Application/DbExecPlanMonitor.Application.csproj DbExecPlanMonitor.Application/
COPY src/DbExecPlanMonitor.Infrastructure/DbExecPlanMonitor.Infrastructure.csproj DbExecPlanMonitor.Infrastructure/
COPY src/DbExecPlanMonitor.Worker/DbExecPlanMonitor.Worker.csproj DbExecPlanMonitor.Worker/

# Restore dependencies (cached layer)
RUN dotnet restore DbExecPlanMonitor.Worker/DbExecPlanMonitor.Worker.csproj

# Copy source code
COPY src/DbExecPlanMonitor.Domain/ DbExecPlanMonitor.Domain/
COPY src/DbExecPlanMonitor.Application/ DbExecPlanMonitor.Application/
COPY src/DbExecPlanMonitor.Infrastructure/ DbExecPlanMonitor.Infrastructure/
COPY src/DbExecPlanMonitor.Worker/ DbExecPlanMonitor.Worker/

# Build and publish
WORKDIR /src/DbExecPlanMonitor.Worker
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    -r linux-x64 \
    --self-contained false

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r dbmonitor && useradd -r -g dbmonitor dbmonitor

# Copy published application
COPY --from=build /app/publish .

# Set ownership
RUN chown -R dbmonitor:dbmonitor /app

# Switch to non-root user
USER dbmonitor

# Environment variables for configuration
ENV DOTNET_ENVIRONMENT=Production
ENV Monitoring__Security__Mode=ReadOnly
ENV Monitoring__Security__EnableRemediation=false

# Health check endpoint (if exposed via HTTP)
# HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
#     CMD curl -f http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "DbExecPlanMonitor.Worker.dll"]
