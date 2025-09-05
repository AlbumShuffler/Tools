module AlbumShuffler.DreiMetadaten.Inputs

open System
open AlbumShuffler.Shared

/// <summary>
/// Reads a previously created output file containing Deezer data
/// </summary>
let parseOutput (filename: string) =
    let rawJson = System.IO.File.ReadAllText(filename)
    let output = Json.deserialize<Outputs.Output>(rawJson)
    output.Audiobooks 
    |> List.find (fun (artist, _) -> artist.ShortName = "ddf")
    |> snd
    
    
let parseKidsOutput (filename: string) =
    let rawJson = System.IO.File.ReadAllText(filename)
    let output = Json.deserialize<Outputs.Output>(rawJson)
    output.Audiobooks 
    |> List.find (fun (artist, _) -> artist.ShortName = "ddfk")
    |> snd


/// <summary>
/// Captures three-digit numbers in strings. Makes sure that it does not capture four or five digit numbers
/// </summary>
[<Literal>]
let numberPattern = @"(?<!\d)\d{3}(?!\d)"

/// <summary>
/// Captures three or two-digit numbers in strings.
/// </summary>
[<Literal>]
let kidsNumberPattern = "(?<!\d)\d{2,3}(?!\d)"

/// <summary>
/// Uses a regular expression to partition the books by searching for three-digit-numbers
/// </summary> 
/// <returns>
/// Regular episodes as left, special episodes as right
/// </returns>
let splitIntoRegularAndSpecial (items: Outputs.Audiobook list) =
    items
    |> List.partition (fun book -> System.Text.RegularExpressions.Regex.IsMatch(book.Name, numberPattern))


/// <summary>
/// Retrievers the deezer data from a file and queries dreimetadaten for its current data
/// </summary>
let retrieve (deezerFile: string) (dbFile: string) =
    let deezerDdf =
        deezerFile |> parseOutput
        |> List.where (fun b -> not <| b.Name.Contains("Kopfhörer-Hörspiel"))
        
    let deezerKids =
        deezerFile |> parseKidsOutput
        |> List.where (fun b -> not <| b.Name.Contains("Adventskalender"))
        |> List.where (fun b -> not <| b.Name.Contains("Mini-Fall"))
    
    let regularBooks, specialBooks =
        deezerDdf |> splitIntoRegularAndSpecial
        
    let indexedRegularBooks =
        regularBooks
        |> List.map (fun book ->
            let number = System.Text.RegularExpressions.Regex.Match(book.Name, numberPattern)
            number.Value |> Int32.Parse, book)
        |> Map.ofList
        
    let indexedKidsBooks =
        deezerKids
        |> List.map (fun book ->
            let number = System.Text.RegularExpressions.Regex.Match(book.Name, kidsNumberPattern)
            try 
                number.Value |> Int32.Parse, book
            with
            | exn -> failwith $"Could not parse episode number from: %s{book.Name}"
            )
        |> Map.ofList
                
    let db = dbFile |> Sql.``open``
    let regularDmds = Sql.Regular |> Sql.getCompleteEpisodes db
    let specialDmds = Sql.Special |> Sql.getCompleteEpisodes db
    let shortsDmds  = Sql.Shorts  |> Sql.getCompleteEpisodes db
    let kidsDmds    = Sql.Kids    |> Sql.getCompleteEpisodes db
    
    {| shortsDmds = shortsDmds
       specialsDeezer = specialBooks
       specialsDmd = specialDmds
       indexedRegularsDeezer = indexedRegularBooks
       indexedKidsDeezer = indexedKidsBooks
       regularsDmd = regularDmds
       kidsDmds = kidsDmds |}
       
       
let getLargestImageUrlFromDeezer (deezer: Outputs.Audiobook) =
    deezer.Images
    |> List.sortByDescending _.Width
    |> List.tryHead
    |> Option.map _.Url
    |> Option.defaultValue String.Empty
