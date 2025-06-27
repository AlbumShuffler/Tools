module AlbumShuffler.ElmGenerator.Cli

type CommandLineArguments = {
    InputFolder: string
    OutputFolder: string
}


type GlobalConfiguration = {
    InputFolder: string
    OutputFolder: string
}


/// <summary>
/// Interprets the given arguments as the four required parameters: client id, client secret, input file, output directory.
/// </summary>
/// <returns>
/// Result.Ok if there are four parameters, Result.Error otherwise
/// </returns>
let private parseCommandLineArgs (args: string[]) : Result<CommandLineArguments, string> =
    match args with
    | [| inputFolder; outputFolder |] -> 
        Ok { 
            InputFolder = inputFolder
            OutputFolder = outputFolder
        }
    | _ ->
        Error "Required arguments: <input-folder> <output-folder>"


let fromCliArgument (argv: string[]) =
    argv
    |> parseCommandLineArgs
    |> Result.map (fun arguments ->
        {
            InputFolder = arguments.InputFolder
            OutputFolder = arguments.OutputFolder
        })
    
