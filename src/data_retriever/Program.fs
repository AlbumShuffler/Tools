// For more information see https://aka.ms/fsharp-console-apps

open System
open System.IO
open System.Threading.Tasks
open AlbumShuffler.Shared
open AlbumShuffler.DataRetriever
open FsToolkit.ErrorHandling
open Spectre.Console


/// <summary>
/// Describes an intermediate product that is used in the retriever chain.
/// Is almost the same as a part of the <see cref="Outputs.Output"/> but contains a provider id for later matching
/// </summary>
type IntermediateRetrieverResult = {
    Artist: Outputs.ArtistInfo
    Audiobooks: Outputs.Audiobook list
    ProviderId: string
}


/// <summary>
/// Reads the contents of the given file.
/// </summary>
/// <returns>
/// Result.Ok if the file was read successfully, Result.Error otherwise
/// </returns>
let readFileContent (filePath: string) : Result<string, string> =
    try
        if File.Exists(filePath) then
            Ok(File.ReadAllText(filePath))
        else
            Error $"File not found: {filePath} (current working directory: {Environment.CurrentDirectory.TrimEnd([|'\r'; '\n'|])})"
    with
    | ex -> Error $"Error reading file: {ex.Message}"


/// <summary>
/// Tries to parse the given string as an <see cref="Input"/>
/// </summary>
/// <returns>
/// Result.Ok if the interpretation is successful, Result.Error otherwise
/// </returns>
let parseInput (json: string) : Result<Inputs.Config, string> =
    try
        let config = AlbumShuffler.Shared.Json.deserialize<Inputs.Config>(json)
        if obj.ReferenceEquals(config, null) then Error "Deserialization returned null."
        else Ok config
    with ex ->
        Error $"Failed to parse the input file: {ex.Message}"


let mergeSpotifyImages (spotify: Outputs.Audiobook list) otherProvider (other: Outputs.Audiobook list) : Result<Outputs.Audiobook list, string list> =
    
    let tryMatchRegular (book: Outputs.Audiobook) : Result<Outputs.Audiobook, string> =
        let s = spotify |> List.tryFind (fun sb -> String.Equals(sb.Id, book.Id, StringComparison.InvariantCultureIgnoreCase))
        match s with
        | Some spotifyBook -> Ok { book with Images = spotifyBook.Images }
        | None ->
            // Use the fallback image given by DieDreiMetadaten (which is usually huge!)
            if book.Images.IsEmpty then
                Error $"Could not find matching regular audiobook in Spotify data for %s{book.Name} (%s{book.Id}) (Provider: %s{otherProvider})"
            else Ok book
    
    let tryMatchSpecial (book: Outputs.Audiobook) : Result<Outputs.Audiobook, string> =
        let bookName = book.Name.ToLowerInvariant()
        match spotify |> List.tryFind _.Name.ToLowerInvariant().Contains(bookName) with
        | Some spotifyBook -> Ok { book with Images = spotifyBook.Images }
        | None ->
            // Use the fallback image given by DieDreiMetadaten (which is usually huge!)
            if book.Images.IsEmpty then
                Error $"Could not find matching special audiobook in Spotify data for %s{book.Name} (%s{book.Id}) (Provider: %s{otherProvider})"
            else Ok book
            
    other
    |> List.map (fun book ->
        book
        |> tryMatchRegular
        |> Result.orElseWith (fun _ -> book |> tryMatchSpecial))
    |> List.sequenceResultA
    

let mergeSpotifyImagesIntoDreiMetadatenStep (spotify: Outputs.Output) (otherOutput: Outputs.Output) : Result<Outputs.Output, string list> =
    let findMatchingSpotifyBooks (artist: Outputs.ArtistInfo) =
        spotify.Audiobooks
        |> Seq.tryPick (fun (a, audiobooks) ->
            if String.Equals(a.Id, artist.Id, StringComparison.InvariantCultureIgnoreCase) 
            then Some audiobooks
            else None)
            
    let processArtistBooks (artist: Outputs.ArtistInfo) (books: Outputs.Audiobook list) =
        match findMatchingSpotifyBooks artist with
        | Some spotifyBooks -> 
            mergeSpotifyImages spotifyBooks otherOutput.Provider.Name books
            |> Result.map (fun mergedBooks -> artist, mergedBooks)
        | None -> 
            Error [$"Could not find matching artist from Spotify for Artist '%s{artist.Name}' (Id: %s{artist.Id}) (Provider: %s{otherOutput.Provider.Name})"]
    
    otherOutput.Audiobooks
    |> Seq.toList
    |> List.map (fun (artist, audiobooks) -> processArtistBooks artist audiobooks)
    |> List.traverseResultA id
    |> Result.mapError (List.collect id)
    |> Result.map (fun updatedAudiobooks -> { otherOutput with Audiobooks = updatedAudiobooks })    


