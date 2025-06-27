module AlbumShuffler.Shared.Records

open System.Reflection
open Microsoft.FSharp.Reflection
        
        
/// <summary>
/// Returns all properties with their respective values. This will return null values!
/// </summary>
/// <param name="record"></param>
let getRecordPropertiesUnsafe (record: 'a) =
    if FSharpType.IsRecord(typeof<'a>, BindingFlags.Public ||| BindingFlags.Instance) then
        let fields = FSharpType.GetRecordFields(typeof<'a>, BindingFlags.Public ||| BindingFlags.Instance)
        let values =
            FSharpValue.GetRecordFields(record)
        Array.zip fields values
    else
        failwith "Not a record type"


/// <summary>
/// Returns all properties with their respective values. Null values are converted into option types
/// </summary>
/// <param name="record"></param>
let getRecordProperties (record: 'a) =
    record
    |> getRecordPropertiesUnsafe
    |> Array.map (fun (p, maybeNull) -> if maybeNull |> isNull then (p, None) else (p, Some (maybeNull)))


/// <summary>
/// Gets the name of all fields of a record.
/// </summary>
let getRecordFieldNames<'a>() =
    if FSharpType.IsRecord(typeof<'a>, BindingFlags.Public ||| BindingFlags.Instance) then
        FSharpType.GetRecordFields(typeof<'a>, BindingFlags.Public ||| BindingFlags.Instance)
        |> Array.map _.Name
        |> List.ofArray
    else
        failwithf $"'%s{typeof<'a>.FullName}' is not a record type"