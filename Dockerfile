# Build and run the web application.
#
# Two stages so the image that ships carries the runtime and the published output, not the SDK,
# the source and the NuGet cache. The restore is its own layer keyed on the project files alone,
# so editing code does not re-download every package.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Project files first: this layer is rebuilt only when a dependency actually changes.
COPY Directory.Build.props ./
COPY global.json ./
COPY PropertyManagement.slnx ./
COPY src/PropertyManagement.Domain/*.csproj src/PropertyManagement.Domain/
COPY src/PropertyManagement.Application/*.csproj src/PropertyManagement.Application/
COPY src/PropertyManagement.Infrastructure/*.csproj src/PropertyManagement.Infrastructure/
COPY src/PropertyManagement.Web/*.csproj src/PropertyManagement.Web/
COPY tests/PropertyManagement.Domain.Tests/*.csproj tests/PropertyManagement.Domain.Tests/
COPY tests/PropertyManagement.Infrastructure.Tests/*.csproj tests/PropertyManagement.Infrastructure.Tests/
COPY tests/PropertyManagement.Web.Tests/*.csproj tests/PropertyManagement.Web.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/PropertyManagement.Web -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Where Data Protection keys go when DataProtection:KeyPath points here. Created before the user
# switch and given to that user, so a named volume mounted over it inherits an owner that can write.
RUN mkdir -p /keys && chown $APP_UID /keys

# Runs as the non-root user the base image provides, so a container escape is not a root shell.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "PropertyManagement.Web.dll"]
