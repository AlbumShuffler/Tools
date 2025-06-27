module AlbumShuffler.DataRetriever.Retrievers.Spotify

open System
open System.Net.Http
open System.Threading.Tasks
open AlbumShuffler.Shared
open FsToolkit.ErrorHandling
open SpotifyAPI.Web

let private httpClient : HttpClient = new HttpClient()

type SpotifyConfig = {
    ClientId: string
    ClientSecret: string
}


type private StreamResult
    = Opened of IO.Stream
    | RateLimited of TimeSpan
    | Failed of string


/// <summary>
/// Runs the given action.
/// If it encounters a <see cref="APITooManyRequestException"/> it will wait for the suggested time (plus two seconds) and try again.
/// Will abort of 20 consecutive tries fail
/// </summary>
let performRateLimitAwareRequest<'a> (action: unit -> Task<'a>) : Task<Result<'a, string>> =
    let maxRetryCount = 20
    let rec step (counter: int) =
        task {
            if counter <= maxRetryCount then
                try
                    let! result = action ()
                    return Ok result
                with
                | :? APITooManyRequestsException as rateLimitException ->
                    do! Task.Delay(rateLimitException.RetryAfter.Add(TimeSpan.FromSeconds(2.0)))
                    return! step (counter + 1)
                | exn ->
                    return Error $"Spotify api request failed because: %s{exn.Message}"
            else
                return Error $"Could not get data from api. Rate limit exceeded the retry counter of %i{maxRetryCount}"
        }
    step 1


let mapSpotifyImageToImage (image: Image) : Outputs.Image =
    {
        Url = image.Url
        Height = image.Height
        Width = image.Width
    }
    

let mapSimpleAlbumToItemOutput (album: SimpleAlbum) : Outputs.Audiobook =
    {
        Id = album.Id
        Name = album.Name
        UrlToOpen = album.ExternalUrls["spotify"]
        Images = album.Images 
                |> List.ofSeq 
                |> List.map mapSpotifyImageToImage
    }
    

/// <summary>
/// Removes all items from the list that should be excluded according to the filters
/// </summary>
/// <param name="idsToIgnore">Ids to discard</param>
/// <param name="titlesToIgnore">List of strings, each item that contains any of the strings in its name will be removed</param>
/// <param name="items">Items to filter</param>
let filterItems (idsToIgnore: string list) (titlesToIgnore: string list) (items: Outputs.Audiobook list) =
    items
    |> List.where (fun album ->
            (idsToIgnore |> List.contains album.Id = false) &&
            (not <| (titlesToIgnore |> List.exists (fun toIgnore -> album.Name.Contains(toIgnore))))
        )


let createSpotifyClient clientId clientSecret : TaskResult<SpotifyClient, string> =
    taskResult {
        try
            let config = SpotifyClientConfig.CreateDefault()
            let request = ClientCredentialsRequest(clientId, clientSecret)
            let! response = OAuthClient(config).RequestToken(request)
            let spotify = SpotifyClient(config.WithToken(response.AccessToken))
            return spotify
        with ex ->
            return! Error $"Authenticating with Spotify failed because: {ex.Message}"
    }


let getAlbumsForArtist (client: SpotifyClient) (artistId: string) =
    taskResult {
        let! firstPageOfAlbums = performRateLimitAwareRequest (fun () -> artistId |> client.Artists.GetAlbums)
        let! allAlbums = firstPageOfAlbums |> client.PaginateAll |> Task.map List.ofSeq
        return allAlbums
    }
    

let rateLimitAwareHttpRequest (url: string) : Task<Result<IO.Stream, string>> =
    let retryLimit = 5
    
    let getRetryAfterFromResponse (response: HttpResponseMessage) : TimeSpan =
        if response.Headers.RetryAfter.Delta.HasValue then
            response.Headers.RetryAfter.Delta.Value
        else
            TimeSpan.FromSeconds(5L)
    
    let tryToStream (url: string) : Task<StreamResult> =
        url
        |> httpClient.GetAsync
        |> Task.map (fun response ->
                let statusCode = response.StatusCode |> int
                if statusCode >= 200 && statusCode < 300 then
                    response.Content.ReadAsStream() |> Opened
                else if statusCode = 429 then
                    response |> getRetryAfterFromResponse |> RateLimited
                else
                    $"Got %i{statusCode} response" |> Failed)
        
    
    let rec step (counter: int) : Task<Result<IO.Stream, string>> =
        task {
            if counter > retryLimit then return (Error $"Exceeded retry limit of %i{retryLimit} while trying to download image")
            else
                match! url |> tryToStream with
                | Opened stream -> return Ok stream
                | RateLimited delay ->
                    let! _ = delay |> Task.Delay
                    return! step (counter + 1)
                | Failed error ->
                    return Error error
        }
    step 1
    

