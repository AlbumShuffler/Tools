/// <summary>
/// Contains intermediate types that are neither used as in- or outputs
/// </summary>
module AlbumShuffler.Shared.Intermediate

type Artist = {
    Id: string
    Name: string
    Images: Outputs.Image list
}

let combineArtistAndInputContent (artist: Artist) (content: Inputs.Content) : Outputs.ArtistInfo =
    {
        Id = artist.Id
        Name = artist.Name
        ShortName = content.ShortName
        Icon = content.Icon
        CoverCenterX = content.CoverCenterX
        CoverCenterY = content.CoverCenterY
        AltCoverCenterX = content.AltCoverCenterX
        AltCoverCenterY = content.AltCoverCenterY
        CoverColorA = content.CoverColorA
        CoverColorB = content.CoverColorB
        Images = artist.Images
    }