module AlbumShuffler.ElmGenerator.AlbumStorages

open System
open AlbumShuffler.Shared
open Scriban

type OutputTriple = {
    CreationDate: DateTime
    ArtistInfo: Outputs.ArtistInfo
    Audiobooks: Outputs.Audiobook seq
}

type TemplateData = {
    ProviderName: string
    Outputs: OutputTriple seq
}

let createAlbumStorage (output: Outputs.Output) =
    let dataPairs =
        output.Audiobooks
        |> Seq.toList
        |> List.map (fun (artist, audiobooks) -> {
                OutputTriple.CreationDate = output.CreationDate
                OutputTriple.ArtistInfo = artist
                OutputTriple.Audiobooks = audiobooks
            })
    {
        TemplateData.ProviderName = output.Provider.Id.Replace("_dmd", "")
        TemplateData.Outputs = dataPairs
    }

let template = """
module AlbumStorage{{ provider_name | string.capitalizewords }} exposing (..)

import Array exposing(Array)
import Albums exposing (Album, ArtistInfo)
import ArtistIds exposing (ArtistId(..))
import AlbumIds exposing (AlbumId(..))
{{~ for output in outputs }}
artistInfo{{ output.artist_info.short_name | string.capitalize }} : ArtistInfo
artistInfo{{ output.artist_info.short_name | string.capitalize }} =
  { id = "{{ output.artist_info.id }}" |> ArtistId
  , name = "{{ output.artist_info.name }}" 
  , images = {{~ for image in output.artist_info.images }}
    {{ if for.first }}[{{ else }},{{ end }} { url = "{{ image.url }}", width = {{ image.width }}, height = {{ image.height }} } {{ end }}]
  , shortName = "{{ output.artist_info.short_name }}"
  , icon = "{{ output.artist_info.icon }}"
  , coverColorA = "{{ output.artist_info.cover_color_a }}"
  , coverColorB = "{{ output.artist_info.cover_color_b }}"
  , coverCenterX = {{ output.artist_info.cover_center_x }}
  , coverCenterY = {{ output.artist_info.cover_center_y }} 
  , altCoverCenterX = {{ if !output.artist_info.alt_cover_center_x }}Nothing{{ else }}{{ output.artist_info.alt_cover_center_x | string.replace "Some(" "Just " | string.replace ")" "" }}{{ end }} 
  , altCoverCenterY = {{ if !output.artist_info.alt_cover_center_y }}Nothing{{ else }}{{ output.artist_info.alt_cover_center_y | string.replace "Some(" "Just " | string.replace ")" "" }}{{ end }}
  , lastUpdated = "{{ output.creation_date | date.to_string "%F %R" }} MEZ/UTC+1"
  }

albumStorage{{ output.artist_info.short_name | string.capitalize }} : Array Album
albumStorage{{ output.artist_info.short_name | string.capitalize }} ={{ for book in output.audiobooks }}
  {{ if for.first }}[{{ else }},{{ end }} { id = "{{ book.id }}" |> AlbumId
    , name = "{{ book.name | string.replace `"` `\"` }}"
    , urlToOpen = "{{ book.url_to_open }}"
    , covers = {{ for image in book.images }}
        {{ if for.first }}[{{ else }},{{ end }} { url = "{{ image.url }}"
          , width = {{ image.width }}
          , height = {{ image.height }} } {{ if for.last }}]{{ end }}{{ end }}
    }{{ end }}
    ] |> Array.fromList
{{ end }}
"""