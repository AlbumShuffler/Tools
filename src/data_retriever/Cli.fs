module AlbumShuffler.DataRetriever.Cli

open AlbumShuffler.Shared.Outputs

type private CommandLineArguments = {
    SpotifyClientId: string
    SpotifyClientSecret: string
    InputFile: string
    OutputFolder: string
}

type GlobalConfiguration = {
    InputFile: string
    OutputFolder: string
    SpotifyConfig: Retrievers.Spotify.SpotifyConfig
}


/// <summary>
/// Interprets the given arguments as the four required parameters: client id, client secret, input file, output directory.
/// </summary>
/// <returns>
/// Result.Ok if there are four parameters, Result.Error otherwise
/// </returns>
let private parseCommandLineArgs (args: string[]) : Result<CommandLineArguments, string> =
    match args with
    | [| clientId; clientSecret; inputFilePath; outputPath |] -> 
        Ok { 
            SpotifyClientId = clientId
            SpotifyClientSecret = clientSecret
            InputFile = inputFilePath
            OutputFolder = outputPath
        }
    | _ ->
        Error "Required arguments: <client-id> <client-secret> <input-file-path> <output-path>"


let fromCliArgument (argv: string[]) =
    argv
    |> parseCommandLineArgs
    |> Result.map (fun arguments ->
        {
            InputFile = arguments.InputFile
            OutputFolder = arguments.OutputFolder
            SpotifyConfig = { ClientId = arguments.SpotifyClientId; ClientSecret = arguments.SpotifyClientSecret }
        })
    