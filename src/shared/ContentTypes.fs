module AlbumShuffler.Shared.ContentTypes

type ContentType
    = Artist
    | Playlist
    | Show


[<Literal>]
let artistAsString = nameof(Artist)


let toString = function
    | Artist -> nameof(Artist).ToLowerInvariant()
    | Playlist -> nameof(Playlist).ToLowerInvariant()
    | Show -> nameof(Show).ToLowerInvariant()
    
    
let fromString (s: string) =
    let s = s.ToLowerInvariant()
    if s = nameof(Artist).ToLowerInvariant() then Artist
    else if s = nameof(Playlist).ToLowerInvariant() then Playlist
    else if s = nameof(Show).ToLowerInvariant() then Show
    else failwithf "Unkown value for ContentType: %s" s
    
    
    