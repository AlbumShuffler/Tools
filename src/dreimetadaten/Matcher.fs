module AlbumShuffler.DreiMetadaten.Matcher

open System
open FsToolkit.ErrorHandling
open AlbumShuffler.Shared

/// <summary>
/// Map with known name differences between Deezer and DMD. DMD is the key, Deezer is the value.
/// </summary>
/// <hint>
/// This list is incomplete as some corrections are done on the fly
/// </hint>
let knownPairs =
    [
    ("master of chess (live & unplugged)","master of chess")
    ("brainwash: gefangene gedanken","brainwash - gefangene gedanken")
    ("house of horrors: haus der angst","house of horrors - haus der angst")
    ("high strung: unter hochspannung","high strung - unter hochspannung")
    ("und der 5. advent","der 5. advent")
    ("stille nacht, düstere nacht","adventskalender - stille nacht, düstere nacht")
    ("o du finstere","adventskalender - o du finstere")
    ("eine schreckliche bescherung","adventskalender - eine schreckliche bescherung")
    ("böser die glocken nie klingen","adventskalender - böser die glocken nie klingen")
    ("und der zeitgeist","und der zeitgeist (sechs kurzgeschichten)")
    ("und der schwarze tag","und der schwarze tag (sechs kurzgeschichten)")
    ("und der super-papagei 2004", "super-papagei 2004") 
    ]
    |> Map.ofList
       
       
let knownMissingDeezerEpisodes = [
    ] 


/// <summary>
/// Result of matching a single DMD album against Deezer data.
/// </summary>
type MatchResult =
    /// <summary>Successful match containing the DMD album and the matching Deezer audiobook.</summary>
    | Match of (Shared.DmdAudbiobook* Outputs.Audiobook)
    /// <summary>Explicitly indicates that no safe match was found; value describes the reason.</summary>
    | NoMatch of string
    /// <summary>The album is known to not exist on Deezer and should be skipped.</summary>
    | KnownToNotExist of Shared.DmdAudbiobook


/// <summary>
/// Aggregated lists of matching results across multiple items.
/// </summary>
/// <remarks>
/// Matches contains all successful pairs; NoMatches contains human-readable error messages;
/// KnownToNotExist includes items that are intentionally skipped.
/// </remarks>
type SequencedMatchResult = {
    Matches: (Shared.DmdAudbiobook * Outputs.Audiobook) list
    NoMatches: string list
    KnownToNotExist: Shared.DmdAudbiobook list
}


/// <summary>
/// Partitions a list of MatchResult values into grouped lists for matches, non-matches, and known-missing items.
/// </summary>
/// <param name="results">The list of per-item matching results.</param>
/// <returns>A SequencedMatchResult aggregating all results.</returns>
let sequenceMatchResult (results: MatchResult list) : SequencedMatchResult =
    let rec step remaining acc =
        match remaining with
        | [] -> acc
        | Match x :: tail -> step tail { acc with Matches = x :: acc.Matches  }
        | NoMatch x :: tail -> step tail { acc with NoMatches = x :: acc.NoMatches }
        | KnownToNotExist x :: tail -> step tail { acc with KnownToNotExist = x :: acc.KnownToNotExist }
    step results { Matches = []; NoMatches = []; KnownToNotExist = [] }


/// <summary>
/// Tries to find a match in the Deezer data for the given DMD audiobook
/// </summary>
let matchSpecial (deezerSpecials: Outputs.Audiobook list) (dmdSpecial: Shared.DmdAudbiobook) : MatchResult =
    let lower = dmdSpecial.Title.ToLowerInvariant()
    if knownMissingDeezerEpisodes |> List.contains lower then dmdSpecial |> KnownToNotExist
    else
        deezerSpecials
        |> List.tryFind (fun b -> b.Name.ToLowerInvariant() = lower)
        |> Option.orElse (
            knownPairs
            |> Map.tryFind lower
            |> Option.bind (fun pair ->
                deezerSpecials
                |> List.tryFind (fun b -> b.Name.ToLowerInvariant() = pair)
            )
        )
        |> function
           | Some b -> Match (dmdSpecial, b)
           // fail explicitly so that the user checks! We do not want to merge any wrong/illegal data
           | None -> NoMatch $"Did not find a match for '%s{dmdSpecial.Title}' in the Deezer data"


/// <summary>
/// Tries to find matches in the Deezer data for all DDF special releases in the database
/// Episodes for which no match is found are not included in the return value
/// </summary>
let matchSpecials (deezerSpecials: Outputs.Audiobook list) (dmdSpecials: Shared.DmdAudbiobook list) : MatchResult list =
    let findDeezer = matchSpecial deezerSpecials
    dmdSpecials |> List.map findDeezer


/// <summary>
/// Tries to find a match in the Deezer data for the given DMD audiobook
/// </summary>
let matchRegular (deezerRegulars: Map<int, Outputs.Audiobook>) (dmdRegular: Shared.DmdAudbiobook) : MatchResult =
    if knownMissingDeezerEpisodes |> List.contains (dmdRegular.Title.ToLowerInvariant()) then dmdRegular |> KnownToNotExist
    else
        match dmdRegular.Number with
        | Some number ->
            match deezerRegulars |> Map.tryFind number with
            | Some x -> Match (dmdRegular, x)
            | None -> NoMatch $"Could not find a match for '%s{dmdRegular.Title}', number '%A{dmdRegular.Number}'"
        | None ->
            NoMatch $"Cannot work on DMD date without Nummer! Title: %s{dmdRegular.Title}"


/// <summary>
/// Tries to find matches in the Deezer data for all regular DDF releases in the database
/// Episodes for which no match is found are not included in the return value
/// </summary>
let matchRegulars (deezerRegulars: Map<int, Outputs.Audiobook>) (dmdRegulars: Shared.DmdAudbiobook list) =
    let findDeezer = matchRegular deezerRegulars
    dmdRegulars
    |> List.map findDeezer
    
    

