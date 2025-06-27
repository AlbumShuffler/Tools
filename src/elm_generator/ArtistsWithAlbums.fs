module AlbumShuffler.ElmGenerator.ArtistsWithAlbums

open AlbumShuffler.Shared

type OutputPair = {
    ArtistInfo: Outputs.ArtistInfo
    Audiobooks: Outputs.Audiobook seq
}

type DataForProvider = {
    Provider: Outputs.Provider
    Pairs: OutputPair seq
}

type TemplateData = {
    Outputs: DataForProvider seq
}

let private convertOutputToTemplateData (output: Outputs.Output) : DataForProvider =
    let pairs =
        output.Audiobooks
        |> Seq.map (fun (artist, audiobooks) ->
            {
                ArtistInfo = artist
                Audiobooks = audiobooks :> seq<_>
            })
    {
        Provider = output.Provider
        Pairs = pairs
    }
    
let mapOutputsToTemplateData (outputs: Outputs.Output list) : TemplateData =
    {
        Outputs =
            outputs
            |> List.map convertOutputToTemplateData
    }


let template = """
module ArtistsWithAlbums exposing ( albumStorage)

import Dict exposing (Dict)
import Albums exposing (ArtistWithAlbums)
{{ for output in outputs }}
import AlbumStorage{{ output.provider.id | string.replace "_dmd" "" | string.capitalizewords -}}
{{ end }}

albumStorage : Dict String (List ArtistWithAlbums)
albumStorage =
    Dict.fromList [
{{~ for output in outputs ~}}{{ provider_id = output.provider.id | string.replace "_dmd" "" }}{{ provider_id_cap = provider_id | string.capitalizewords ~}}
        ("{{ output.provider.id }}",
            {{~ for pair in output.pairs }}{{ artist_id = pair.artist_info.id ~}}
            {{ if for.first }}[{{ else }},{{ end }}{ artist = AlbumStorage{{ provider_id_cap }}.artistInfo{{ pair.artist_info.short_name | string.capitalizewords }}, albums = AlbumStorage{{ provider_id_cap }}.albumStorage{{ pair.artist_info.short_name | string.capitalizewords }} }
{{ end }}            ]){{ if !for.last }},{{ end }}
{{~ end ~}}
    ]
"""