/// <summary>
/// The DreiMetadaten retriever does not contain good images. They are either retrieved using the web archive (slow)
/// or from the provider itself (causes lots of traffic because of high-def images).
/// Use this function to replace the images of dmd items with images from Spotify
/// </summary>
let mergeSpotifyImagesIntoDreiMetadaten (outputs: Outputs.Output list) : Result<Outputs.Output list, string list> =
    let spotifyData =
        outputs
        |> List.where (fun o -> String.Equals(o.Provider.Id, "spotify", StringComparison.InvariantCultureIgnoreCase))
        |> List.exactlyOne
    
    let nonSpotifyData = outputs |> List.except [spotifyData]
    let merger = mergeSpotifyImagesIntoDreiMetadatenStep spotifyData
    
    nonSpotifyData
    |> List.map (fun output -> if output.Provider.Id.ToLowerInvariant().EndsWith("_dmd") then output |> merger else Ok output)
    |> List.traverseResultA id
    |> Result.mapError (List.collect id)
    |> Result.map (fun r -> spotifyData :: r)


let operateForSingleContent (retrievers: Map<string, Retrievers.Retrievers.Retriever>) (ctx: ProgressContext) (content: Inputs.Content) =
    let tasks =
        content.Sources
        |> List.map (fun source ->
                let progressTrackerDescription = $"Downloading %s{content.ShortName} from %s{source.ProviderId} ... "
                let progressTracker = ctx.AddTask(progressTrackerDescription + "[yellow]<RUNNING>[/]", true, 1.0)
                let matchingRetriever = retrievers[source.ProviderId]
                source
                |> matchingRetriever
                |> TaskResult.map (fun (artist, books) ->
                        do progressTracker.Description <- progressTrackerDescription + $"[blue]%s{books.Length.ToString().PadLeft(3, ' ')}[/] ITEMS"
                        do progressTracker.StopTask()
                        let outputArtist = Intermediate.combineArtistAndInputContent artist content
                        { Artist = outputArtist; Audiobooks = books; ProviderId = source.ProviderId }
                    )
            )
    tasks |> List.sequenceTaskResultA


/// <summary>
/// Executes the data retrieval operation using the provided configuration.
/// </summary>
let operate creationDate (retrievers: Map<string, Retrievers.Retrievers.Retriever>) (ctx: ProgressContext) (config: Inputs.Config)  : Task<Result<Outputs.Output list, string list>> =
    
    let getSingleContent = operateForSingleContent retrievers ctx

    let matchingProvider providerId =
        config.Providers
        |> List.tryFind (fun p -> String.Equals(providerId, p.Id, StringComparison.InvariantCultureIgnoreCase))
        |> function
           | Some p -> Outputs.createProvider p.Id p.Name p.Icon p.Logo
           | None -> failwithf $"Could not find provider '%s{providerId}'"
    
    
    taskResult {
        let allResults = System.Collections.Generic.List<IntermediateRetrieverResult>()
        for content in config.Content do
            let! result = content |> getSingleContent
            do allResults.AddRange(result)
            
        let outputs =
            allResults
            |> Seq.groupBy _.ProviderId
            |> Seq.map (fun (providerId, results) ->
                let audiobooks =
                    results
                    |> Seq.map (fun result -> result.Artist, result.Audiobooks)
                    |> List.ofSeq
                let provider = providerId |> matchingProvider
                { Outputs.Output.CreationDate = creationDate
                  Outputs.Output.Provider = provider
                  Outputs.Output.Audiobooks = audiobooks })
            |> List.ofSeq
            
        let sortedOutputs =
            outputs
            |> List.map (fun output ->
                let sortedAudiobooks =
                    output.Audiobooks
                    |> List.sortBy (fun (artist, _) ->
                        let originalContent = config.Content |> List.find (fun c -> c.ShortName = artist.ShortName)
                        originalContent.Priority)
                        
                { output with Audiobooks = sortedAudiobooks })
        return sortedOutputs
    }
    

