module AlbumShuffler.Shared.Inputs

open System

/// <summary>
/// A source is an API (or similar) that allows the retrieval of Content data.
/// Examples are Spotify, Deezer, Apple Music, ...
/// </summary>
type Source = {
    ProviderId: string
    ContentId: string
    Type: string
    IgnoreIds: string list
    ItemNameFilter: string list
}

/// <summary>
/// Content is an abstract definition of something that content that is available via multiple sources
/// </summary>
type Content = {
    ShortName: string
    Priority: int
    Icon: string
    CoverCenterX: int
    CoverCenterY: int
    AltCoverCenterX: int option
    AltCoverCenterY: int option
    CoverColorA: string
    CoverColorB: string
    Sources: Source list
}

/// <summary>
/// Provider configuration (Spotify, Deezer, ...) for input configurations
/// </summary>
type Provider = {
    Id: string
    Name: string
    Icon: string
    Logo: string
    AllowedTypes: string list
}

/// <summary>
/// Configuration that holds a complete provider and content list
/// </summary>
type Config = {
    Providers: Provider list
    Content: Content list
}


/// <summary>
/// Checks whether the given config has sources that do not match any of the providers.
/// </summary>
/// <returns>`None` if everything is in order, `Some errormessage` otherwise</returns>
let checkForUnknownSources (config: Config) : string option =
    let providerIds = config.Providers |> List.map _.Id
    let contentWithUnknownSource =
        config.Content |> List.where (fun content -> content.Sources |> List.exists (fun source -> providerIds |> (not << List.contains source.ProviderId)))
 
    if contentWithUnknownSource.IsEmpty then None
    else
        let mergedContent =
            String.Join(
                ", ",
                contentWithUnknownSource |> List.map (fun content ->
                    let mergedSourceNames = String.Join(", ", content.Sources |> List.map _.ProviderId)
                    $"%s{content.ShortName} (%s{mergedSourceNames})"))
        Some $"The following content references at least one provider that is unknown: %s{mergedContent}"
        
/// <summary>
/// Checks whether a given config contains any null values as fields. This indicates an incomplete config
/// </summary>
/// <returns>`None` if everything is in order, `Some errormessage` otherwise</returns>
let checkForNullFields (config: Config) : string option =
    let checkSourceForNull (source: Source) =
        let fields = source |> Records.getRecordPropertiesUnsafe
        if fields |> Array.exists (fun (_, v) -> v |> isNull) then
            Some $"The following source contains null values: %A{source}"
        else None
        
    let checkContentForNull (content: Content) =
        let nullFieldErrors =
            content
            |> Records.getRecordPropertiesUnsafe
            |> Array.where (fun (p,_) -> p.Name <> nameof(content.Sources))
            |> Array.where (fun (p,v) ->
                if p.PropertyType = typeof<int option> then false
                else if p.PropertyType = typeof<string option> then false
                else v |> isNull)
            |> Array.map (fun (p, _) -> $"%s{p.Name} contains null")
            |> List.ofArray
        
        let nullSources =
            content.Sources
            |> List.choose checkSourceForNull
        
        let errors = nullFieldErrors @ nullSources
        
        let name =
            if content.ShortName |> String.IsNullOrWhiteSpace then $"%A{content}"
            else content.ShortName
            
        if errors |> List.isEmpty then None
        else Some $"The content '%s{name}' has errors: %A{errors}"
        
    let checkProviderForNull (provider: Provider) =
        let fields = provider |> Records.getRecordPropertiesUnsafe
        if fields |> Array.exists (fun (_, v) -> v |> isNull) then
            Some $"The following source contains null values: %A{provider}"
        else None
        
    let errorMessages =
        (config.Content
        |> List.map checkContentForNull
        |> List.choose id)
        @
        (config.Providers
        |> List.map checkProviderForNull 
        |> List.choose id)
        
    if errorMessages.IsEmpty then None
    else
        let merged = String.Join(Environment.NewLine, errorMessages)
        Some merged
        
        
/// <summary>
/// Checks whether a given config is valid
/// </summary>
/// <returns>`None` if everything is in order, `Some errormessage` otherwise</returns>
let validateConfig (config: Config) =
    let errors =
        [ config |> checkForUnknownSources
          config |> checkForNullFields
        ] |> List.choose id
    if errors.IsEmpty then None
    else Some (String.Join(Environment.NewLine, errors))
    
