module AlbumShuffler.DataRetriever.Retrievers.Tidal

open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open System.Text
open AlbumShuffler.Shared
open FsToolkit.ErrorHandling


type TidalConfig = {
    ClientId: string
    ClientSecret: string
}

type TidalResponseWrapper<'a, 'b> = {
    Data: 'a
    Included: 'b list option
    // we ignore the links property
}

type TidalArtistAttributes = {
    Name: string
}

type TidalArtist = {
    Id: string
    Attributes: TidalArtistAttributes
}

type NestedProfileArt = {
    Attributes: {| 
        Files: {|
            Href: string
            Meta: {| Width: int; Height: int |} 
        |} list 
    |}
}

type TidalAlbumCoverArt = {
    Id: string
    Attributes: {|
        Files: {|
            Href: string
            Meta: {| Width: int; Height: int |} 
        |} list
    |}
    Relationships: {| Owners: {| Links: {| Self: string |} |} |} 
}

type TidalAlbum = {
    Id: string
    Attributes: {|
        Title: string
        ExternalLinks: {|
            Href: string
        |} list
    |}
    Relationships: {|
        CoverArt: {|
            Data: {|
                Id: string
            |} list
        |}
    |}
}

type TidalAlbumPayload
    = TidalAlbumCoverArt of TidalAlbumCoverArt
    | TidalAlbum of TidalAlbum

 
[<Literal>]
let DefaultCountryCode = "DE"

type AccessToken = {
    Scope: string
    [<JsonPropertyName("token_type")>]
    TokenType: string
    [<JsonPropertyName("access_token")>]
    AccessToken: string
    [<JsonPropertyName("expires_in")>]
    ExpiresIn: int
}

let private client = new HttpClient()

/// <summary>
/// Uses the given configuration to get an access token from the api.
/// </summary>
let getAccessToken (config: TidalConfig) : TaskResult<AccessToken, string> = 
    taskResult {
        try
            let credentials = $"{config.ClientId}:{config.ClientSecret}"
            let b64Credentials = 
                credentials
                |> Encoding.UTF8.GetBytes
                |> Convert.ToBase64String

            use request = new HttpRequestMessage(HttpMethod.Post, "https://auth.tidal.com/v1/oauth2/token")
            request.Headers.Add("Authorization", $"Basic {b64Credentials}")

            let content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
            request.Content <- content

            let! response = client.SendAsync(request)
            let! responseContent = response.Content.ReadAsStringAsync()
            return!
                responseContent
                |> System.Text.Json.JsonSerializer.Deserialize<AccessToken>
                |> Ok
        with
        | exn -> return! exn.Message |> Error
    }


/// <summary>
/// Creates a bare request to be used later on
/// </summary>
let createBaseRequestMessage (accessToken: AccessToken) (url: string) : HttpRequestMessage =
    let request = new HttpRequestMessage(HttpMethod.Get, url)
    request.Headers.Add("accept", "application/vnd.api+json")
    request.Headers.Add("Authorization", $"Bearer {accessToken.AccessToken}")
    request


/// <summary>
/// Adds the given payloas as serialized json to the given request
/// </summary>
/// <returns>
/// Ok case if the content was serialized and added to the request. Error otherwise
/// </returns>
let addJsonBodyToRequest<'T> (request: HttpRequestMessage) (payload: 'T) : Result<HttpRequestMessage, string> =
    try
        let jsonString = System.Text.Json.JsonSerializer.Serialize(payload)
        request.Content <- new StringContent(jsonString, Encoding.UTF8, "application/vnd.tidal.v1+json")
        request.Method <- HttpMethod.Post
        Ok request
    with
    | exn -> Error exn.Message


