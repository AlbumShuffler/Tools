module AlbumShuffler.Shared.Json

open System.Text.Json
open System.Text.Json.Serialization

let serializerOptions =
        let options =
            JsonFSharpOptions.Default()
                .WithSkippableOptionFields()
                .WithAllowNullFields()
                .ToJsonSerializerOptions()
        do options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        do options.WriteIndented <- true
        do options.PropertyNameCaseInsensitive <- true
        options


let deserialize<'a> (json: string) =
    JsonSerializer.Deserialize<'a>(json, serializerOptions)


let serialize obj =
    JsonSerializer.Serialize(obj, serializerOptions)