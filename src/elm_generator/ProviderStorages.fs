module AlbumShuffler.ElmGenerator.ProviderStorages

open AlbumShuffler.Shared

type TemplateData = {
    Providers: Outputs.Provider seq
    Default: Outputs.Provider
}

let createTemplateData (data: Outputs.Output list) =
    {| Providers = data |> Seq.ofList |> Seq.map _.Provider
       Default = (data |> List.find (fun d -> d.Provider.Id = "spotify")).Provider
    |}

let template = """
module ProviderStorage exposing (..)

import Providers

all : List Providers.Provider
all =
{{ for provider in providers ~}}
    {{ if for.first }}[{{ else }},{{ end }} { name = "{{- provider.name }}", id = "{{ provider.id }}", icon = "{{ provider.icon }}", logo = "{{ provider.logo }}" }
{{~ end }}    ]

default : Providers.Provider
default =
    { name = "{{ default.name }}", id = "{{ default.id }}", icon = "{{ default.icon }}", logo = "{{ default.logo }}" }
"""

