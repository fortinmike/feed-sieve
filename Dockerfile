# Stage 1: Use the full .NET 10 SDK to build the project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the csproj first to leverage Docker cache for dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the source code
COPY . ./

# Publish the app in Release configuration to /app/publish
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Use lightweight ASP.NET runtime image for final container
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Copy the published output from the build stage
COPY --from=build /app/publish .

# Expose the internal port the app listens on
EXPOSE 6677

# Set the entrypoint to run the server
ENTRYPOINT ["dotnet", "feed-sieve.dll"]