let configureRetrievers (spotify: Retrievers.Spotify.SpotifyConfig) = 
    let retrieverArray : (string * Retrievers.Retrievers.Retriever) array =
        [|
        "amazon", Retrievers.Amazon.retriever
        "amazon_dmd", Retrievers.DreiMetaDaten.retriever
        "apple", Retrievers.Apple.retriever
        "apple_dmd", Retrievers.DreiMetaDaten.retriever
        "deezer", Retrievers.Deezer.retriever
        "dmd", Retrievers.DreiMetaDaten.retriever
        "spotify", Retrievers.Spotify.retriever { ClientId = spotify.ClientId; ClientSecret = spotify.ClientSecret }
        "youtube", Retrievers.YoutubeMusic.retriever
        "youtube_dmd", Retrievers.DreiMetaDaten.retriever
        |]
    retrieverArray |> Map.ofArray


let writeToOutputFiles (outputFolder: string) (outputs: Outputs.Output list) : Result<unit, string> =
    try
        if Directory.Exists outputFolder then
            Directory.Delete(outputFolder, true)

        Directory.CreateDirectory(outputFolder) |> ignore

        outputs
        |> List.iter (fun output ->
            let filePath = Path.Combine(outputFolder, $"{output.Provider.Id}.json")
            let serialized = AlbumShuffler.Shared.Json.serialize output
            File.WriteAllText(filePath, serialized))
        Ok ()
    with
    | ex -> Error $"Failed to write output files: {ex.Message}"
    

[<EntryPoint>]
let main args =
    let writeInfo (message: string) =
        AnsiConsole.Write message
    
    let writeOk () =
        AnsiConsole.MarkupLine(" ... [green]OK[/]")
        
    (taskResult {
        do "Trying to get cli arguments" |> writeInfo
        let! config = args |> Cli.fromCliArgument |> Result.mapError List.singleton
        let retrievers = configureRetrievers config.SpotifyConfig
        do writeOk ()
        
        do "Trying to read input file" |> writeInfo
        let! rawJson = config.InputFile |> readFileContent |> Result.mapError List.singleton
        do writeOk ()
        
        do "Trying to parse input file" |> writeInfo
        let! (source : Inputs.Config) = rawJson |> parseInput |> Result.mapError List.singleton
        do writeOk ()
        
        do "Validating parsed input" |> writeInfo
        let! _ = match source |> Inputs.validateConfig with Some error -> Error [error] | _ -> Ok ()
        do writeOk ()
        
        // We want the creation time to be the same for all files so we define it here
        let creationDate = DateTime.Now
        let operation =
            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns([|TaskDescriptionColumn() :> ProgressColumn; SpinnerColumn() :> ProgressColumn; ElapsedTimeColumn() :> ProgressColumn|])
                .StartAsync<Result<Outputs.Output list, string list>>(fun ctx -> (operate creationDate retrievers ctx source))
                
        let! items = operation
        
        do "Enriching DreiMetadaten data with Spotify image data" |> writeInfo
        let! enrichedItems =
            if items |> List.exists (fun item -> item.Provider.Id = "spotify") then items |> mergeSpotifyImagesIntoDreiMetadaten
            else
                printf " (skipped because no Spotify data was retrieved)"
                items |> Ok
        do writeOk ()
        
        do "Writing retrieved data to files" |> writeInfo
        let! _ = enrichedItems |> writeToOutputFiles config.OutputFolder |> Result.mapError List.singleton
        do writeOk ()
        
        return 0
    } |> TaskResult.defaultWith (fun errors ->
        let error = String.Join(Environment.NewLine, errors)
        do AnsiConsole.MarkupLine(" ... [red]ERROR[/]")
        do AnsiConsole.WriteLine(error)
        1)).GetAwaiter().GetResult()