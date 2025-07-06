# Album Shuffler
This repository contains a self-contained dotnet application to automatically retrieve data from the Spotify API and save it as local JSON files.

Using this tool does not require the dotnet sdk or runtime! Download the latest release (pre-built for Linux, Mac & Windows) and run it. The downloads contain self-contained binaries meaning they do not have any external dependencies.

## Usage

### Spotify Access
In order to retrieve data from the Spotify Web API you need a Spotify account. Go to [Spotify for Developers](https://developer.spotify.com/), login and create a new app. This will give you a `client_id` and a `client_secret`. These two values allow you to create an access token that needs to be send with every request to the api. This project does not need access to user or user profile data.

### Deezer Access
The Deezer api does not require any authentication and can be used freely. 

### Input File
To tell the scripts what artists or playlists to download you need to supply an input file. It needs to be a JSON file in the following form:
```
{
  "providers": [
    {
      "id": "spotify",
      "name": "Spotify",
      "icon": "img/spotify_transparent.svg",
      "logo": "img/spotify.svg",
      "allowedTypes": [
        "artist",
        "playlist",
        "show"
      ]
    },
    {
      "id": "deezer",
      "name": "Deezer",
      "icon": "img/deezer_transparent.svg",
      "logo": "img/deezer.svg",
      "allowedTypes": [
        "artist"
      ]
    }
  ],
  "content": [
    {
      "priority": 1,
      "shortName": "ddf",
      "icon": "img/ddf_transparent.png",
      "coverCenterX": 50,
      "coverCenterY": 50,
      "altCoverCenterX": 60,
      "altCoverCenterY": 50,
      "coverColorA": "#DF030E",
      "coverColorB": "#04A5E3",
      "sources": [
        {
          "providerId": "spotify",
          "contentId": "3meJIgRw7YleJrmbpbJK6S",
          "type": "artist",
          "ignoreIds": [
            "0sCs2S5YTEN0UT1fwWpvKw",
            "59WTBKsGdomSgMztadw3uL",
            "5JHijjtr65MjdNOnNvD3Ec",
            "67Ipucoa0blx27O3sV7yAi"
          ],
          "itemNameFilter": [
            "liest ...",
            "liest...",
            "Outro",
            "Original-H\u00F6rspiel"
          ]
        },
        {
          "providerId": "deezer",
          "contentId": "71513",
          "type": "artist",
          "ignoreIds": [
          ],
          "itemNameFilter": [
            "Das verfluchte Schloss",
            "Das Geheimnis der Geisterinsel",
            "Die Originalmusik",
            "Erbe des Drachen",
            "liest ...",
            "liest...",
            "Outro",
            "Original-H\u00F6rspiel"
          ]
        }
      ]
    }
  ]
}
```
The first part of the configuration describes the providers (like Spotify and Deezer). The design of the tools is modular so providers can easily be added.
The definition serves two purposes:
- define the properties of the providers, these are necessary for proper display in the web app
- for later reference in the content defintion; all content is checked for matching providers

Here is a breakdown of a provider definition:
```
id: id of the provider; is referenced in the content definition
name: name of the provider; will be displayed in the web app
icon: icon of the provider; should be transparent, only have white color and be small
logo: big (colored) image of the provider; is displayer in the provider selection overlay
allowedTypes: defines what type of sources this provider supports; these identifiers need to match the identifiers used in the code.
```

#### Currently supported providers and their `allowedTypes`

There are two providers that are fully supported: Spotify and Deezer. There is support for exactly two artists for Amazon Music, Apple Music and YouTube Music as they do not use the respectives api but an api meta data service (https://dreimetadaten.de)

Not all providers support the same content types, here is a complete table:

| Provider          ||||
|-------------------|--------|----------|------|
| **Spotify**       | Artist | Playlist | Show |
| **Deezer**        | Artist | Playlist |      |
| **Amazon Music**  | Artist |          |      |
| **Apple Music**   | Artist |          |      |
| **YouTube Music** | Artist |          |      |

#### What is 'content' and 'source' and why do you need them?
`Content` is an abstraction of the content that Spotify/Deezer/... provide. Let's give an example. Let us assume that you want to add the evergreen "The Famous Five" by "Enid Blyton".
So the first step is to define the part of the configuration that is the same for all providers (Spotify/Deezer/...).

Here is a breakdown of a content defintion:
```
priority: Defines the order in which the artists are displayed in the artist selection
shortName: Acts as an identifier of sorts; Needs to be unique!
icon:
coverCenterX/Y: The covers are zoomed in by the web app; This tells the web app which point to center; 50,50 is the center
coverColorA/B: The covers have light glow effect on the top right und bottom left corder
altCoverCenterX/Y: Alternate cover definition; currently not implemented in a meaningful way
icon: Link to a small icon for this artist/playlist; Will be displayed in the web app
sources: see description below
type: Is either "artist" or "playlist"; tells the script which endpoint to use to download metadata
id: Id if the artist or playlist on Spotify; You can find these by looking at the urls when opening the artist/playlist in the web interface
```

As you can see that's already a lot of information but it does not contain the necessary information for the tools to download the meta data. This is where the `source` comes into play. A `source` is a definition of how to retrieve `content` for a specific `provider`.
Here is a breakdown of the source definition:
```
providerId: Id of the provider; must match the id of one of the providers defined in the provider definition
contentId: Id of the content as defined by the provider
type: Must match one of the 'allowedTypes' in the provider definition; see above for details
ignoreIds: Items retrieved from this provider which this id will be ignored
itemNameFilter: Items retrieved from this provider with any of these strings in their title will be ignored
```

#### Update data
To generate new album data run the following command:
```
./AlbumShuffler.DataRetriever $SPOTIFY_CLIENT_ID $SPOTIFY_CLIENT_SECRET $INPUT_FILE $OUTPUT_DIR
```
Running the app will remove the given output folder and recreate it to make sure its empty.