/// <summary>
/// Performs a rate limit aware request. Takes a request factory (not a request) and launches continuous request until
/// a non-rate-limit error occurs, the request succeeds or the retry limit is exceeded
/// </summary>
/// <remarks>
/// Uses a request factory instead of a request because the latter can only be sent once
/// </remarks>
let performRateLimitAwareRequest<'a> (requestFactory: unit -> HttpRequestMessage) : TaskResult<string, string> =
    let maximumNumberOfRetries = 20
    let defaultRetryDelay = TimeSpan.FromSeconds(5L)
    
    let rec step counter : TaskResult<string, string> =
        task {
            if counter >= maximumNumberOfRetries then
                return Error $"Exceeded retry count limit (%i{maximumNumberOfRetries}) while querying Tidal"
            else
                let! response = client.SendAsync(requestFactory ())
                let shouldRetry = (response.StatusCode |> int) = 429
                if shouldRetry then
                    let retryDelay =
                        if response.Headers.RetryAfter.Delta.HasValue then response.Headers.RetryAfter.Delta.Value
                        else defaultRetryDelay
                    do! Task.Delay(retryDelay)
                    return! step (counter + 1)
                else if response.IsSuccessStatusCode = false then  
                    let! body = 
                        try
                            response.Content.ReadAsStringAsync ()
                        with 
                        | exn -> Task.FromResult($"Error reading response body: %s{exn.Message}")
                    return Error $"Encountered %A{response.StatusCode}: %s{body}" 
                else
                    let! body = response.Content.ReadAsStringAsync ()
                    return body |> Ok
        }
    step 1


/// <summary>
/// Retrieves all artist information (including cover art) for the given artist
/// </summary>
let getArtistDetails (accessToken: AccessToken) (artistId: string) : TaskResult<Intermediate.Artist, string> =
    taskResult {
        let requestFactory =
            fun () -> 
                $"https://openapi.tidal.com/v2/artists/%s{artistId}?countryCode=%s{DefaultCountryCode}&include=profileArt" 
                |> createBaseRequestMessage accessToken

        let! response =
            requestFactory
            |> performRateLimitAwareRequest
            
        return!
            match response |> Json.tryDeserialize<TidalResponseWrapper<TidalArtist, NestedProfileArt>> with
            | Ok p ->
                let images = 
                    p.Included 
                    |> Option.map List.exactlyOne 
                    |> Option.map (fun profileArt -> profileArt.Attributes.Files |> List.map (fun i -> { AlbumShuffler.Shared.Outputs.Image.Width = i.Meta.Width; AlbumShuffler.Shared.Outputs.Image.Height = i.Meta.Height; AlbumShuffler.Shared.Outputs.Image.Url = i.Href })) 
                    |> Option.defaultValue [] 
                Ok { Intermediate.Artist.Name = p.Data.Attributes.Name; Intermediate.Id = p.Data.Id; Intermediate.Images = images }
            | Error e -> Error e
    }
       

/// <summary>
/// Extracts the next cursor from the given document. Returns `None` if there is none
/// </summary>
let extractNextCursor (doc: JsonDocument) : Option<string> =
    try
        let root = doc.RootElement

        let nextCursor = 
            root
                .GetProperty("links")
                .GetProperty("meta") 
                .GetProperty("nextCursor")
                .GetString()

        if String.IsNullOrEmpty(nextCursor) then
            None
        else
            Some nextCursor
    with
    | _ -> None
       
       
/// <summary>
/// Checks whether the given document represents an album or an artwork and deserializes the contents accordingly.
/// Return value is wrapped in a union type
/// </summary> 
let extractAlbumPayload (doc: JsonDocument) =
    let included = doc.RootElement.GetProperty("included")
    let options = Json.serializerOptions 
    [ for element in included.EnumerateArray () do
            match element.GetProperty("type").GetString() with
            | "albums" -> element.Deserialize<TidalAlbum>(options) |> TidalAlbum
            | "artworks" -> element.Deserialize<TidalAlbumCoverArt>(options) |> TidalAlbumCoverArt
            | _ -> failwith "Could not extract type from json payload element" ]
 
       
/// <summary>
/// Retrieves all payload from a single call to the Tidal api.
/// Includes albums as well as their cover art 
/// </summary>
let getPayloadForCursor (accessToken: AccessToken) (artistId: string) (maybeCursor: string option) : TaskResult<(TidalAlbumPayload list * string option), string> =
    taskResult {
        let requestFactory =
            fun () ->
                match maybeCursor with
                | Some cursor -> $"https://openapi.tidal.com/v2/artists/%s{artistId}/relationships/albums?countryCode=%s{DefaultCountryCode}&include=albums&include=albums.coverArt&page[cursor]=%s{cursor}"
                | None -> $"https://openapi.tidal.com/v2/artists/%s{artistId}/relationships/albums?countryCode=%s{DefaultCountryCode}&include=albums&include=albums.coverArt"
                |> createBaseRequestMessage accessToken 
    
        let! response =
            requestFactory
            |> performRateLimitAwareRequest
            
        use json = JsonDocument.Parse(response)
        let nextCursor = json |> extractNextCursor 
        return (json |> extractAlbumPayload, nextCursor)
    }
        
