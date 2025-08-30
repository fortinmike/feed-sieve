# feed-sieve

A simple self-hosted RSS feed proxy that filters out unwanted items.

## Docker Dev Setup

Make sure that Docker is installed and running, then run:

    docker compose -f docker-compose.dev.yaml up --build

This builds the app and starts the server in a Docker container named feed-sieve-dev which you can access at `http://localhost:9011`.

The `Dockerfile.dev` and `docker-compose-dev.yaml` files support starting a dev server locally without 

## Native Dev Setup

- Install .NET (see required version in `feed-sieve.csproj`)
- Run `dotnet watch` to start the server and watch for changes
