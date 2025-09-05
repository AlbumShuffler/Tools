module AlbumShuffler.DreiMetadaten.Sql

open System
open Microsoft.Data.Sqlite


type Target =
    | Regular
    | Special
    | Shorts
    | Kids
let targetAsTable =
    function
    | Regular -> "serie"
    | Special -> "spezial"
    | Shorts  -> "kurzgeschichten"
    | Kids    -> "kids"


/// <summary>
/// Opens a connection to the given database.
/// </summary>
let ``open`` (dbPath: string) =
    let connectionString = $"Data Source={dbPath}"
    let connection = new SqliteConnection(connectionString)
    do connection.Open()
    connection
    
    
/// <summary>
/// Closes the db connection, there is no explicit flush!
/// </summary>
let close (connection: SqliteConnection) =
    do connection.Close()
    
    
let getCompleteEpisodes (connection: SqliteConnection) (target: Target) =
    let table = target |> targetAsTable
    let hasNummer = match target with Regular -> true | Kids -> true | _ -> false
    let optionalNummerCol = if hasNummer then ", s.nummer" else ""
    let optionalCoverCheck = match target with Kids -> String.Empty | _ -> " AND (h.cover = 1)"
    use command = connection.CreateCommand()
    command.CommandText <- $"""
        SELECT h.hörspielID, h.titel, h.idDeezer%s{optionalNummerCol}
        FROM hörspiel h
        JOIN %s{table} s ON h.hörspielID = s.hörspielID
        WHERE h.unvollständig = 0 AND (h.idDeezer IS NULL OR h.idDeezer = 0) %s{optionalCoverCheck};"""
    
    use reader = command.ExecuteReader()
    
    let mutable results = []
    while reader.Read() do
        let hoerspielId = reader.GetInt32(0)
        let titel = reader.GetString(1)
        let nummer = if hasNummer then reader.GetInt32(3) |> Some else None
        results <- { Shared.DatabaseId = hoerspielId; Shared.Number = nummer; Shared.Title = titel } :: results
    
    results |> List.rev
    
    
/// <summary>
/// Checks if the 'hörspiel' table has the columns 'idDeezer' and 'urlCoverDeezer' and creates them if they don't
/// </summary>
let ensureHoerspielHasDeezerColumns (connection: SqliteConnection) =
    // Query current columns
    let checkForColumns () =
        use cmd = connection.CreateCommand()
        cmd.CommandText <- "PRAGMA table_info('hörspiel');"
        use reader = cmd.ExecuteReader()

        let mutable hasUrlCoverDeezer = false
        let mutable hasDeezerId = false

        while reader.Read() do
            let colName = reader.GetString(1)
            if colName.Equals("urlCoverDeezer", StringComparison.OrdinalIgnoreCase) then
                hasUrlCoverDeezer <- true
            elif colName.Equals("idDeezer", StringComparison.OrdinalIgnoreCase) then
                hasDeezerId <- true

        hasUrlCoverDeezer, hasDeezerId

    let addColumnIfMissing (columnName: string) =
        do printfn $"Altering table to add column: %s{columnName}"
        use cmd = connection.CreateCommand()
        cmd.CommandText <- $"ALTER TABLE \"hörspiel\" ADD COLUMN \"{columnName}\" TEXT;"
        cmd.ExecuteNonQuery() |> ignore

    let hasUrlCoverDeezer, hasDeezerId = checkForColumns()

    //if not hasUrlCoverDeezer then
    //    addColumnIfMissing "urlCoverDeezer"
    if not hasDeezerId then
        addColumnIfMissing "idDeezer"


/// <summary>
/// Updates the Deezer fields for a given episode identified by the DMD id if they are currently empty.
/// </summary>
/// <param name="connection">An open SqliteConnection.</param>
/// <param name="target">Target type in the database, refers to regular episodes, special episodes, ...</param>
/// <param name="hoerspielId">Episode number used to locate the record via serie.nummer.</param>
/// <param name="deezerId">The Deezer ID to set.</param>
/// <param name="deezerImageUrl">The Deezer cover image URL to set.</param>
/// <returns>Number of affected rows.</returns>
let upsertByHoerspielId (connection: SqliteConnection) (target: Target) (hoerspielId: int) (deezerId: string) (deezerImageUrl: string) =
    use command = connection.CreateCommand()
    let table = target |> targetAsTable
    let optionalCoverCheck = match target with Kids -> String.Empty | _ -> "AND (cover = 1)"
    //  REMOVED FROM QUERY: urlCoverDeezer = @urlCoverDeezer
    let commandText = $"""
        UPDATE hörspiel
        SET idDeezer = @deezerId
        WHERE hörspielID = (SELECT hörspielID FROM %s{table} WHERE hörspielID = @hoerspielId)
          AND (idDeezer IS NULL OR idDeezer = '')
          %s{optionalCoverCheck};"""
    command.CommandText <- commandText
    
    do command.Parameters.AddWithValue("@deezerId", deezerId) |> ignore
    do command.Parameters.AddWithValue("@urlCoverDeezer", deezerImageUrl) |> ignore
    do command.Parameters.AddWithValue("@hoerspielId", hoerspielId) |> ignore

    let result = command.ExecuteNonQuery()
    result
    