/// <summary>
/// Repeatedly calls the Tidal api to retrieve all albums (and cover art) for a single artist.
/// Returns the raw Tidal data, nothing is transformed
/// </summary>   
let rec getAllPayloadForArtistAlbums (accessToken: AccessToken) (artistId: string) (currentCursor: string option) (accumulator: TidalAlbumPayload list) : TaskResult<TidalAlbumPayload list, string> =
    taskResult { 
        let! (newItems, next) = getPayloadForCursor accessToken artistId currentCursor
        if next.IsSome then return! getAllPayloadForArtistAlbums accessToken artistId next (newItems @ accumulator)
        else return accumulator 
    } 
           
           
/// <summary>
/// Splits a list into two lists based on a splitter function. The splitter must produce either a 'b or a 'c value and fail otherwise
/// </summary>
let separate (splitter: 'a -> ('b option * 'c option)) (items: 'a list) : ('b list * 'c list) =
    let rec step (remaining: 'a list) (bb: 'b list) (cc: 'c list) =
        match remaining with
        | [] -> (bb, cc)
        | head :: tail ->
               match head |> splitter with
               | None, None -> failwith "Splitter only produced None values!"
               | Some _, Some _ -> failwith "Splitter produces to Some values!"
               | Some b, None -> step tail (b :: bb) cc
               | None, Some c -> step tail bb (c :: cc) 
    step items [] [] 


/// <summary>
/// Combines a Tidal album with Tidal cover art to create a provider-agnostic audiobook
/// </summary>  
let mapIntoAudiobook (album: TidalAlbum) (coverArt: TidalAlbumCoverArt) : Outputs.Audiobook =
    if album.Relationships.CoverArt.Data.IsEmpty then failwith $"There is no cover art relationship for %s{album.Attributes.Title}"
    else if album.Relationships.CoverArt.Data.Head.Id <> coverArt.Id then failwith $"The cover art relationship ids (album: %s{album.Relationships.CoverArt.Data.Head.Id} - cover: %s{coverArt.Id})does not match for %s{album.Attributes.Title}"
    else 
        let images = coverArt.Attributes.Files |> List.map (fun c -> { Outputs.Image.Height = c.Meta.Height; Outputs.Image.Width = c.Meta.Width; Outputs.Image.Url = c.Href }) 
        {
            Outputs.Audiobook.Id = album.Id
            Outputs.Audiobook.Name = album.Attributes.Title 
            Outputs.Audiobook.UrlToOpen = album.Attributes.ExternalLinks.Head.Href
            Outputs.Audiobook.Images = images 
        } 
                          
        
/// <summary>
/// Returns all audiobooks for a single artist. Includes the albums' cover art
/// </summary> 
let rec getArtistAlbums (accessToken: AccessToken) (artistId: string) : TaskResult<Outputs.Audiobook list, string> =
    taskResult {
        let! payload = getAllPayloadForArtistAlbums accessToken artistId None []
        let albumsWithoutCover, coverArt =
            payload |> separate (function TidalAlbum a -> (Some a, None) | TidalAlbumCoverArt c -> (None, Some c))
    
        return
            List.zip 
                (albumsWithoutCover |> List.sortBy (fun a -> a.Relationships.CoverArt.Data.Head.Id))
                (coverArt |> List.sortBy _.Id)
            |> List.map (fun (album, cover) -> mapIntoAudiobook album cover)
    }


let retriever (config: TidalConfig) (s: Inputs.Source) : TaskResult<Intermediate.Artist * Outputs.Audiobook list, string> =
    taskResult {
        if String.Equals(s.Type, "artist", StringComparison.InvariantCultureIgnoreCase) = false then
            return failwith $"The Tidal retriever only supports artists and not '%s{s.Type}'"
        else
            let! accessToken = config |> getAccessToken
            let! artist = getArtistDetails accessToken s.ContentId
            let! albums = getArtistAlbums accessToken s.ContentId
            return (artist, albums)
    }