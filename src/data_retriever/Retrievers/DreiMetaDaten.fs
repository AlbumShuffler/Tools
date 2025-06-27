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

type DdfKSerie = {
    Kids: DreiMetadatenAlbum list
}

[<Literal>]
let private regularDdfJsonUrl = "https://dreimetadaten.de/data/Serie.json"
[<Literal>]
let private specialDdfJsonUrl = "https://dreimetadaten.de/data/Spezial.json"
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
                    do cache <- regulars @ specials
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
    let id =
        album.Ids.Spotify
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
    }