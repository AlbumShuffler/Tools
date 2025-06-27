module AlbumShuffler.DataRetriever.Retrievers.Deezer

open System
open System.Net.Http
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Threading.Tasks
open System.Web
open FsToolkit.ErrorHandling
open AlbumShuffler.Shared


module RetrievalResults =
    type DeezerErrorDetail = {
        Type: string
        Message: string
        Code: int
    }

    type DeezerErrorResponse = {
        Error: DeezerErrorDetail
    }
    
    type ApiError =
        | Quota
        | ItemsLimitExceeded
        | Permission
        | TokenInvalid
        | Parameter
        | ParameterMissing
        | QueryInvalid
        | ServiceBusy
        | DataNotFound
        | IndividualAccountNotAllowed
        | Unknown of string
        
    let apiErrorToString (error: ApiError) : string =
        match error with
        | Quota -> "Quota exceeded"
        | ItemsLimitExceeded -> "Too many items"
        | Permission -> "Permission denied"
        | TokenInvalid -> "Invalid token"
        | Parameter -> "Invalid parameter"
        | ParameterMissing -> "Missing parameter"
        | QueryInvalid -> "Invalid query"
        | ServiceBusy -> "Service is busy"
        | DataNotFound -> "Data not found"
        | IndividualAccountNotAllowed -> "Individual accounts not allowed"
        | ApiError.Unknown s -> s

    type RetrievalResult<'a> =
        | Response of 'a
        | ApiError of ApiError
        | InvalidJson of string
        | RetriesExceeded
        | TwinResult of left:RetrievalResult<'a> * right:RetrievalResult<'a>
        | Unknown of string
        
    let rec asString (r: RetrievalResult<_>) =
        match r with
        | Response response -> $"%A{response}"
        | ApiError error -> error |> apiErrorToString
        | InvalidJson error -> error
        | RetriesExceeded -> "Exceeded retry counter while trying to retrieve data"
        | TwinResult (left, right) -> $"{left |> asString} | {right |> asString}"
        | Unknown error -> error

    let rec map (f: 'a -> 'b) (result: RetrievalResult<'a>) : RetrievalResult<'b> =
        match result with
        | RetrievalResult.Response value -> value |> f |> Response
        | ApiError e -> ApiError e
        | InvalidJson e -> InvalidJson e
        | RetriesExceeded -> RetriesExceeded
        | TwinResult (left, right) -> TwinResult (left |> map f, right |> map f)
        | Unknown e -> Unknown e
        
    let rec bind (f: 'a -> RetrievalResult<'b>) (result: RetrievalResult<'a>) : RetrievalResult<'b> =
        match result with
        | ApiError e -> ApiError e
        | InvalidJson e -> InvalidJson e
        | Response r -> r |> f
        | RetriesExceeded -> RetriesExceeded
        | TwinResult (left, right) -> TwinResult (left |> bind f, right |> bind f)
        | Unknown e -> Unknown e


    let errorResponseAsApiError (error: DeezerErrorResponse) : ApiError =
        match error.Error.Code with
        | 4   -> Quota
        | 100 -> ItemsLimitExceeded
        | 200 -> Permission
        | 300 -> TokenInvalid
        | 500 -> Parameter
        | 501 -> ParameterMissing
        | 600 -> QueryInvalid
        | 700 -> ServiceBusy
        | 800 -> DataNotFound
        | 901 -> IndividualAccountNotAllowed
        | _ -> ApiError.Unknown error.Error.Message


type DeezerArtist = {
    Id: int64
    Name: string
    [<JsonPropertyName("picture_small")>]
    PictureSmall: string
    [<JsonPropertyName("picture_medium")>]
    PictureMedium: string
    [<JsonPropertyName("picture_big")>]
    PictureBig: string
    [<JsonPropertyName("picture_xl")>]
    PictureXl: string
    [<JsonPropertyName("nb_album")>]
    NumberOfAlbums: int64
}


type DeezerAlbum = {
    Id: int64
    Title: string
    Link: string
    [<JsonPropertyName("cover_small")>]
    CoverSmall: string
    [<JsonPropertyName("cover_medium")>]
    CoverMedium: string
    [<JsonPropertyName("cover_big")>]
    CoverBig: string
    [<JsonPropertyName("cover_xl")>]
    CoverXl: string
}


/// <summary>
/// Condensed version of the album data structure.
/// Is returned when retrieving items from a playlist
/// </summary>
type DeezerTrackAlbum = {
    Id: int64
    Title: string
    [<JsonPropertyName("cover_small")>]
    CoverSmall: string
    [<JsonPropertyName("cover_medium")>]
    CoverMedium: string
    [<JsonPropertyName("cover_big")>]
    CoverBig: string
    [<JsonPropertyName("cover_xl")>]
    CoverXl: string
}


/// <summary>
/// A track is part of a playlist
/// </summary>
type DeezerTrack = {
    Id: int64
    Title: string
    Album: DeezerTrackAlbum
}


type DeezerTracksResponse = {
    Data: DeezerTrack list
    Total: int64 option
    Next: string option
}

type DeezerPlaylist = {
    Id: int64
    Title: string
    [<JsonPropertyName("nb_tracks")>]
    NumberOfTracks: int64
    [<JsonPropertyName("picture_small")>]
    PictureSmall: string
    [<JsonPropertyName("picture_medium")>]
    PictureMedium: string
    [<JsonPropertyName("picture_big")>]
    PictureBig: string
    [<JsonPropertyName("picture_xl")>]
    PictureXl: string
    Tracks: DeezerTracksResponse
}

type DeezerAlbumResponse = {
    Data: DeezerAlbum list
    Total: int64
    Next: string option
}

let client = new HttpClient()

let private imageSizeRegex = @"\b\d{2,4}x\d{2,4}\b"

let mapImageFromImageUrl (sourceName: string) (imageUrl: string) : Result<Outputs.Image, string> =
    if (imageUrl |> String.IsNullOrWhiteSpace) then
        let text = sourceName |> HttpUtility.UrlEncode
        Ok {
            Outputs.Image.Height = 512
            Outputs.Image.Width = 512
            Outputs.Image.Url = $"https://placehold.co/512x512?text=%s{text}"
        }
    else
        let regexMatch =
            Regex.Match(imageUrl, imageSizeRegex)
        
        if regexMatch.Success then
            regexMatch
                .Value.Split('x')
            |> Array.map Int32.Parse
            |> (function
               | [|w;h|] -> { Outputs.Image.Url = imageUrl; Outputs.Image.Width = w; Outputs.Image.Height = h } |> Ok
               | _ -> (Error $"Could not determine image size for %s{sourceName} from Deezer from url: %s{imageUrl}"))
        else
            Error $"The imageUrl '%s{imageUrl}'"


let mapImagesFromArtistDetails (artist: DeezerArtist) =
    let sizeUrls = [
        artist.PictureSmall
        artist.PictureMedium
        artist.PictureBig
        artist.PictureXl
    ]
    sizeUrls
    |> List.map (mapImageFromImageUrl artist.Name)
    |> List.sequenceResultM
    |> Result.mapError ((+) $"Error for artist '%s{artist.Name}' (%i{artist.Id}): ")


let mapImagesFromAlbum (album: DeezerAlbum) =
    let sizeUrls = [
        album.CoverSmall
        album.CoverMedium
        album.CoverBig
        album.CoverXl
    ]
    sizeUrls
    |> List.map (mapImageFromImageUrl album.Title)
    |> List.sequenceResultM
    |> Result.mapError ((+) $"Error for album '%s{album.Title}' (%i{album.Id}): ")
    
    
let mapImagesFromTrack (track: DeezerTrack) =
    let sizeUrls = [
        track.Album.CoverSmall
        track.Album.CoverMedium
        track.Album.CoverBig
        track.Album.CoverXl
    ]
    sizeUrls
    |> List.map (mapImageFromImageUrl track.Title)
    |> List.sequenceResultM
    |> Result.mapError ((+) $"Error for album '%s{track.Title}' (%i{track.Id}): ")
    
    
let mapImagesFromPlaylist (playlist: DeezerPlaylist) =
    let sizeUrls = [
        playlist.PictureSmall
        playlist.PictureMedium
        playlist.PictureBig
        playlist.PictureXl
    ]
    sizeUrls |> List.map (mapImageFromImageUrl playlist.Title) |> List.sequenceResultA


let tryParseAsError (response: string) : RetrievalResults.ApiError option =
    try
        let asError = response |> Json.deserialize<RetrievalResults.DeezerErrorResponse>
        asError |> RetrievalResults.errorResponseAsApiError |> Some
    with
    | _ -> None


let performRateLimitAwareRequest (url: string) : Task<RetrievalResults.RetrievalResult<string>> =
    let maximumNumberOfRetries = 20
    let retryDelay = TimeSpan.FromSeconds(2L)
    
    let rec step counter : Task<RetrievalResults.RetrievalResult<string>> =
        task {
            if counter >= maximumNumberOfRetries then return RetrievalResults.RetrievalResult.RetriesExceeded
            else
                let! response = url |> client.GetStringAsync
                match response |> tryParseAsError with
                | Some RetrievalResults.Quota ->
                    do! Task.Delay(retryDelay)
                    return! step (counter + 1)
                | Some otherError ->
                    return (otherError |> RetrievalResults.RetrievalResult.ApiError)
                | None ->
                    return response |> RetrievalResults.RetrievalResult.Response
        }
    step 1
    
    
let performRateLimitAwareRequestAndDeserialize<'a> (url: string) : Task<RetrievalResults.RetrievalResult<'a>> =
    url |> performRateLimitAwareRequest
        |> Task.map (fun x ->
            x |> RetrievalResults.bind (fun response ->
                try
                    response |> Json.deserialize<'a> |> RetrievalResults.RetrievalResult.Response
                with
                | ex ->
                    let message = $"{ex.Message}{Environment.NewLine}{response}"
                    (message |> RetrievalResults.RetrievalResult.InvalidJson)
            ))


let retrieveArtistDetails (artistId: string) =
    let url = $"https://api.deezer.com/artist/{artistId}"
    url |> performRateLimitAwareRequestAndDeserialize<DeezerArtist>


let retrieveAlbumDataForArtist (artistId: string) : Task<RetrievalResults.RetrievalResult<DeezerAlbum list>> =
    let rec step (url: string option) (acc: RetrievalResults.RetrievalResult<DeezerAlbum list>) : Task<RetrievalResults.RetrievalResult<DeezerAlbum list>> =
        task {
            match url with
            | Some u ->
                match! u |> performRateLimitAwareRequestAndDeserialize<DeezerAlbumResponse> with
                | RetrievalResults.Response result ->
                    let newAcc = acc |> RetrievalResults.map (fun oldAcc -> result.Data @ oldAcc)
                    return! step result.Next newAcc
                | RetrievalResults.ApiError error ->
                    return RetrievalResults.ApiError error
                | RetrievalResults.InvalidJson error ->
                    return RetrievalResults.InvalidJson error
                | RetrievalResults.RetriesExceeded ->
                    return RetrievalResults.RetriesExceeded
                | RetrievalResults.TwinResult (left, right) ->
                    return failwith "not implemented since its unnecessary"
                | RetrievalResults.RetrievalResult.Unknown error ->
                    return RetrievalResults.RetrievalResult.Unknown error
            | None ->
                return acc
        }
    step (Some $"https://api.deezer.com/artist/{artistId}/albums") (RetrievalResults.Response [])
    
    
let mapDeezerAlbumToOutputAudiobook (album: DeezerAlbum) : Result<Outputs.Audiobook, string> =
    album
    |> mapImagesFromAlbum
    |> Result.map (fun images ->
        {
            Outputs.Audiobook.Id = album.Id |> string
            Outputs.Audiobook.Name = album.Title
            Outputs.Audiobook.UrlToOpen = album.Link
            Outputs.Audiobook.Images = images
        })
    
    
let mapDeezerTrackToOutputAudiobook (track: DeezerTrack) : Result<Outputs.Audiobook, string> =
    let albumIdToAlbumUrl (albumId: int64) : string =
        $"https://www.deezer.com/album/%i{albumId}"
    
    track
    |> mapImagesFromTrack
    |> Result.map (fun images ->
        {
            Outputs.Audiobook.Id = track.Album.Id |> string
            Outputs.Audiobook.Name = track.Album.Title
            Outputs.Audiobook.UrlToOpen = track.Album.Id |> albumIdToAlbumUrl
            Outputs.Audiobook.Images = images
        })
    
    
let retrieveDataForArtist (artistId: string) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    task {
        let! artistDetailsResult = (artistId |> retrieveArtistDetails)
        let! albumsResult = (artistId |> retrieveAlbumDataForArtist)

        match artistDetailsResult, albumsResult with
        | RetrievalResults.Response artist, RetrievalResults.Response albums ->
            let artist =
                artist
                |> mapImagesFromArtistDetails
                |> Result.map (fun images -> {
                        Intermediate.Artist.Id = artist.Id |> string
                        Intermediate.Artist.Images = images
                        Intermediate.Artist.Name = artist.Name
                    })
                |> Result.mapError (fun errors -> String.Join(Environment.NewLine, errors))
                
            let audiobooks =
                albums
                |> List.map mapDeezerAlbumToOutputAudiobook
                |> List.sequenceResultA
                |> Result.mapError (fun errors -> String.Join(Environment.NewLine, errors))
                
            return (Result.zip artist audiobooks)
        | RetrievalResults.Response _, other ->
            return Error (other |> RetrievalResults.asString)
        | other, RetrievalResults.Response _ ->
            return Error (other |> RetrievalResults.asString)
        | errorA, errorB ->
            return Error (
                (errorA |> RetrievalResults.asString) +
                Environment.NewLine +
                (errorB |> RetrievalResults.asString))
    }
    

let retrievePlaylistDetails (playlistId: string) : Task<RetrievalResults.RetrievalResult<DeezerPlaylist>> =
    $"https://api.deezer.com/playlist/%s{playlistId}"
    |> performRateLimitAwareRequestAndDeserialize<DeezerPlaylist>
    
    
let retrieveAllPlaylistTracks (playlistId: string) : Task<RetrievalResults.RetrievalResult<DeezerTrack list>> =
    let rec step (nextUrl: string option) (acc: RetrievalResults.RetrievalResult<DeezerTrack list>) =
        task {
            match nextUrl with
            | Some url ->
                match! url |> performRateLimitAwareRequestAndDeserialize<DeezerTracksResponse> with
                | RetrievalResults.Response result ->
                    let newAcc = acc |> RetrievalResults.map (fun oldAcc -> result.Data @ oldAcc)
                    return! step result.Next newAcc
                | RetrievalResults.ApiError error ->
                    return RetrievalResults.ApiError error
                | RetrievalResults.InvalidJson error ->
                    return RetrievalResults.InvalidJson error
                | RetrievalResults.RetriesExceeded ->
                    return RetrievalResults.RetriesExceeded
                | RetrievalResults.TwinResult (_, _) ->
                    return failwith "not implemented since its unnecessary"
                | RetrievalResults.RetrievalResult.Unknown error ->
                    return RetrievalResults.RetrievalResult.Unknown error                
            | None ->
                return acc
        }
    step (Some $"https://api.deezer.com/playlist/%s{playlistId}/tracks") (RetrievalResults.Response [])
    

let retrieveDataForPlaylist (playlistId: string) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =   
    task {
        let playlistResult = (playlistId |> retrievePlaylistDetails).GetAwaiter().GetResult()
        let tracksResult = (playlistId |> retrieveAllPlaylistTracks).GetAwaiter().GetResult()
        
        match playlistResult, tracksResult with
        | RetrievalResults.Response playlist, RetrievalResults.Response tracks ->
            let artist =
                playlist
                |> mapImagesFromPlaylist
                |> Result.map (fun images ->
                    {
                        Intermediate.Artist.Id = playlist.Id |> string
                        Intermediate.Artist.Name = playlist.Title
                        Intermediate.Artist.Images = images
                    })
                |> Result.mapError (fun errors -> String.Join(Environment.NewLine, errors))
            let audiobooks =
                tracks
                |> List.distinctBy _.Album.Id
                |> List.map mapDeezerTrackToOutputAudiobook
                |> List.sequenceResultA
                |> Result.mapError(fun errors -> String.Join(Environment.NewLine, errors))
            return (Result.zip artist audiobooks)
        | RetrievalResults.Response _, other ->
            return Error (other |> RetrievalResults.asString)
        | other, RetrievalResults.Response _ ->
            return Error (other |> RetrievalResults.asString)
        | errorA, errorB ->
            return Error (
                (errorA |> RetrievalResults.asString) +
                Environment.NewLine +
                (errorB |> RetrievalResults.asString))
    }


let retriever (s: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    let result =
        match s.Type with
        | "artist" -> retrieveDataForArtist s.ContentId
        | "playlist" -> retrieveDataForPlaylist s.ContentId
        | other -> failwith $"The Deezer retriever does not support the content type '%s{other}'"
    result
    |> TaskResult.map (fun (artist, books) ->
            let filteredBooks =
                books
                |> List.where (fun book -> not <| (s.IgnoreIds |> List.contains book.Id))
                |> List.where (fun book -> not <| (s.ItemNameFilter |> List.exists (fun toIgnore -> book.Name.ToLowerInvariant().Contains(toIgnore.ToLowerInvariant()))))
            artist, filteredBooks
        )
