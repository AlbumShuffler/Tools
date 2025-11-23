module AlbumShuffler.DataRetriever.Cli

open AlbumShuffler.Shared.Outputs

type private CommandLineArguments = {
    SpotifyClientId: string
    SpotifyClientSecret: string
    TidalClientId: string
    TidalClientSecret: string
    InputFile: string
    OutputFolder: string
}

type GlobalConfiguration = {
    InputFile: string
    OutputFolder: string
    SpotifyConfig: Retrievers.Spotify.SpotifyConfig
    TidalConfig: Retrievers.Tidal.TidalConfig
}


/// <summary>
/// Interprets the given arguments as the four required parameters: client id, client secret, input file, output directory.
/// </summary>
/// <returns>
/// Result.Ok if there are four parameters, Result.Error otherwise
/// </returns>
let private parseCommandLineArgs (args: string[]) : Result<CommandLineArguments, string> =
    match args with
    | [| spotifyClientId; spotifyClientSecret; tidalClientId; tidalClientSecret; inputFilePath; outputPath |] -> 
        Ok { 
            SpotifyClientId = spotifyClientId
            SpotifyClientSecret = spotifyClientSecret
            TidalClientId = tidalClientId
            TidalClientSecret = tidalClientSecret
            InputFile = inputFilePath
            OutputFolder = outputPath
        }
    | _ ->
        Error "Required arguments: <spotify-client-id> <spotify-client-secret> <tidal-client-id> <tidal-client-secret> <input-file-path> <output-path>"


let fromCliArgument (argv: string[]) =
    argv
    |> parseCommandLineArgs
    |> Result.map (fun arguments ->
        {
            InputFile = arguments.InputFile
            OutputFolder = arguments.OutputFolder
            SpotifyConfig = { ClientId = arguments.SpotifyClientId; ClientSecret = arguments.SpotifyClientSecret }
            TidalConfig = { ClientId = arguments.TidalClientId; ClientSecret = arguments.TidalClientSecret }
        })
    