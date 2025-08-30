# feed-sieve

A simple self-hosted RSS feed proxy that filters out unwanted items.

## Native Dev Setup

- Install .NET (see required version in `feed-sieve.csproj`)
- Duplicate the `ruleset.example.yaml` file as `ruleset.default.yaml` (this will be the ruleset you'll be testing with)
- Run `dotnet watch` to start the server and watch for changes

## Manual Publishing

To produce a build that can be deployed on bare metal run the following command:

    rm -rf publish && dotnet publish -c Release --output publish

## Running in Docker (in this cloned project)

Running this command will build the image using the project's Dockerfile and start the `feed-sieve` container which can be reached on port 6677.

    docker compose up --build

## Running in Docker (on any machine)

This project is not on Docker Hub (yet). You can still run it easily with a simple configuration that refers to this repo directly. The suggested method is to create a `docker-compose.yml` file:

    name: feed-sieve
    services:
      app:
        build:
          context: https://github.com/fortinmike/feed-sieve.git
        restart: always
        ports:
          - 6677:6677
        volumes:
          - ./volumes/rules.yaml:/app/rules.yaml:rw

Then run `docker compose up` in the directory where the file is located.

If you just want to start the server without the convenience of Docker Compose, you can convert the suggested configuration to a `docker run` command manually or using a number of online tools.
