# feed-sieve

A simple self-hosted RSS feed proxy that filters out unwanted items. It's useful in the following scenarios:

- A feed is great but has too many sponsored posts, ads and other uninteresting stuff
- A site sometimes writes about things you don't want to hear about
- A bigger site offers a single feed combining all categories but only some of the categories are interesting to you
- Whatever else you can think of! I'm sure you've had some feeds you wished were higher signal-to-noise.

## Features

- Setup simple rules to filter out anything you don't want to end up in your reader app
- Match item title or content (or both) with regexes for maximum flexibility
- Works will all RSS readers; just subscribe to feed-sieve's filter URL instead of the original feed URL
- Simple & no database; feeds are processed on-the-fly and cached when nothing changed since the last source fetch
- Simple query-string based authentication to prevent use by unauthorized parties
- More coming soon...

## Editing Rules

Coming soon...

## Dev Setup

- Install the .NET SDK (see required version in `feed-sieve.csproj`).
- Duplicate the `rules.example.yaml` file as `rules.default.yaml` (this will be the rules you'll be testing with)
- Run `ASPNETCORE_ENVIRONMENT=Development dotnet watch` to start the server and watch for changes.

## Manual Publishing

To produce a build that can be deployed on bare metal run the following command:

    rm -rf publish && dotnet publish -c Release --output publish

## Running in Docker (on any machine)

This project is not on Docker Hub (yet). You can still use it easily with a simple configuration that refers to this repo directly and builds the Docker image on-the-fly before creating and starting up the container. The suggested method is to create a `docker-compose.yml` file:

    name: feed-sieve
    services:
      app:
        build:
          context: https://github.com/fortinmike/feed-sieve.git
        restart: always
        ports:
          - 6677:6677
        volumes:
          - ./volumes/rules.yaml:/app/rules.yaml:ro
          - ./appsettings.Local.json:/app/appsettings.Local.json:ro

Then run `docker compose up` in the directory where the file is located.

If you just want to start the server without the convenience of Docker Compose, you can probably convert the suggested configuration to a `docker run` command manually or using a number of online tools.

## Running in Docker (if you cloned this repo)

Running this command will build the image using the project's Dockerfile and start the `feed-sieve` container which can be reached on port 6677.

    docker compose up --build
