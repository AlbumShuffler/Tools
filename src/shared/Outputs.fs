module AlbumShuffler.Shared.Outputs

/// <summary>
/// Generic image class
/// </summary>
type Image = {
    Url: string
    Width: int
    Height: int
}

/// <summary>
/// Provider in its output configuration. No longer has fields for filters
/// </summary>
type Provider = {
    Id: string
    Name: string
    Icon: string
    Logo: string
}
let createProvider id name icon logo =
    { Id = id; Name = name; Icon = icon; Logo = logo }

/// <summary>
/// This represents a single playable item that will later be displayed.
/// Can be constructed from show episodes, albums and playlist entries
/// </summary>
type Audiobook = {
    Id: string
    Name: string
    UrlToOpen: string
    Images: Image list
}


/// <summary>
/// Content is an abstract definition of something that content that is available via multiple sources
/// </summary>
type ArtistInfo = {
    Id: string
    Name: string
    ShortName: string
    Icon: string
    Images: Image list
    CoverCenterX: int
    CoverCenterY: int
    AltCoverCenterX: int option
    AltCoverCenterY: int option
    CoverColorA: string
    CoverColorB: string
}

/// <summary>
/// Contains all audiobooks for a specific provider (Spotify, Deezer, ...)
/// </summary>
type Output = {
    // we do not use DateTimeOffset here because the web app simply displays this value with a timezone hint 
    CreationDate: System.DateTime 
    Provider: Provider
    Audiobooks: (ArtistInfo * Audiobook list) list
}
