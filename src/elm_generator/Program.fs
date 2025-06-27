
open System
open AlbumShuffler.ElmGenerator
open AlbumShuffler.Shared
open FsToolkit.ErrorHandling
open Scriban
open Spectre.Console

let getJsonDataFromFolder folder : Result<string list, string> =
    if not <| (folder |> IO.Directory.Exists) then Error $"The given folder: '%s{folder}' does not exist"
    else
        let jsonFileNames = IO.Directory.GetFiles(folder, "*.json")
        if jsonFileNames |> Array.isEmpty then Error $"The given folder: %s{folder} did not contain any json files"
        else
            try
                jsonFileNames
                |> List.ofArray
                |> List.map IO.File.ReadAllText
                |> Ok
            with
            | exn -> Error $"Could not read json files in '%s{folder}' because: %s{exn.Message}"


let parseJsonData json : Result<Outputs.Output, string> =
    try
        json |> Json.deserialize<Outputs.Output> |> Ok
    with
    | exn -> Error $"Could not parse json data because: %s{exn.Message}"


let createAlbumStorageForOutput (output: Outputs.Output) : string * string =
    let albumStorageTemplate = Template.Parse(AlbumStorages.template)
    let albumStorageDate = output |> AlbumStorages.createAlbumStorage
    let filename = output.Provider.Id.Replace("_dmd", "") |> (fun s -> $"{Char.ToUpperInvariant(s[0])}{s.Substring(1)}")
    ($"AlbumStorage{filename}.elm", albumStorageTemplate.Render(albumStorageDate))
    
    
let writeFiles (baseDir: string) (filesWithContent: (string * string) list) : unit =
    try
        if not <| IO.Directory.Exists(baseDir) then failwith $"The output directory '%s{baseDir}' does not exist"
        filesWithContent
        |> List.iter (fun (filename, content) ->
                let filename = IO.Path.Combine(baseDir, filename)
                IO.File.WriteAllText(filename, content))
    with
    | exn -> failwith $"Could not write output files because: %s{exn.Message}"
    
    
[<EntryPoint>]
let main args =
    result {
        let! config = args |> Cli.fromCliArgument
        
        let! input =
            config.InputFolder
            |> getJsonDataFromFolder
            |> Result.bind (fun results ->
                results
                |> List.map parseJsonData
                |> List.sequenceResultA
                |> Result.mapError (fun errors -> String.Join(Environment.NewLine, errors)))
                |> Result.map (List.sortBy _.Provider.Name)
        
        let artistWithAlbumsTemplate = Template.Parse(ArtistsWithAlbums.template)
        let artistWithAlbumsData = input |> ArtistsWithAlbums.mapOutputsToTemplateData
        let artistWithAlbumsFileContent = artistWithAlbumsTemplate.Render(artistWithAlbumsData)
        
        let providerStorageTemplate = Template.Parse(ProviderStorages.template)
        let providerStorageData =
            {| Providers = input |> Seq.ofList |> Seq.map _.Provider
               Default = (input |> List.find (fun d -> d.Provider.Id = "spotify")).Provider
            |}
        let providerStorageFileContent = providerStorageTemplate.Render(providerStorageData)
        
        let albumStorages = input |> List.map createAlbumStorageForOutput
        
        let combined = ("ArtistsWithAlbums.elm", artistWithAlbumsFileContent) :: ("ProviderStorage.elm", providerStorageFileContent) :: albumStorages
        
        do combined |> writeFiles "/tmp/test"
        
        AnsiConsole.Markup("[green]OK[/]")
        
        
        return 0
    } |> Result.defaultWith (fun error ->
            AnsiConsole.Markup("[red]ERROR: [/]" + error)
            1
        )