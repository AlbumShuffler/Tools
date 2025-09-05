open System

open AlbumShuffler.DreiMetadaten


/// <summary>
/// Entry point for the DreiMetadaten tool. Expects a single argument: path to the Deezer data JSON file.
/// It fetches current DMD data, matches Deezer entries against DMD (special and regular episodes),
/// prints a brief report, fails if any items remain unmatched, and prepares the target database for Deezer columns.
/// </summary>
[<EntryPoint>]
let main argv =
    if argv.Length <> 2 then failwith "This tool requires two parameters: the path to the deezer data file and the path to the dmd-sql file"
    
    // Before reading the database we need to make sure that th enew columns exist
    let db = argv[1] |> Sql.``open``
    do db |> Sql.ensureHoerspielHasDeezerColumns
    
    let inputs = Inputs.retrieve argv[0] argv[1]

    // --- Check the special episodes and print some brief feedback ---
    let specialMatches = (Matcher.matchSpecials inputs.specialsDeezer inputs.specialsDmd) |> Matcher.sequenceMatchResult
    do printfn $"Found %i{specialMatches.Matches.Length} matches for a total of %i{inputs.specialsDmd.Length} special episodes"
    if not <| specialMatches.KnownToNotExist.IsEmpty then printfn $"%i{specialMatches.KnownToNotExist.Length} special items are known to be unavailable on Deezer and were not matched"
    
    // --- Check the shorts episodes and print some brief feedback ---
    let shortsMatches = (Matcher.matchSpecials inputs.specialsDeezer inputs.shortsDmds) |> Matcher.sequenceMatchResult
    do printfn $"Found %i{shortsMatches.Matches.Length} matches for a total of %i{inputs.regularsDmd.Length} shorts episodes"
    if not <| shortsMatches.KnownToNotExist.IsEmpty then printfn $"%i{shortsMatches.KnownToNotExist.Length} shorts items are known to be unavailable on Deezer and were not matched"

    // --- Check the regular episodes and print some brief feedback ---
    let regularMatches = (Matcher.matchRegulars inputs.indexedRegularsDeezer inputs.regularsDmd) |> Matcher.sequenceMatchResult
    do printfn $"Found %i{regularMatches.Matches.Length} matches for a total of %i{inputs.regularsDmd.Length} regular episodes"
    if not <| regularMatches.KnownToNotExist.IsEmpty then printfn $"%i{regularMatches.KnownToNotExist.Length} regular items are known to be unavailable on Deezer and were not matched"
    
    let kidsMatches = (Matcher.matchRegulars inputs.indexedKidsDeezer inputs.kidsDmds) |> Matcher.sequenceMatchResult
    do printfn $"Found %i{kidsMatches.Matches.Length} matches for a total of %i{inputs.kidsDmds.Length} ddf kids episodes"
    if not <| kidsMatches.KnownToNotExist.IsEmpty then printfn $"%i{kidsMatches.KnownToNotExist.Length} ddf kids are known to be unavailable on Deezer and were not matched"
    
    // --- Fail if there are unmatched episodes ---
    let nonMatched = specialMatches.NoMatches @ regularMatches.NoMatches @ shortsMatches.NoMatches @ kidsMatches.NoMatches
    if not <| nonMatched.IsEmpty then failwith $"The following items were not matched: %s{Environment.NewLine}%s{String.Join(Environment.NewLine, nonMatched)}"

    // --- Do the actual work :) ---
    let numberOfSpecialUpdates =
        specialMatches.Matches
        |> List.sumBy (fun (dmd, deezer) ->
            let image = deezer |> Inputs.getLargestImageUrlFromDeezer
            Sql.upsertByHoerspielId db Sql.Target.Special dmd.DatabaseId deezer.Id image)
    do printfn $"Updated a total of %i{numberOfSpecialUpdates} rows (special episodes)"
    
    let numberOfShortsUpdates =
        shortsMatches.Matches
        |> List.sumBy (fun (dmd, deezer) ->
            let image = deezer |> Inputs.getLargestImageUrlFromDeezer
            Sql.upsertByHoerspielId db Sql.Target.Shorts dmd.DatabaseId deezer.Id image)
    do printfn $"Updated a total of %i{numberOfShortsUpdates} rows (shorts)"
    
    let numberOfRegularUpdates =
        regularMatches.Matches
        |> List.sumBy (fun (dmd, deezer) ->
            let image = deezer |> Inputs.getLargestImageUrlFromDeezer
            Sql.upsertByHoerspielId db Sql.Target.Regular dmd.DatabaseId deezer.Id image)
    do printfn $"Updated a total of %i{numberOfRegularUpdates} rows (regular episodes)"
    
    let numberOfKidsUpdate =
        kidsMatches.Matches
        |> List.sumBy (fun (dmd, deezer) ->
            // covers are no longer required
            Sql.upsertByHoerspielId db Sql.Target.Kids dmd.DatabaseId deezer.Id String.Empty)
    do printfn $"Updated a total of %i{numberOfKidsUpdate} rows (kids episodes)"
    
    do db |> Sql.close
    0

(*
// Function to open or create a new SQLite database
let openDatabase (dbPath: string) =
    let connection = new SQLiteConnection(sprintf "Data Source=%s;Version=3;" dbPath)
    connection.Open()
    connection

// Function to execute a non-query command (e.g., CREATE TABLE, INSERT)
let executeNonQuery (connection: SQLiteConnection) (sql: string) =
    use command = new SQLiteCommand(sql, connection)
    command.ExecuteNonQuery() |> ignore

// Function to query the database and return results
let queryDatabase (connection: SQLiteConnection) (sql: string) =
    let mutable results = []
    use command = new SQLiteCommand(sql, connection)
    use reader = command.ExecuteReader()
    while reader.Read() do
        // Assuming a single column result set for simplicity
        results <- results @ [reader.GetString(0)]
    results

// Main function to demonstrate CRUD operations
let main () =
    let dbPath = "example.db"
    let connection = openDatabase dbPath

    // Create a table
    executeNonQuery connection "CREATE TABLE IF NOT EXISTS Users (Id INTEGER PRIMARY KEY, Name TEXT);"

    // Insert data
    executeNonQuery connection "INSERT INTO Users (Name) VALUES ('Alice');"
    executeNonQuery connection "INSERT INTO Users (Name) VALUES ('Bob');"

    // Query data
    let users = queryDatabase connection "SELECT Name FROM Users;"
    printfn "Users: %A" users

    // Close the database connection
    connection.Close()

// Run the main function
*)