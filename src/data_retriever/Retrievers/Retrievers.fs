module AlbumShuffler.DataRetriever.Retrievers.Retrievers

open System.Threading.Tasks
open AlbumShuffler.Shared


/// <summary>
/// Single case union to store the name of the content as returned from the APIs
/// </summary>
type ContentNameFromProvider = ContentNameFromProvider of string
let contentNameValue = function ContentNameFromProvider s -> s
let asContentName = ContentNameFromProvider


type Retriever = Inputs.Source -> Task<Result<Intermediate.Artist * Outputs.Audiobook list, string>>
