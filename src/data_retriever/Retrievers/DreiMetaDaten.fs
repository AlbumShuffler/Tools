module AlbumShuffler.DataRetriever.Retrievers.DreiMetaDaten

open System
open System.Threading.Tasks
open AlbumShuffler.Shared
open FsToolkit.ErrorHandling

type Links = {
    AppleMusic: string option
    AmazonMusic: string option
    YouTubeMusic: string option
    Cover_Itunes: string option
}

type Ids = {
    Dreimetadaten: int
    AppleMusic: string option
    Spotify: string option
    Bookbeat: string option
    AmazonMusic: string option
    Amazon: string option
    YouTubeMusic: string option
}

type DreiMetadatenAlbum = {
    Nummer: int option
    Titel: string
    Links: Links
    Ids: Ids
}

type DdfSerie = {
    Serie: DreiMetadatenAlbum list
}

type DdfSpezial = {
    Spezial: DreiMetadatenAlbum list
}

type DdfShort = {
    Kurzgeschichten: DreiMetadatenAlbum list
}

type DdfKSerie = {
    Kids: DreiMetadatenAlbum list
}

[<Literal>]
let private regularDdfJsonUrl = "https://dreimetadaten.de/data/Serie.json"
[<Literal>]
let private specialDdfJsonUrl = "https://dreimetadaten.de/data/Spezial.json"
[<Literal>]
let private shortDdfJsonUrl = "https://dreimetadaten.de/data/Kurzgeschichten.json"
[<Literal>]
let private regularDdfKJsonUrl = "https://dreimetadaten.de/data/Kids.json"
[<Literal>]
let private DdfSpotifyId = "3meJIgRw7YleJrmbpbJK6S"
[<Literal>]
let private DdfkSpotifyId = "0vLsqW05dyLvjuKKftAEGA"

let private HttpClient = new System.Net.Http.HttpClient()


let downloadAndParse<'a> (mapper: 'a -> DreiMetadatenAlbum list) (url: string) =
    task {
        let! rawJson = url |> HttpClient.GetStringAsync
        return rawJson |> Json.deserialize<'a> |> mapper
    }
    
    
let getAllDdfAlbums =
    let mutable cache = []
    fun () ->
        task {
            try
                if cache.IsEmpty then
                    let! regulars = regularDdfJsonUrl |> downloadAndParse<DdfSerie> _.Serie
                    let! specials = specialDdfJsonUrl |> downloadAndParse<DdfSpezial> _.Spezial
                    let! shorts = shortDdfJsonUrl |> downloadAndParse<DdfShort> _.Kurzgeschichten
                    do cache <- regulars @ specials @ shorts
                return cache |> Ok
            with
            | exn -> return exn.Message |> Error
        }
    
    
let getAllDdfKAlbum =
    let mutable cache = []
    fun () ->
        task {
            try
                if cache.IsEmpty then
                    let! regulars= (regularDdfKJsonUrl |> downloadAndParse<DdfKSerie> _.Kids)
                    do cache <- regulars
                return cache |> Ok
            with
            | exn -> return exn.Message |> Error
        }
    
    
let albumAsContent (url: string) (album: DreiMetadatenAlbum) : Outputs.Audiobook =
    // We do not want to put workarounds in the `Outputs.Audiobook` record so we put
    // a workaround in the id field itself. It is filled with the id of the DMD project
    // (which is later required to properly match their sqlite database) and the Spotify
    // id that is used in a later step to add images from the Spotify cdn
    let id =
        album.Ids.Spotify
        |> Option.map (fun spotifyId -> $"%i{album.Ids.Dreimetadaten}||%s{spotifyId}")
        |> Option.defaultWith (fun () -> failwithf $"Audiobook '%s{album.Titel}' does not have a Spotify id")
        
    let images =
        album.Links.Cover_Itunes
        |> Option.map (fun link -> [{
                Outputs.Image.Url = link
                Outputs.Image.Height = 3000
                Outputs.Image.Width = 3000
            }])
        |> Option.defaultValue []

    {
        Id = id
        Name = album.Titel
        UrlToOpen = url
        Images = images
    }
    
    
// We do not want to take resources from the DMD project, so we hardcode the urls
// of the proper images from Spotify
let private DdfkImages : Outputs.Image list = [
    { Url = "https://i.scdn.co/image/ab6761610000e5eb4b258838de4271df70886fb8"
      Height = 640
      Width = 640 }
    { Url = "https://i.scdn.co/image/ab676161000051744b258838de4271df70886fb8"
      Height = 320
      Width = 320 }
    { Url = "https://i.scdn.co/image/ab6761610000f1784b258838de4271df70886fb8"
      Height = 160
      Width = 160 } ]


// We do not want to take resources from the DMD project, so we hardcode the urls
// of the proper images from Spotify
let private DdfImages : Outputs.Image list = [
      { Url = "https://i.scdn.co/image/ab6761610000e5eb7de827ab626c867816052015"
        Height = 640
        Width = 640 }
      { Url = "https://i.scdn.co/image/ab676161000051747de827ab626c867816052015"
        Height = 320
        Width = 320 }
      { Url = "https://i.scdn.co/image/ab6761610000f1787de827ab626c867816052015"
        Height = 160
        Width = 160 } ]

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

let retriever (source: Inputs.Source) : Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>> =
    taskResult {
        let urlSelector =
            match source.ProviderId with
            | "amazon_dmd" -> fun (a: DreiMetadatenAlbum) -> a.Links.AmazonMusic
            | "apple_dmd" -> fun (a: DreiMetadatenAlbum) -> a.Links.AppleMusic
            | "youtube_dmd" -> fun (a: DreiMetadatenAlbum) -> a.Links.YouTubeMusic
            | s -> failwithf $"The content source '%s{s}' is not supported"

        let! albums =
            match source.ContentId with
            | DdfSpotifyId -> getAllDdfAlbums()
            | DdfkSpotifyId -> getAllDdfKAlbum()
            | s -> failwithf $"The artist/playlist/show '%s{s}' is not supported"
            
        let name, images =
            match source.ContentId with
            | DdfSpotifyId -> "Die Drei ???", DdfImages
            | DdfkSpotifyId -> "Die Drei ??? Kids", DdfkImages
            | s -> failwithf $"The artist/playlist/show '%s{s}' is not supported"

        return
            { Id = source.ContentId; Name = name; Images = images },
            albums
            // The retrieved data contains metadata of releases that are not yet available as audiobooks
            |> List.choose (fun a ->
                a
                |> urlSelector
                |> Option.map (fun url -> albumAsContent url a)
            )
            |> (filterItems source.IgnoreIds source.ItemNameFilter)
    }