let ensureImageDimensionsAreSet (image: Image) : Task<Result<Image, string>> =
    taskResult {
        try
            if (image.Width = 0 || image.Height = 0) then
                let! rawImage = image.Url |> rateLimitAwareHttpRequest
                let! remoteImage = SixLabors.ImageSharp.Image.LoadAsync(rawImage)
                do image.Width <- remoteImage.Width
                do image.Height <- remoteImage.Height
                return image
            else
                return image
        with
        | exn -> return! Error $"Could not retrieve image from '%s{image.Url}' because: %s{exn.Message}"
    }


let ensureAllImageDimensionsAreSet (playlist: FullPlaylist) : Task<Result<FullPlaylist, string>> =
    taskResult {
        let images = playlist.Images |> List.ofSeq
        let imageUpdateTasks = images |> List.map ensureImageDimensionsAreSet
        let! updatedImages = imageUpdateTasks |> List.sequenceTaskResultM
        do playlist.Images <- System.Collections.Generic.List<Image>(updatedImages)
        return playlist
    }

    
let retrieveDataForPlaylist (client: SpotifyClient) (source: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    taskResult {
        try
            let! uncheckedPlaylist = performRateLimitAwareRequest (fun () -> source.ContentId |> client.Playlists.Get)
            let! playlist = uncheckedPlaylist |> ensureAllImageDimensionsAreSet
            let! allTracks = performRateLimitAwareRequest (fun () -> client.PaginateAll(playlist.Tracks) |> Task.map List.ofSeq)
            
            let playlistTracks =
                    allTracks
                    |> List.map (fun element ->
                        match element.Track with
                        | :? FullTrack as track -> Some track
                        | :? FullEpisode -> failwith "Found an episode in a playlist. This might be valid but is not supported currently"
                        | other -> failwith $"Found an unknown type if IPlayableItem: ${other.GetType().FullName}")
                    |> List.choose id

            let albums = playlistTracks |> List.map _.Album |> List.distinctBy _.Id
            let mappedAlbums = albums |> List.map mapSimpleAlbumToItemOutput
            let filteredAlbums = mappedAlbums |> (filterItems source.IgnoreIds source.ItemNameFilter)
            return
                { Intermediate.Artist.Id = playlist.Id; Intermediate.Artist.Name = playlist.Name; Intermediate.Artist.Images = playlist.Images |> List.ofSeq |> List.map mapSpotifyImageToImage },
                filteredAlbums
        with ex ->
            return! Error $"Could not retrieve data from Spotify for playlist {source.ContentId} because: {ex.Message}"
    } 

    
let retrieveDataForShow (client: SpotifyClient) (source: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    taskResult {
        try
            let! show = source.ContentId |> client.Shows.Get
            let! allEpisodes = performRateLimitAwareRequest (fun () -> client.PaginateAll(show.Episodes) |> Task.map List.ofSeq)
            let filteredEpisodes =
                    allEpisodes
                    |> List.map (fun episode ->
                        {
                            Outputs.Audiobook.Id = episode.Id
                            Outputs.Audiobook.Name = episode.Name
                            Outputs.Audiobook.UrlToOpen = episode.ExternalUrls["spotify"]
                            Outputs.Audiobook.Images = episode.Images |> Seq.map mapSpotifyImageToImage |> List.ofSeq
                        })
                    |> filterItems source.IgnoreIds source.ItemNameFilter
            return
                { Intermediate.Artist.Id = show.Id; Intermediate.Artist.Name = show.Name; Intermediate.Artist.Images = show.Images |> List.ofSeq |> List.map mapSpotifyImageToImage },
                filteredEpisodes
            
        with ex ->
            return! Error $"Could not retrieve data from Spotify for show {source.ContentId} because: {ex.Message}"
    }


let mapArtistToOutputArtist (artist: FullArtist) : Intermediate.Artist =
    {
        Id = artist.Id
        Name = artist.Name
        Images = artist.Images |> List.ofSeq |> List.map mapSpotifyImageToImage
    }


let retrieveDataForArtist (client: SpotifyClient) (source: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    taskResult {
        let! artistDetails = performRateLimitAwareRequest (fun () -> source.ContentId |> client.Artists.Get)
        let mappedArtist = artistDetails |> mapArtistToOutputArtist
        let! allAlbums = source.ContentId |> getAlbumsForArtist client
        let filteredAlbums =
            allAlbums
            |> List.map mapSimpleAlbumToItemOutput
            |> filterItems source.IgnoreIds source.ItemNameFilter
        
        return 
            mappedArtist,
            filteredAlbums
    }
    

let retriever (config: SpotifyConfig) (s: Inputs.Source)  : TaskResult<Intermediate.Artist * Outputs.Audiobook list, string> =
    let mutable client: SpotifyClient option = None
    taskResult {
        if client.IsNone then
            let! c = createSpotifyClient config.ClientId config.ClientSecret
            do client <- Some c
        match s.Type.ToLowerInvariant() with
        | "artist" ->
            return! retrieveDataForArtist client.Value s
        | "playlist" ->
            return! retrieveDataForPlaylist client.Value s
        | "show" ->
            return! retrieveDataForShow client.Value s
        | unmatched -> return! Error $"Source type '%s{unmatched}' is not implemented for Spotify"
    }
    
    //System.Threading.Tasks.Task.FromResult(List.empty<Outputs.Item>)