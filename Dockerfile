# Build stage – requires the full SDK with native AOT toolchain (clang)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restore dependencies before copying the rest of the source for better layer caching
COPY Pohoda-MCP.Net.csproj .
RUN dotnet restore -r linux-x64

# Copy remaining source files and publish as a native AOT single-file binary
COPY . .
RUN dotnet publish Pohoda-MCP.Net.csproj \
    -c Release \
    -r linux-x64 \
    --no-restore \
    -o /app/publish

# Runtime stage – minimal image sufficient for native AOT / self-contained apps
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app

COPY --from=build /app/publish .

# Create a non-root user and adjust permissions for the application directory
RUN groupadd -r app && useradd -r -g app app && chown -R app:app /app
USER app

# Configure ASP.NET Core to listen on port 8080 inside the container
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Pohoda connection settings – override at runtime via environment variables:
#   Pohoda__ServerUrl, Pohoda__Username, Pohoda__Password, Pohoda__Ico
# Transport mode defaults to "http"; set Mcp__Transport=stdio for stdio mode.

ENTRYPOINT ["./Pohoda-MCP.Net"]
