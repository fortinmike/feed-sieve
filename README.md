# feed-sieve

A simple self-hosted RSS feed proxy that filters out unwanted items.

## Native Dev Setup

- Install .NET (see required version in `feed-sieve.csproj`)
- Run `dotnet watch` to start the server and watch for changes

## Running in Docker

Running this command will build the image and start the `feed-sieve` container which can be reached on port 9010.

    docker compose up --build
